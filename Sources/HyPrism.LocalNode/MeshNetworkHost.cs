// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using HyPrism.Mesh;
using Microsoft.Extensions.Hosting;

namespace HyPrism.LocalNode;

/// <summary>
/// Runs signed LAN discovery and encrypted pairwise UDP delivery for active autonomous profiles
/// </summary>
public sealed class MeshNetworkHost : BackgroundService
{
    private const int PresenceProtocolVersion = 1;
    private readonly SemaphoreSlim _activationGate = new(1, 1);
    private readonly ConcurrentDictionary<string, ProfileTransport> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ProfileTransport> _profilesByPeerId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<PeerRouteKey, AuthenticatedEndpoint> _endpoints = [];
    private readonly ConcurrentDictionary<PeerRouteKey, MeshPeerPresence> _presence = [];
    private readonly ConcurrentDictionary<PeerRouteKey, TaskCompletionSource<MeshPeerPresence>> _presenceWaiters = [];
    private readonly byte[] _pairingCookieKey = RandomNumberGenerator.GetBytes(32);
    private readonly Channel<MeshApplicationDelivery> _applicationMessages;
    private readonly MainlineDhtPeerLocator _dht;
    private readonly MeshFriendService _friends;
    private readonly ConcurrentDictionary<PairingRequestKey, AcceptedFriendRequest> _acceptedFriendRequests = [];
    private readonly ConcurrentDictionary<PairingRequestKey, IncomingFriendRequest> _incomingFriendRequests = [];
    private readonly ConcurrentDictionary<PairingRequestKey, OutgoingFriendRequest> _outgoingFriendRequests = [];
    private readonly LocalNodeLog? _log;
    private readonly MeshSecurityLimits _limits;
    private readonly MeshTransportOptions _options;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeProvider _timeProvider;
    private CancellationToken _stoppingToken;
    private UdpClient? _discoverySocket;
    private UdpClient? _transportSocket;

