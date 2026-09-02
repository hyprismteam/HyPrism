// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
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
    private readonly MeshFriendService _friends;
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
        _log = log;
    }

    public int TransportPort { get; private set; }

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
            _log?.Info($"Mesh profile {identity.PeerId} activated on UDP port {TransportPort}");
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
                AnnouncePeriodicallyAsync(stoppingToken)).ConfigureAwait(false);
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
            if (received.Buffer.Length > _limits.MaximumDiscoveryPacketBytes
                || !limiter.TryConsume(received.RemoteEndPoint.Address)
                || !MeshDiscoveryService.TryReadRoute(received.Buffer, out var routeKey))
            {
                continue;
            }

            foreach (var profile in _profiles.Values)
            {
                if (!profile.TryGetDiscoveryRoute(routeKey, out var discoveryRoute))
                    continue;
                var verified = profile.Discovery.VerifyAnnouncement(received.Buffer, discoveryRoute);
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
            if (received.Buffer.Length > _limits.MaximumInboundEnvelopeBytes + 512
                || !limiter.TryConsume(received.RemoteEndPoint.Address)
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
                    continue;

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
    private sealed record AuthenticatedEndpoint(
        IPEndPoint Endpoint,
        DateTimeOffset SeenAt,
        bool IsAuthenticated);
    private sealed record MeshPresencePayload(int Version, string DisplayName, bool IsOnline);
}