    public MeshNetworkHost(
        string dataDirectory,
        MeshTransportOptions? options = null,
        LocalNodeLog? log = null,
        TimeProvider? timeProvider = null,
        MeshSecurityLimits? limits = null,
        MeshFriendService? friends = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _options = options ?? new MeshTransportOptions();
        _options.Validate();
        _limits = limits ?? new MeshSecurityLimits();
        _limits.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _friends = friends ?? new MeshFriendService(dataDirectory, _timeProvider, _limits);
        _dht = new MainlineDhtPeerLocator(_options, log);
        _log = log;
        _applicationMessages = Channel.CreateBounded<MeshApplicationDelivery>(
            new BoundedChannelOptions(_limits.MaximumInboundQueueDepth * _limits.MaximumActiveProfiles)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false
            });
    }

    public int TransportPort { get; private set; }

    /// <summary>
    /// Returns the stable short identifier for one active profile
    /// </summary>
    public string? GetFriendId(string profileId)
        => _profiles.TryGetValue(profileId, out var profile)
            ? profile.Identity.FriendId
            : null;

    /// <summary>
    /// Returns verified pending requests received by one local profile
    /// </summary>
    public IReadOnlyList<MeshFriendRequestSnapshot> GetIncomingFriendRequests(string profileId)
    {
        PruneFriendRequests();
        return _incomingFriendRequests
            .Where(item => string.Equals(item.Key.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Value.Snapshot)
            .OrderByDescending(item => item.CreatedAt)
            .ToArray();
    }

    /// <summary>
    /// Returns pending requests sent by one local profile
    /// </summary>
    public IReadOnlyList<MeshFriendRequestSnapshot> GetOutgoingFriendRequests(string profileId)
    {
        PruneFriendRequests();
        return _outgoingFriendRequests
            .Where(item => string.Equals(item.Key.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Value.Snapshot)
            .OrderByDescending(item => item.CreatedAt)
            .ToArray();
    }

    /// <summary>
    /// Starts serverless rendezvous for a Friend ID and sends a signed request to discovered candidates
    /// </summary>
    public async Task<MeshResult<MeshFriendRequestSnapshot>> SendFriendRequestAsync(
        string profileId,
        string friendId,
        CancellationToken cancellationToken = default)
    {
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!_profiles.TryGetValue(profileId, out var profile))
        {
            return MeshResult<MeshFriendRequestSnapshot>.Failed(
                "profile_not_active",
                "The local Mesh profile is not active");
        }
        if (!MeshFriendId.TryNormalize(friendId, out var normalized))
            return MeshResult<MeshFriendRequestSnapshot>.Failed("unknown_username", "The Friend ID is invalid");
        if (string.Equals(normalized, profile.Identity.FriendId, StringComparison.Ordinal))
            return MeshResult<MeshFriendRequestSnapshot>.Failed("self_target", "A profile cannot friend itself");
        if (profile.Friends.Values.Any(friend => string.Equals(
                MeshFriendId.FromSigningPublicKey(friend.SigningPublicKey),
                normalized,
                StringComparison.Ordinal)))
        {
            return MeshResult<MeshFriendRequestSnapshot>.Failed("already_friends", "This profile is already a friend");
        }
        if (_outgoingFriendRequests.Values.Any(request =>
                string.Equals(request.ProfileId, profileId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(request.TargetFriendId, normalized, StringComparison.Ordinal)))
        {
            return MeshResult<MeshFriendRequestSnapshot>.Failed(
                "duplicate_pending_invite",
                "A request for this Friend ID is already pending");
        }

        var invite = await _friends.CreateInviteForFriendIdAsync(
            profileId,
            profile.DisplayName,
            normalized,
            TimeSpan.FromMinutes(10),
            cancellationToken).ConfigureAwait(false);
        if (!invite.IsSuccess)
            return MeshResult<MeshFriendRequestSnapshot>.Failed(invite.Failure.Code, invite.Failure.Message);

        var requestId = Guid.NewGuid().ToString("N");
        var now = _timeProvider.GetUtcNow();
        var snapshot = new MeshFriendRequestSnapshot(
            Guid.ParseExact(requestId, "N"),
            Guid.TryParse(profileId, out var requesterUuid) ? requesterUuid : Guid.Empty,
            null,
            normalized,
            now,
            null);
        var pending = new OutgoingFriendRequest(
            profileId,
            normalized,
            requestId,
            invite.Value.Token,
            invite.Value.ExpiresAt,
            snapshot);
        _outgoingFriendRequests[new PairingRequestKey(profileId, requestId)] = pending;
        _log?.Info($"Mesh friend request {requestId} started for Friend ID {normalized}");

        var packet = MeshPairingProtocol.Encode(
            new MeshPairingMessage(1, "probe", requestId, normalized),
            _limits);
        await SendPairingToDiscoveryTargetsAsync(packet, cancellationToken).ConfigureAwait(false);
        pending.Delivery = DeliverOutgoingRequestAsync(pending, packet, _stoppingToken);
        return MeshResult<MeshFriendRequestSnapshot>.Success(snapshot);
    }

    /// <summary>
    /// Accepts a verified incoming request and sends the signed acceptance to its issuer
    /// </summary>
    public async Task<MeshResult<MeshFriendRequestSnapshot>> AcceptFriendRequestAsync(
        string profileId,
        Guid requesterUuid,
        CancellationToken cancellationToken = default)
    {
        PruneFriendRequests();
        var match = _incomingFriendRequests.FirstOrDefault(item =>
            string.Equals(item.Key.ProfileId, profileId, StringComparison.OrdinalIgnoreCase)
            && item.Value.Snapshot.RequesterUuid == requesterUuid);
        if (match.Value is null || !_profiles.TryGetValue(profileId, out var profile))
            return MeshResult<MeshFriendRequestSnapshot>.Failed("request_not_found", "The friend request was not found");

        var acceptance = await _friends.AcceptInviteAsync(
            profileId,
            profile.DisplayName,
            match.Value.InviteToken,
            cancellationToken).ConfigureAwait(false);
        if (!acceptance.IsSuccess)
            return MeshResult<MeshFriendRequestSnapshot>.Failed(acceptance.Failure.Code, acceptance.Failure.Message);

        var packet = MeshPairingProtocol.Encode(
            new MeshPairingMessage(1, "accept", match.Value.RequestId, Token: acceptance.Value.AcceptanceToken),
            _limits);
        _acceptedFriendRequests[match.Key] = new AcceptedFriendRequest(
            acceptance.Value.AcceptanceToken,
            acceptance.Value.ExpiresAt);
        await SendPairingRepeatedlyAsync(packet, match.Value.Endpoint, cancellationToken).ConfigureAwait(false);
        _incomingFriendRequests.TryRemove(match.Key, out _);
        await RefreshProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        _log?.Info($"Mesh friend request {match.Value.RequestId} accepted locally");
        return MeshResult<MeshFriendRequestSnapshot>.Success(match.Value.Snapshot with
        {
            AcceptedAt = _timeProvider.GetUtcNow()
        });
    }

    /// <summary>
    /// Rejects a verified incoming request and notifies its issuer
    /// </summary>
    public async Task<bool> RejectFriendRequestAsync(
        string profileId,
        Guid requesterUuid,
        CancellationToken cancellationToken = default)
    {
        var match = _incomingFriendRequests.FirstOrDefault(item =>
            string.Equals(item.Key.ProfileId, profileId, StringComparison.OrdinalIgnoreCase)
            && item.Value.Snapshot.RequesterUuid == requesterUuid);
        if (match.Value is null || !_profiles.TryGetValue(profileId, out var profile))
            return false;
        var proof = await _friends.CreatePeerRecordAsync(
            profileId,
            profile.DisplayName,
            match.Value.RequestId,
            "reject",
            TimeSpan.FromMinutes(2),
            cancellationToken).ConfigureAwait(false);
        if (!proof.IsSuccess)
            return false;
        var packet = MeshPairingProtocol.Encode(
            new MeshPairingMessage(1, "reject", match.Value.RequestId, Token: proof.Value.Token),
            _limits);
        await SendPairingRepeatedlyAsync(packet, match.Value.Endpoint, cancellationToken).ConfigureAwait(false);
        var removed = _incomingFriendRequests.TryRemove(match.Key, out _);
        if (removed)
            _log?.Info($"Mesh friend request {match.Value.RequestId} rejected locally");
        return removed;
    }

    public async Task<MeshPublicIdentity> ActivateProfileAsync(
        string profileId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        await _activationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_profiles.TryGetValue(profileId, out var existing))
            {
                existing.DisplayName = ValidateDisplayName(displayName);
                await RefreshFriendsAsync(existing, cancellationToken).ConfigureAwait(false);
                await AnnounceProfileAsync(existing, cancellationToken).ConfigureAwait(false);
                await SendPresenceToKnownFriendsAsync(existing, true, cancellationToken).ConfigureAwait(false);
                return existing.Identity;
            }

            if (_profiles.Count >= _limits.MaximumActiveProfiles)
                throw new InvalidOperationException("The Local Node mesh profile limit was reached");

            var identity = await _friends.GetIdentityAsync(profileId, cancellationToken).ConfigureAwait(false);
            var envelopes = new MeshEnvelopeService(_friends, _timeProvider, _limits);
            var pipeline = new MeshInboundPipeline(envelopes, _timeProvider, _limits);
            var profile = new ProfileTransport(
                profileId,
                ValidateDisplayName(displayName),
                identity,
                envelopes,
                new MeshDiscoveryService(_friends, _timeProvider, _limits),
                pipeline);
            await RefreshFriendsAsync(profile, cancellationToken).ConfigureAwait(false);
            if (!_profiles.TryAdd(profileId, profile))
            {
                profile.Dispose();
                throw new InvalidOperationException("The mesh profile is already active");
            }
            if (!_profilesByPeerId.TryAdd(identity.PeerId, profile))
            {
                _profiles.TryRemove(new KeyValuePair<string, ProfileTransport>(profileId, profile));
                profile.Dispose();
                throw new InvalidOperationException("The mesh Peer ID is already active");
            }

            profile.Consumer = ConsumeInboundAsync(profile, _stoppingToken);
            await AnnounceProfileAsync(profile, cancellationToken).ConfigureAwait(false);
            await SendPresenceToKnownFriendsAsync(profile, true, cancellationToken).ConfigureAwait(false);
            QueueDhtRefresh(profile);
            _log?.Info(
                $"Mesh profile {identity.PeerId} with Friend ID {identity.FriendId} activated on UDP port {TransportPort}");
            return identity;
        }
        finally
        {
            _activationGate.Release();
        }
    }

    public async Task RefreshProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        if (_profiles.TryGetValue(profileId, out var profile))
        {
            await RefreshFriendsAsync(profile, cancellationToken).ConfigureAwait(false);
            await AnnounceProfileAsync(profile, cancellationToken).ConfigureAwait(false);
        }
    }

    public IReadOnlyList<MeshPeerPresence> GetPresence(string profileId)
    {
        if (!_profiles.TryGetValue(profileId, out var profile))
            return [];

        var now = _timeProvider.GetUtcNow();
        return profile.Friends.Values
            .Select(friend =>
            {
                var key = new PeerRouteKey(profile.Identity.PeerId, friend.PeerId);
                return _presence.TryGetValue(key, out var current)
                    ? current with
                    {
                        DisplayName = friend.DisplayName,
                        IsOnline = current.IsOnline && now - current.LastSeenAt <= _options.PresenceTimeout
                    }
                    : null;
            })
            .OfType<MeshPeerPresence>()
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Sends one authenticated application message to a discovered confirmed friend
    /// </summary>
    public async Task<bool> SendAsync(
        string profileId,
        string peerId,
        MeshMessageKind kind,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        if (kind == MeshMessageKind.Presence)
            throw new ArgumentOutOfRangeException(nameof(kind), "Presence is managed by the mesh host");
        if (payload.IsEmpty || payload.Length > _limits.MaximumEnvelopePayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(payload), "The mesh message payload exceeds its limit");

        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!_profiles.TryGetValue(profileId, out var profile)
            || !profile.Friends.ContainsKey(peerId))
        {
            return false;
        }

        var route = new PeerRouteKey(profile.Identity.PeerId, peerId);
        if (!_endpoints.TryGetValue(route, out var endpoint)
            || !endpoint.IsAuthenticated
            || _timeProvider.GetUtcNow() - endpoint.SeenAt > _options.EndpointLifetime)
        {
            return false;
        }

        var sealedResult = await profile.Envelopes.SealAsync(
            profile.ProfileId,
            peerId,
            kind,
            payload,
            cancellationToken).ConfigureAwait(false);
        if (!sealedResult.IsSuccess)
            return false;

        var packet = MeshTransportProtocol.EncodeEnvelope(
            profile.Identity.PeerId,
            peerId,
            sealedResult.Value,
            _limits);
        await _transportSocket!.SendAsync(packet, endpoint.Endpoint, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Reads authenticated non-presence messages accepted by active profiles
    /// </summary>
    public async IAsyncEnumerable<MeshApplicationDelivery> ReadApplicationMessagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var delivery in _applicationMessages.Reader
                           .ReadAllAsync(cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return delivery;
        }
    }

    public async Task<MeshPeerPresence> WaitForPresenceAsync(
        string profileId,
        string peerId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        if (!_profiles.TryGetValue(profileId, out var profile)
            || !profile.Friends.ContainsKey(peerId))
        {
            throw new InvalidOperationException("The requested mesh friend is not active for this profile");
        }

        var route = new PeerRouteKey(profile.Identity.PeerId, peerId);
        var current = GetCurrentOnlinePresence(route);
        if (current is not null)
            return current;

        var waiter = _presenceWaiters.GetOrAdd(
            route,
            static _ => new TaskCompletionSource<MeshPeerPresence>(
                TaskCreationOptions.RunContinuationsAsynchronously));
        current = GetCurrentOnlinePresence(route);
        if (current is not null)
        {
            if (_presenceWaiters.TryRemove(route, out var completed))
                completed.TrySetResult(current);
            return current;
        }

        return await waiter.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _stoppingToken = stoppingToken;
            _discoverySocket = CreateDiscoverySocket();
            _transportSocket = CreateTransportSocket();
            TransportPort = ((IPEndPoint)_transportSocket.Client.LocalEndPoint!).Port;
            _ready.TrySetResult();

            await Task.WhenAll(
                ReceiveDiscoveryAsync(stoppingToken),
                ReceiveTransportAsync(stoppingToken),
                AnnouncePeriodicallyAsync(stoppingToken),
                RefreshDhtPeriodicallyAsync(stoppingToken)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _log?.Info("Mesh transport stopped");
        }
        catch (Exception exception)
        {
            _ready.TrySetException(exception);
            _log?.Error($"Mesh transport failed: {exception}");
            return;
        }
        finally
        {
            _applicationMessages.Writer.TryComplete();
            foreach (var profile in _profiles.Values)
                profile.Dispose();
            Interlocked.Exchange(ref _discoverySocket, null)?.Dispose();
            Interlocked.Exchange(ref _transportSocket, null)?.Dispose();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_transportSocket is not null)
        {
            try
            {
                foreach (var profile in _profiles.Values)
                    await SendPresenceToKnownFriendsAsync(profile, false, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
            {
                _log?.Warning($"Could not send final mesh presence: {exception.Message}");
            }
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        var consumers = _profiles.Values
            .Select(profile => profile.Consumer)
            .Where(task => task is not null)
            .Cast<Task>()
            .ToArray();
        if (consumers.Length > 0)
            await Task.WhenAll(consumers).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private UdpClient CreateDiscoverySocket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.ExclusiveAddressUse = false;
        socket.Bind(new IPEndPoint(IPAddress.Any, _options.DiscoveryPort));
        if (_options.EnableMulticast)
        {
            socket.SetSocketOption(
                SocketOptionLevel.IP,
                SocketOptionName.AddMembership,
                new MulticastOption(MeshTransportOptions.DefaultMulticastAddress, IPAddress.Any));
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);
        }
        return new UdpClient { Client = socket };
    }

    private UdpClient CreateTransportSocket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Any, _options.TransportPort));
        return new UdpClient { Client = socket };
    }

    private async Task ReceiveDiscoveryAsync(CancellationToken cancellationToken)
    {
        var limiter = new MeshIpRateLimiter(_limits, _timeProvider);
        while (!cancellationToken.IsCancellationRequested)
        {
            var received = await _discoverySocket!.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (MeshPairingProtocol.TryParse(received.Buffer, _limits, out var pairing))
            {
                if (limiter.TryConsume(received.RemoteEndPoint.Address))
                    await HandlePairingAsync(pairing, received.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (received.Buffer.Length > _limits.MaximumDiscoveryPacketBytes
                || !MeshDiscoveryService.TryReadRoute(received.Buffer, out var routeKey))
            {
                continue;
            }

            List<(ProfileTransport Profile, MeshDiscoveryRoute Route)>? routes = null;
            foreach (var profile in _profiles.Values)
            {
                if (profile.TryGetDiscoveryRoute(routeKey, out var route))
                    (routes ??= []).Add((profile, route));
            }
            if (routes is null || !limiter.TryConsume(received.RemoteEndPoint.Address))
                continue;

            foreach (var match in routes)
            {
                var profile = match.Profile;
                var verified = profile.Discovery.VerifyAnnouncement(received.Buffer, match.Route);
                if (!verified.IsSuccess)
                    continue;
                var senderPeerId = verified.Value.PeerId;
                if (!profile.Friends.TryGetValue(senderPeerId, out var friend))
                    continue;

                var route = new PeerRouteKey(profile.Identity.PeerId, senderPeerId);
                var discoveredEndpoint = new IPEndPoint(
                    received.RemoteEndPoint.Address,
                    verified.Value.TransportPort);
                _endpoints.AddOrUpdate(
                    route,
                    _ => new AuthenticatedEndpoint(discoveredEndpoint, _timeProvider.GetUtcNow(), false),
                    (_, current) => UpdateDiscoveredEndpoint(current, discoveredEndpoint));
                await SendPresenceAsync(profile, friend, true, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ReceiveTransportAsync(CancellationToken cancellationToken)
    {
        var limiter = new MeshIpRateLimiter(_limits, _timeProvider);
        while (!cancellationToken.IsCancellationRequested)
        {
            var received = await _transportSocket!.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (_dht.TryHandleResponse(received.Buffer, received.RemoteEndPoint))
                continue;
            if (!limiter.TryConsume(received.RemoteEndPoint.Address))
                continue;
            if (MeshPairingProtocol.TryParse(received.Buffer, _limits, out var pairing))
            {
                await HandlePairingAsync(pairing, received.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (received.Buffer.Length > _limits.MaximumInboundEnvelopeBytes + 512
                || !MeshTransportProtocol.TryParseEnvelope(
                    received.Buffer,
                    _limits,
                    out var senderPeerId,
                    out var recipientPeerId,
                    out var envelope)
                || !_profilesByPeerId.TryGetValue(recipientPeerId, out var profile)
                || !profile.Friends.ContainsKey(senderPeerId))
            {
                continue;
            }

            var route = new PeerRouteKey(recipientPeerId, senderPeerId);
            if (!_endpoints.TryGetValue(route, out var authenticated)
                || _timeProvider.GetUtcNow() - authenticated.SeenAt > _options.EndpointLifetime
                || !Equals(authenticated.Endpoint.Address, received.RemoteEndPoint.Address)
                || authenticated.Endpoint.Port != received.RemoteEndPoint.Port)
            {
                continue;
            }

            profile.Pipeline.TryEnqueue(
                senderPeerId,
                envelope.Span,
                FormatEndpoint(received.RemoteEndPoint));
        }
    }

    private async Task ConsumeInboundAsync(ProfileTransport profile, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var delivery in profile.Pipeline
                               .ReadDeliveriesAsync(profile.ProfileId, cancellationToken)
                               .ConfigureAwait(false))
            {
                var message = delivery.Message;
                if (message.Kind != MeshMessageKind.Presence)
                {
                    _applicationMessages.Writer.TryWrite(new MeshApplicationDelivery(
                        profile.ProfileId,
                        message.SenderPeerId,
                        message.Kind,
                        message.Payload));
                    continue;
                }

                MeshPresencePayload? payload;
                try
                {
                    payload = JsonSerializer.Deserialize<MeshPresencePayload>(message.Payload.Span);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (payload is not { Version: PresenceProtocolVersion }
                    || string.IsNullOrWhiteSpace(payload.DisplayName)
                    || payload.DisplayName.Length > _limits.MaximumDisplayNameLength
                    || payload.DisplayName.Any(char.IsControl))
                {
                    continue;
                }

                var route = new PeerRouteKey(profile.Identity.PeerId, message.SenderPeerId);
                if (!TryParseEndpoint(delivery.TransportContext, out var sourceEndpoint)
                    || !_endpoints.TryGetValue(route, out var candidate)
                    || !Equals(candidate.Endpoint, sourceEndpoint))
                {
                    continue;
                }

                _endpoints[route] = candidate with
                {
                    SeenAt = _timeProvider.GetUtcNow(),
                    IsAuthenticated = true
                };
                var presence = new MeshPeerPresence(
                    message.SenderPeerId,
                    payload.DisplayName,
                    payload.IsOnline,
                    _timeProvider.GetUtcNow());
                _presence[route] = presence;
                if (presence.IsOnline && _presenceWaiters.TryRemove(route, out var waiter))
                    waiter.TrySetResult(presence);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
    }

    private async Task AnnouncePeriodicallyAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.AnnouncementInterval, _timeProvider);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var profile in _profiles.Values)
            {
                await AnnounceProfileAsync(profile, cancellationToken).ConfigureAwait(false);
                await SendPresenceToKnownFriendsAsync(profile, true, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RefreshDhtPeriodicallyAsync(CancellationToken cancellationToken)
    {
        if (!_options.EnableInternetDiscovery)
            return;
        using var timer = new PeriodicTimer(_options.DhtRefreshInterval, _timeProvider);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            PruneFriendRequests();
            foreach (var profile in _profiles.Values)
                QueueDhtRefresh(profile);
        }
    }

    private void QueueDhtRefresh(ProfileTransport profile)
    {
        if (!_options.EnableInternetDiscovery || profile.DhtRefresh is { IsCompleted: false })
            return;
        profile.DhtRefresh = RefreshDhtProfileAsync(profile, _stoppingToken);
    }

    private async Task RefreshDhtProfileAsync(ProfileTransport profile, CancellationToken cancellationToken)
    {
        try
        {
            var endpoints = await _dht.LookupAndAnnounceAsync(
                profile.Identity.FriendId,
                TransportPort,
                SendDhtAsync,
                cancellationToken).ConfigureAwait(false);
            var punch = MeshPairingProtocol.Encode(
                new MeshPairingMessage(1, "punch", Guid.NewGuid().ToString("N"), profile.Identity.FriendId),
                _limits);
            foreach (var endpoint in endpoints)
                await SendPairingAsync(punch, endpoint, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            _log?.Warning($"Mesh DHT refresh failed: {exception.Message}");
        }
    }

    private async Task DeliverOutgoingRequestAsync(
        OutgoingFriendRequest request,
        ReadOnlyMemory<byte> packet,
        CancellationToken cancellationToken)
    {
        try
        {
            for (var attempt = 0; attempt < 3 && !cancellationToken.IsCancellationRequested; attempt++)
            {
                var candidates = await _dht.LookupAndAnnounceAsync(
                    request.TargetFriendId,
                    TransportPort,
                    SendDhtAsync,
                    cancellationToken).ConfigureAwait(false);
                _log?.Info(
                    $"Mesh friend request {request.RequestId} DHT attempt {attempt + 1} found {candidates.Count} candidate endpoints");
                foreach (var endpoint in candidates)
                    await SendPairingAsync(packet, endpoint, cancellationToken).ConfigureAwait(false);
                if (attempt < 2)
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            _log?.Warning($"Mesh friend request delivery failed: {exception.Message}");
        }
    }

    private async Task HandlePairingAsync(
        MeshPairingMessage message,
        IPEndPoint source,
        CancellationToken cancellationToken)
    {
        if (message.Type == "punch" && message.TargetFriendId is not null)
        {
            foreach (var request in _outgoingFriendRequests.Values.Where(request =>
                         string.Equals(request.TargetFriendId, message.TargetFriendId, StringComparison.Ordinal)))
            {
                var packet = MeshPairingProtocol.Encode(
                    new MeshPairingMessage(1, "probe", request.RequestId, request.TargetFriendId),
                    _limits);
                await SendPairingAsync(packet, source, cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        if (message.Type == "challenge"
            && message.TargetFriendId is not null
            && message.Token is not null)
        {
            var challengedRequest = _outgoingFriendRequests.Values.FirstOrDefault(request =>
                string.Equals(request.RequestId, message.RequestId, StringComparison.Ordinal)
                && string.Equals(request.TargetFriendId, message.TargetFriendId, StringComparison.Ordinal));
            if (challengedRequest is null)
                return;
            var response = MeshPairingProtocol.Encode(
                new MeshPairingMessage(
                    1,
                    "probe",
                    challengedRequest.RequestId,
                    challengedRequest.TargetFriendId,
                    message.Token),
                _limits);
            await SendPairingAsync(response, source, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (message.Type == "probe" && message.TargetFriendId is not null)
        {
            var profile = _profiles.Values.FirstOrDefault(candidate => string.Equals(
                candidate.Identity.FriendId,
                message.TargetFriendId,
                StringComparison.Ordinal));
            if (profile is null)
                return;
            if (message.Token is null)
            {
                var challenge = MeshPairingProtocol.Encode(
                    new MeshPairingMessage(
                        1,
                        "challenge",
                        message.RequestId,
                        message.TargetFriendId,
                        CreatePairingCookie(message.RequestId, message.TargetFriendId, source)),
                    _limits);
                await SendPairingAsync(challenge, source, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (!VerifyPairingCookie(message.Token, message.RequestId, message.TargetFriendId, source))
                return;
            var record = await _friends.CreatePeerRecordAsync(
                profile.ProfileId,
                profile.DisplayName,
                message.RequestId,
                "ack",
                TimeSpan.FromMinutes(2),
                cancellationToken).ConfigureAwait(false);
            if (!record.IsSuccess)
                return;
            var acknowledgement = MeshPairingProtocol.Encode(
                new MeshPairingMessage(1, "ack", message.RequestId, Token: record.Value.Token),
                _limits);
            await SendPairingAsync(acknowledgement, source, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (message.Type == "invite"
            && message.TargetFriendId is not null
            && message.Token is not null)
        {
            var profile = _profiles.Values.FirstOrDefault(candidate => string.Equals(
                candidate.Identity.FriendId,
                message.TargetFriendId,
                StringComparison.Ordinal));
            if (profile is null)
                return;
            var requestKey = new PairingRequestKey(profile.ProfileId, message.RequestId);
            if (_acceptedFriendRequests.TryGetValue(requestKey, out var accepted))
            {
                var acceptance = MeshPairingProtocol.Encode(
                    new MeshPairingMessage(1, "accept", message.RequestId, Token: accepted.AcceptanceToken),
                    _limits);
                await SendPairingAsync(acceptance, source, cancellationToken).ConfigureAwait(false);
                return;
            }
            var invite = _friends.InspectInvite(message.Token);
            if (!invite.IsSuccess
                || string.Equals(invite.Value.PeerId, profile.Identity.PeerId, StringComparison.Ordinal)
                || !string.Equals(invite.Value.TargetFriendId, profile.Identity.FriendId, StringComparison.Ordinal)
                || !Guid.TryParse(invite.Value.PlayerUuid, out var requesterUuid))
            {
                return;
            }
            if (!_incomingFriendRequests.ContainsKey(requestKey)
                && _incomingFriendRequests.Count(item => string.Equals(
                    item.Key.ProfileId,
                    profile.ProfileId,
                    StringComparison.OrdinalIgnoreCase)) >= _limits.MaximumIssuedInvitesPerProfile)
            {
                return;
            }

            var requestUuid = Guid.TryParseExact(message.RequestId, "N", out var parsedRequestUuid)
                ? parsedRequestUuid
                : Guid.NewGuid();
            var snapshot = new MeshFriendRequestSnapshot(
                requestUuid,
                requesterUuid,
                Guid.TryParse(profile.ProfileId, out var playerUuid) ? playerUuid : null,
                invite.Value.DisplayName,
                _timeProvider.GetUtcNow(),
                null);
            if (_incomingFriendRequests.TryAdd(
                    requestKey,
                    new IncomingFriendRequest(
                        profile.ProfileId,
                        message.RequestId,
                        message.Token,
                        invite.Value.ExpiresAt,
                        source,
                        snapshot)))
            {
                _log?.Info(
                    $"Mesh friend request {message.RequestId} received from {invite.Value.FriendId}");
            }

            return;
        }

        var outgoing = _outgoingFriendRequests.FirstOrDefault(item =>
            string.Equals(item.Key.RequestId, message.RequestId, StringComparison.Ordinal));
        if (outgoing.Value is null || message.Token is null)
            return;

        if (message.Type is "ack" or "reject")
        {
            var record = _friends.VerifyPeerRecord(message.Token, message.RequestId, message.Type);
            if (!record.IsSuccess
                || !string.Equals(record.Value.FriendId, outgoing.Value.TargetFriendId, StringComparison.Ordinal))
            {
                return;
            }
            if (message.Type == "reject")
            {
                _outgoingFriendRequests.TryRemove(outgoing.Key, out _);
                _log?.Info($"Mesh friend request {message.RequestId} rejected remotely");
                return;
            }
            outgoing.Value.Endpoint = source;
            outgoing.Value.Snapshot = outgoing.Value.Snapshot with
            {
                PlayerUuid = Guid.TryParse(record.Value.PlayerUuid, out var playerUuid) ? playerUuid : null,
                Username = record.Value.DisplayName
            };
            var invitation = MeshPairingProtocol.Encode(
                new MeshPairingMessage(
                    1,
                    "invite",
                    outgoing.Value.RequestId,
                    outgoing.Value.TargetFriendId,
                    outgoing.Value.InviteToken),
                _limits);
            await SendPairingAsync(invitation, source, cancellationToken).ConfigureAwait(false);
            _log?.Info($"Mesh friend request {message.RequestId} reached the requested identity");
            return;
        }

        if (message.Type == "accept")
        {
            var completed = await _friends.CompleteInviteFromFriendIdAsync(
                outgoing.Value.ProfileId,
                message.Token,
                outgoing.Value.TargetFriendId,
                cancellationToken).ConfigureAwait(false);
            if (!completed.IsSuccess)
                return;
            _outgoingFriendRequests.TryRemove(outgoing.Key, out _);
            await RefreshProfileAsync(outgoing.Value.ProfileId, cancellationToken).ConfigureAwait(false);
            _log?.Info($"Mesh friend request {message.RequestId} accepted remotely and completed");
        }
    }

    private async Task SendPairingToDiscoveryTargetsAsync(
        ReadOnlyMemory<byte> packet,
        CancellationToken cancellationToken)
    {
        foreach (var target in _options.ResolveDiscoveryTargets())
            await _transportSocket!.SendAsync(packet, target, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask SendPairingAsync(
        ReadOnlyMemory<byte> packet,
        IPEndPoint endpoint,
        CancellationToken cancellationToken)
        => await _transportSocket!.SendAsync(packet, endpoint, cancellationToken).ConfigureAwait(false);

    private async Task SendPairingRepeatedlyAsync(
        ReadOnlyMemory<byte> packet,
        IPEndPoint endpoint,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await SendPairingAsync(packet, endpoint, cancellationToken).ConfigureAwait(false);
            if (attempt < 2)
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask SendDhtAsync(
        ReadOnlyMemory<byte> packet,
        IPEndPoint endpoint,
        CancellationToken cancellationToken)
        => await _transportSocket!.SendAsync(packet, endpoint, cancellationToken).ConfigureAwait(false);

    private void PruneFriendRequests()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var item in _incomingFriendRequests.Where(item => item.Value.ExpiresAt <= now))
            _incomingFriendRequests.TryRemove(item.Key, out _);
        foreach (var item in _outgoingFriendRequests.Where(item => item.Value.ExpiresAt <= now))
            _outgoingFriendRequests.TryRemove(item.Key, out _);
        foreach (var item in _acceptedFriendRequests.Where(item => item.Value.ExpiresAt <= now))
            _acceptedFriendRequests.TryRemove(item.Key, out _);
    }

    private string CreatePairingCookie(string requestId, string targetFriendId, IPEndPoint endpoint)
    {
        var epoch = _timeProvider.GetUtcNow().ToUnixTimeSeconds() / 60;
        var mac = ComputePairingCookie(epoch, requestId, targetFriendId, endpoint);
        return epoch.ToString(CultureInfo.InvariantCulture)
               + "."
               + Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private bool VerifyPairingCookie(
        string token,
        string requestId,
        string targetFriendId,
        IPEndPoint endpoint)
    {
        var separator = token.IndexOf('.');
        if (separator <= 0
            || separator != token.LastIndexOf('.')
            || !long.TryParse(
                token.AsSpan(0, separator),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var epoch))
        {
            return false;
        }
        var currentEpoch = _timeProvider.GetUtcNow().ToUnixTimeSeconds() / 60;
        if (epoch < currentEpoch - 1 || epoch > currentEpoch + 1)
            return false;

        byte[] supplied;
        try
        {
            var encoded = token[(separator + 1)..].Replace('-', '+').Replace('_', '/');
            supplied = Convert.FromBase64String(encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '='));
        }
        catch (FormatException)
        {
            return false;
        }
        var expected = ComputePairingCookie(epoch, requestId, targetFriendId, endpoint);
        return supplied.Length == expected.Length
               && CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private byte[] ComputePairingCookie(
        long epoch,
        string requestId,
        string targetFriendId,
        IPEndPoint endpoint)
    {
        var message = Encoding.UTF8.GetBytes(
            $"{epoch}|{endpoint.Address}|{endpoint.Port}|{requestId}|{targetFriendId}");
        return HMACSHA256.HashData(_pairingCookieKey, message);
    }

    private async Task AnnounceProfileAsync(
        ProfileTransport profile,
        CancellationToken cancellationToken)
    {
        var cycle = await profile.Discovery.CreateCycleAsync(
            profile.ProfileId,
            TransportPort,
            cancellationToken).ConfigureAwait(false);
        profile.ReplaceDiscoveryRoutes(cycle.InboundRoutes);
        foreach (var packet in cycle.Announcements)
        {
            foreach (var target in _options.ResolveDiscoveryTargets())
                await _discoverySocket!.SendAsync(packet, target, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendPresenceToKnownFriendsAsync(
        ProfileTransport profile,
        bool isOnline,
        CancellationToken cancellationToken)
    {
        foreach (var friend in profile.Friends.Values)
            await SendPresenceAsync(profile, friend, isOnline, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendPresenceAsync(
        ProfileTransport profile,
        MeshFriend friend,
        bool isOnline,
        CancellationToken cancellationToken)
    {
        var route = new PeerRouteKey(profile.Identity.PeerId, friend.PeerId);
        if (!_endpoints.TryGetValue(route, out var endpoint)
            || _timeProvider.GetUtcNow() - endpoint.SeenAt > _options.EndpointLifetime)
        {
            return;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(new MeshPresencePayload(
            PresenceProtocolVersion,
            profile.DisplayName,
            isOnline));
        var sealedResult = await profile.Envelopes.SealAsync(
            profile.ProfileId,
            friend.PeerId,
            MeshMessageKind.Presence,
            payload,
            cancellationToken).ConfigureAwait(false);
        if (!sealedResult.IsSuccess)
            return;

        var packet = MeshTransportProtocol.EncodeEnvelope(
            profile.Identity.PeerId,
            friend.PeerId,
            sealedResult.Value,
            _limits);
        await _transportSocket!.SendAsync(packet, endpoint.Endpoint, cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshFriendsAsync(
        ProfileTransport profile,
        CancellationToken cancellationToken)
    {
        var friends = await _friends.GetFriendsAsync(profile.ProfileId, cancellationToken).ConfigureAwait(false);
        var currentPeerIds = friends.Select(friend => friend.PeerId).ToHashSet(StringComparer.Ordinal);
        foreach (var friend in friends)
            profile.Friends[friend.PeerId] = friend;
        foreach (var peerId in profile.Friends.Keys.Where(peerId => !currentPeerIds.Contains(peerId)))
            profile.Friends.TryRemove(peerId, out _);
    }

    private string ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)
            || displayName.Length > _limits.MaximumDisplayNameLength
            || displayName.Any(char.IsControl))
        {
            throw new ArgumentException("A valid mesh display name is required", nameof(displayName));
        }
        return displayName.Trim();
    }

    private MeshPeerPresence? GetCurrentOnlinePresence(PeerRouteKey route)
        => _presence.TryGetValue(route, out var presence)
           && presence.IsOnline
           && _timeProvider.GetUtcNow() - presence.LastSeenAt <= _options.PresenceTimeout
            ? presence
            : null;

    private AuthenticatedEndpoint UpdateDiscoveredEndpoint(
        AuthenticatedEndpoint current,
        IPEndPoint discoveredEndpoint)
    {
        var now = _timeProvider.GetUtcNow();
        if (current.IsAuthenticated
            && now - current.SeenAt <= _options.EndpointLifetime
            && !Equals(current.Endpoint, discoveredEndpoint))
        {
            return current;
        }

        return new AuthenticatedEndpoint(
            discoveredEndpoint,
            now,
            current.IsAuthenticated && Equals(current.Endpoint, discoveredEndpoint));
    }

    private static string FormatEndpoint(IPEndPoint endpoint)
        => $"{endpoint.Address}|{endpoint.Port}";

    private static bool TryParseEndpoint(string? value, out IPEndPoint endpoint)
    {
        endpoint = new IPEndPoint(IPAddress.None, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var separator = value.LastIndexOf('|');
        if (separator <= 0
            || !IPAddress.TryParse(value.AsSpan(0, separator), out var address)
            || !int.TryParse(value.AsSpan(separator + 1), out var port)
            || port is < 1 or > ushort.MaxValue)
        {
            return false;
        }
        endpoint = new IPEndPoint(address, port);
        return true;
    }

    public override void Dispose()
    {
        CryptographicOperations.ZeroMemory(_pairingCookieKey);
        _activationGate.Dispose();
        base.Dispose();
    }

    private sealed class ProfileTransport(
        string profileId,
        string displayName,
        MeshPublicIdentity identity,
        MeshEnvelopeService envelopes,
        MeshDiscoveryService discovery,
        MeshInboundPipeline pipeline) : IDisposable
    {
        private IReadOnlyDictionary<MeshDiscoveryRouteKey, MeshDiscoveryRoute> _discoveryRoutes
            = new Dictionary<MeshDiscoveryRouteKey, MeshDiscoveryRoute>();

        public string ProfileId { get; } = profileId;
        public string DisplayName { get; set; } = displayName;
        public MeshPublicIdentity Identity { get; } = identity;
        public MeshEnvelopeService Envelopes { get; } = envelopes;
        public MeshDiscoveryService Discovery { get; } = discovery;
        public MeshInboundPipeline Pipeline { get; } = pipeline;
        public ConcurrentDictionary<string, MeshFriend> Friends { get; } = new(StringComparer.Ordinal);
        public Task? Consumer { get; set; }
        public Task? DhtRefresh { get; set; }

        public bool TryGetDiscoveryRoute(
            MeshDiscoveryRouteKey key,
            out MeshDiscoveryRoute route)
            => Volatile.Read(ref _discoveryRoutes).TryGetValue(key, out route!);

        public void ReplaceDiscoveryRoutes(IReadOnlyList<MeshDiscoveryRoute> routes)
        {
            var next = new Dictionary<MeshDiscoveryRouteKey, MeshDiscoveryRoute>(routes.Count);
            foreach (var route in routes)
                next[route.Key] = route;
            Volatile.Write(ref _discoveryRoutes, next);
        }

        public void Dispose()
        {
            Pipeline.Complete();
            Discovery.Dispose();
        }
    }

    private readonly record struct PeerRouteKey(string LocalPeerId, string RemotePeerId);
    private readonly record struct PairingRequestKey(string ProfileId, string RequestId);
    private sealed record AcceptedFriendRequest(string AcceptanceToken, DateTimeOffset ExpiresAt);
    private sealed record IncomingFriendRequest(
        string ProfileId,
        string RequestId,
        string InviteToken,
        DateTimeOffset ExpiresAt,
        IPEndPoint Endpoint,
        MeshFriendRequestSnapshot Snapshot);
    private sealed class OutgoingFriendRequest(
        string profileId,
        string targetFriendId,
        string requestId,
        string inviteToken,
        DateTimeOffset expiresAt,
        MeshFriendRequestSnapshot snapshot)
    {
        public string ProfileId { get; } = profileId;
        public string TargetFriendId { get; } = targetFriendId;
        public string RequestId { get; } = requestId;
        public string InviteToken { get; } = inviteToken;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public MeshFriendRequestSnapshot Snapshot { get; set; } = snapshot;
        public IPEndPoint? Endpoint { get; set; }
        public Task? Delivery { get; set; }
    }
    private sealed record AuthenticatedEndpoint(
        IPEndPoint Endpoint,
        DateTimeOffset SeenAt,
        bool IsAuthenticated);
    private sealed record MeshPresencePayload(int Version, string DisplayName, bool IsOnline);
}

/// <summary>
/// Authenticated application message delivered to one active local profile
/// </summary>
public sealed record MeshApplicationDelivery(
    string ProfileId,
    string SenderPeerId,
    MeshMessageKind Kind,
    ReadOnlyMemory<byte> Payload);

/// <summary>
/// Social friend request exposed through the Hytale compatibility API
/// </summary>
public sealed record MeshFriendRequestSnapshot(
    Guid RequestUuid,
    Guid RequesterUuid,
    Guid? PlayerUuid,
    string Username,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AcceptedAt);
