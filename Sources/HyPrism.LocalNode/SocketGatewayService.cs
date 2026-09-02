// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using HyPrism.Mesh;
using Microsoft.Extensions.Hosting;

namespace HyPrism.LocalNode;

/// <summary>
/// Bridges Hytale socket gateway peer sessions and world invitations to authenticated Mesh messages
/// </summary>
public sealed class SocketGatewayService(
    MeshNetworkHost meshNetwork,
    MeshFriendService meshFriends,
    LocalNodeLog? log = null) : BackgroundService
{
    private const int GatewayProtocolVersion = 1;
    private const int MaximumGatewayMessageBytes = 12 * 1024;
    private const int MaximumInviteCodeLength = 8192;
    private const int MaximumInvitesPerProfile = 128;
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan WorldInviteLifetime = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions MeshJson = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, GatewayConnection>> _connections
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<SessionKey, PeerSessionRoute> _sessions = [];
    private readonly ConcurrentDictionary<SessionKey, TaskCompletionSource<bool>> _pendingOpens = [];
    private readonly ConcurrentDictionary<InviteKey, WorldInviteSnapshot> _incomingInvites = [];
    private readonly ConcurrentDictionary<InviteKey, WorldInviteRoute> _outgoingInvites = [];
    private readonly ConcurrentDictionary<InviteKey, DateTimeOffset> _resolvedInvites = [];
    private readonly object _worldInviteGate = new();

    /// <summary>
    /// Returns pending world invitations received by one local profile
    /// </summary>
    public IReadOnlyList<WorldInviteSnapshot> GetIncomingWorldInvites(string profileId)
    {
        PruneWorldInvites();
        return _incomingInvites
            .Where(item => string.Equals(item.Key.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Value)
            .OrderByDescending(invite => invite.CreatedAt)
            .ToArray();
    }

    /// <summary>
    /// Returns pending world invitations sent by one local profile
    /// </summary>
    public IReadOnlyList<WorldInviteSnapshot> GetOutgoingWorldInvites(string profileId)
    {
        PruneWorldInvites();
        return _outgoingInvites
            .Where(item => string.Equals(item.Key.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Value.Invite)
            .OrderByDescending(invite => invite.CreatedAt)
            .ToArray();
    }

    /// <summary>
    /// Sends a short-lived P2P world invitation to one confirmed Mesh friend
    /// </summary>
    public async Task<WorldInviteSnapshot?> SendWorldInviteAsync(
        string profileId,
        Guid recipientUuid,
        string inviteCode,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(profileId, out var inviterUuid)
            || string.IsNullOrWhiteSpace(inviteCode)
            || inviteCode.Length > MaximumInviteCodeLength)
            return null;
        PruneWorldInvites();
        var friend = await FindFriendAsync(profileId, recipientUuid, cancellationToken).ConfigureAwait(false);
        if (friend is null)
            return null;

        var now = DateTimeOffset.UtcNow;
        var invite = new WorldInviteSnapshot(
            Guid.NewGuid(),
            inviterUuid,
            recipientUuid,
            now,
            now.Add(WorldInviteLifetime),
            inviteCode,
            true);
        var key = new InviteKey(profileId, invite.InviteUuid);
        lock (_worldInviteGate)
        {
            if (_outgoingInvites.Count(item => string.Equals(
                    item.Key.ProfileId,
                    profileId,
                    StringComparison.OrdinalIgnoreCase)) >= MaximumInvitesPerProfile)
            {
                return null;
            }
            _outgoingInvites[key] = new WorldInviteRoute(invite, friend.PeerId);
        }
        var delivered = await SendWorldInviteMessageAsync(
            profileId,
            friend.PeerId,
            new MeshWorldInviteMessage(
                GatewayProtocolVersion,
                "invite",
                invite.InviteUuid,
                invite.InviterUuid,
                invite.InvitedPlayerUuid,
                invite.CreatedAt,
                invite.ExpiresAt,
                invite.InviteCode),
            cancellationToken).ConfigureAwait(false);
        if (!delivered)
        {
            _outgoingInvites.TryRemove(key, out _);
            return null;
        }
        return invite;
    }

    /// <summary>
    /// Accepts one incoming world invitation and returns its P2P join data
    /// </summary>
    public async Task<WorldJoinSnapshot?> AcceptWorldInviteAsync(
        string profileId,
        Guid inviteUuid,
        CancellationToken cancellationToken)
    {
        PruneWorldInvites();
        var key = new InviteKey(profileId, inviteUuid);
        WorldInviteSnapshot invite;
        lock (_worldInviteGate)
        {
            if (!_incomingInvites.TryRemove(key, out invite!))
                return null;
            _resolvedInvites[key] = invite.ExpiresAt;
        }

        var friend = await FindFriendAsync(profileId, invite.InviterUuid, cancellationToken).ConfigureAwait(false);
        if (friend is null)
            return null;
        await SendWorldInviteOutcomeAsync(
            profileId,
            friend.PeerId,
            invite,
            "accepted",
            cancellationToken).ConfigureAwait(false);
        return new WorldJoinSnapshot(null, null, invite.InviteCode, null, null, true);
    }

    /// <summary>
    /// Rejects one incoming world invitation
    /// </summary>
    public Task<bool> RejectWorldInviteAsync(
        string profileId,
        Guid inviteUuid,
        CancellationToken cancellationToken)
        => ResolveIncomingWorldInviteAsync(profileId, inviteUuid, "rejected", cancellationToken);

    /// <summary>
    /// Cancels one world invitation previously sent by the local profile
    /// </summary>
    public async Task<bool> CancelWorldInviteAsync(
        string profileId,
        Guid inviteUuid,
        CancellationToken cancellationToken)
    {
        PruneWorldInvites();
        if (!_outgoingInvites.TryRemove(new InviteKey(profileId, inviteUuid), out var route))
            return false;
        await SendWorldInviteOutcomeAsync(
            profileId,
            route.RemotePeerId,
            route.Invite,
            "canceled",
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Returns P2P join data from the newest pending invitation sent by a friend
    /// </summary>
    public WorldJoinSnapshot? JoinFriendWorld(string profileId, Guid friendUuid)
    {
        var invite = GetIncomingWorldInvites(profileId)
            .Where(item => item.InviterUuid == friendUuid)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefault();
        return invite is null
            ? null
            : new WorldJoinSnapshot(null, null, invite.InviteCode, null, null, true);
    }

    /// <summary>
    /// Owns one authenticated Hytale WebSocket until the client disconnects
    /// </summary>
    public async Task RunConnectionAsync(
        string profileId,
        string playerName,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        await meshNetwork.ActivateProfileAsync(profileId, playerName, cancellationToken).ConfigureAwait(false);

        var connection = new GatewayConnection(Guid.NewGuid().ToString("D"), socket);
        var profileConnections = _connections.GetOrAdd(
            profileId,
            static _ => new ConcurrentDictionary<string, GatewayConnection>(StringComparer.Ordinal));
        profileConnections[connection.Id] = connection;

        try
        {
            await connection.SendAsync("gateway.connected", new
            {
                connection_id = connection.Id
            }, cancellationToken).ConfigureAwait(false);
            await ReceiveMessagesAsync(profileId, connection, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (WebSocketException exception)
        {
            log?.Warning($"Socket gateway connection ended: {exception.Message}");
        }
        finally
        {
            profileConnections.TryRemove(connection.Id, out _);
            if (profileConnections.IsEmpty)
            {
                _connections.TryRemove(new KeyValuePair<string, ConcurrentDictionary<string, GatewayConnection>>(
                    profileId,
                    profileConnections));
                await CloseProfileSessionsAsync(profileId, CancellationToken.None).ConfigureAwait(false);
            }

            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var delivery in meshNetwork
                               .ReadApplicationMessagesAsync(stoppingToken)
                               .ConfigureAwait(false))
            {
                if (delivery.Kind == MeshMessageKind.IceSignal)
                    await HandleMeshMessageAsync(delivery, stoppingToken).ConfigureAwait(false);
                else if (delivery.Kind == MeshMessageKind.WorldInvite)
                    await HandleWorldInviteMessageAsync(delivery, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
    }

    private async Task ReceiveMessagesAsync(
        string profileId,
        GatewayConnection connection,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        while (connection.Socket.State is WebSocketState.Open or WebSocketState.CloseSent)
        {
            using var message = new MemoryStream();
            ValueWebSocketReceiveResult result;
            do
            {
                result = await connection.Socket.ReceiveAsync(buffer.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                if (result.MessageType != WebSocketMessageType.Text
                    || message.Length + result.Count > MaximumGatewayMessageBytes)
                {
                    await connection.CloseAsync(
                        WebSocketCloseStatus.MessageTooBig,
                        "Unsupported gateway message",
                        cancellationToken).ConfigureAwait(false);
                    return;
                }
                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            await HandleClientMessageAsync(
                profileId,
                connection,
                message.GetBuffer().AsMemory(0, checked((int)message.Length)),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleClientMessageAsync(
        string profileId,
        GatewayConnection connection,
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(message);
        }
        catch (JsonException)
        {
            await connection.CloseAsync(
                WebSocketCloseStatus.InvalidPayloadData,
                "Malformed gateway message",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var typeValue)
                || typeValue.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object)
            {
                await connection.CloseAsync(
                    WebSocketCloseStatus.InvalidPayloadData,
                    "Invalid gateway envelope",
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            switch (typeValue.GetString())
            {
                case "peer.session.open":
                    await HandleOpenAsync(profileId, connection, data, cancellationToken).ConfigureAwait(false);
                    break;
                case "peer.send":
                    await HandleSendAsync(profileId, connection, data, cancellationToken).ConfigureAwait(false);
                    break;
                case "peer.session.close":
                    await HandleCloseAsync(profileId, connection, data, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    await SendErrorAsync(
                        connection,
                        null,
                        ReadString(data, "client_ref"),
                        "unsupported_message",
                        "Unsupported gateway message type",
                        cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    private async Task HandleOpenAsync(
        string profileId,
        GatewayConnection connection,
        JsonElement data,
        CancellationToken cancellationToken)
    {
        var peerUuidValue = ReadString(data, "peer_uuid");
        var kind = ReadString(data, "kind");
        var clientRef = ReadString(data, "client_ref");
        if (!Guid.TryParse(peerUuidValue, out var peerUuid)
            || string.IsNullOrWhiteSpace(kind)
            || kind.Length > 128
            || string.IsNullOrWhiteSpace(clientRef)
            || clientRef.Length > 128)
        {
            await SendErrorAsync(
                connection,
                null,
                clientRef,
                "invalid_request",
                "Invalid peer session open request",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var friend = (await meshFriends.GetFriendsAsync(profileId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => Guid.TryParse(item.PlayerUuid, out var candidate)
                                    && candidate == peerUuid);
        var sessionId = Guid.NewGuid().ToString("D");
        var key = new SessionKey(profileId, sessionId);
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingOpens[key] = pending;

        var accepted = false;
        try
        {
            if (friend is not null)
            {
                var request = new MeshPeerMessage(
                    GatewayProtocolVersion,
                    "open",
                    sessionId,
                    profileId,
                    kind,
                    0,
                    null,
                    false);
                var sent = await SendMeshAsync(
                    profileId,
                    friend.PeerId,
                    request,
                    cancellationToken).ConfigureAwait(false);
                if (sent)
                    accepted = await pending.Task.WaitAsync(OpenTimeout, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (TimeoutException)
        {
            accepted = false;
        }
        finally
        {
            _pendingOpens.TryRemove(key, out _);
        }

        if (accepted && friend is not null)
            _sessions[key] = new PeerSessionRoute(friend.PeerId, peerUuid.ToString("D"), kind);

        await connection.SendAsync("peer.session.ack", new
        {
            session_id = sessionId,
            client_ref = clientRef,
            peer_online = accepted
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleSendAsync(
        string profileId,
        GatewayConnection connection,
        JsonElement data,
        CancellationToken cancellationToken)
    {
        var sessionId = ReadString(data, "session_id");
        if (string.IsNullOrWhiteSpace(sessionId)
            || sessionId.Length > 128
            || !_sessions.TryGetValue(new SessionKey(profileId, sessionId), out var route)
            || !data.TryGetProperty("seq", out var sequenceValue)
            || !sequenceValue.TryGetInt64(out var sequence)
            || !data.TryGetProperty("payload", out var payload))
        {
            await SendErrorAsync(
                connection,
                sessionId,
                null,
                "unknown_session",
                "Unknown or invalid peer session",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var request = new MeshPeerMessage(
            GatewayProtocolVersion,
            "message",
            sessionId,
            profileId,
            null,
            sequence,
            payload.Clone(),
            false);
        if (!await SendMeshAsync(profileId, route.RemotePeerId, request, cancellationToken).ConfigureAwait(false))
        {
            await SendErrorAsync(
                connection,
                sessionId,
                null,
                "peer_offline",
                "The peer is offline",
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleCloseAsync(
        string profileId,
        GatewayConnection connection,
        JsonElement data,
        CancellationToken cancellationToken)
    {
        var sessionId = ReadString(data, "session_id");
        if (string.IsNullOrWhiteSpace(sessionId)
            || !_sessions.TryRemove(new SessionKey(profileId, sessionId), out var route))
        {
            await SendErrorAsync(
                connection,
                sessionId,
                null,
                "unknown_session",
                "Unknown peer session",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await SendMeshAsync(
            profileId,
            route.RemotePeerId,
            new MeshPeerMessage(
                GatewayProtocolVersion,
                "close",
                sessionId,
                profileId,
                null,
                0,
                null,
                false),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleMeshMessageAsync(
        MeshApplicationDelivery delivery,
        CancellationToken cancellationToken)
    {
        MeshPeerMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<MeshPeerMessage>(delivery.Payload.Span, MeshJson);
        }
        catch (JsonException)
        {
            return;
        }

        if (message is not { Version: GatewayProtocolVersion }
            || string.IsNullOrWhiteSpace(message.Operation)
            || string.IsNullOrWhiteSpace(message.SessionId)
            || message.SessionId.Length > 128
            || !Guid.TryParse(message.FromUuid, out var fromUuid))
        {
            return;
        }

        var friend = (await meshFriends.GetFriendsAsync(delivery.ProfileId, cancellationToken)
                .ConfigureAwait(false))
            .FirstOrDefault(item => item.PeerId == delivery.SenderPeerId
                                    && Guid.TryParse(item.PlayerUuid, out var playerUuid)
                                    && playerUuid == fromUuid);
        if (friend is null)
            return;

        var key = new SessionKey(delivery.ProfileId, message.SessionId);
        switch (message.Operation)
        {
            case "open":
                {
                    var accepted = HasConnections(delivery.ProfileId)
                                   && !string.IsNullOrWhiteSpace(message.Kind)
                                   && message.Kind.Length <= 128;
                    if (accepted)
                    {
                        _sessions[key] = new PeerSessionRoute(
                            delivery.SenderPeerId,
                            fromUuid.ToString("D"),
                            message.Kind!);
                        await BroadcastAsync(delivery.ProfileId, "peer.session.opened", new
                        {
                            session_id = message.SessionId,
                            from_uuid = fromUuid,
                            kind = message.Kind
                        }, cancellationToken).ConfigureAwait(false);
                    }

                    await SendMeshAsync(
                        delivery.ProfileId,
                        delivery.SenderPeerId,
                        message with
                        {
                            Operation = "open-result",
                            FromUuid = delivery.ProfileId,
                            Kind = null,
                            Accepted = accepted
                        },
                        cancellationToken).ConfigureAwait(false);
                    break;
                }
            case "open-result":
                if (_pendingOpens.TryGetValue(key, out var pending))
                    pending.TrySetResult(message.Accepted);
                break;
            case "message":
                if (_sessions.TryGetValue(key, out var route)
                    && route.RemotePeerId == delivery.SenderPeerId
                    && message.Payload.HasValue)
                {
                    await BroadcastAsync(delivery.ProfileId, "peer.message", new
                    {
                        session_id = message.SessionId,
                        from_uuid = fromUuid,
                        seq = message.Sequence,
                        payload = message.Payload.Value
                    }, cancellationToken).ConfigureAwait(false);
                }
                break;
            case "close":
                if (_sessions.TryRemove(key, out var closed)
                    && closed.RemotePeerId == delivery.SenderPeerId)
                {
                    await BroadcastAsync(delivery.ProfileId, "peer.session.closed", new
                    {
                        session_id = message.SessionId,
                        reason = (string?)null
                    }, cancellationToken).ConfigureAwait(false);
                }
                break;
        }
    }

    private async Task HandleWorldInviteMessageAsync(
        MeshApplicationDelivery delivery,
        CancellationToken cancellationToken)
    {
        MeshWorldInviteMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<MeshWorldInviteMessage>(delivery.Payload.Span, MeshJson);
        }
        catch (JsonException)
        {
            return;
        }

        if (message is not { Version: GatewayProtocolVersion }
            || message.InviteUuid == Guid.Empty
            || message.InviterUuid == Guid.Empty
            || message.InvitedPlayerUuid == Guid.Empty
            || !Guid.TryParse(delivery.ProfileId, out var localUuid)
            || string.IsNullOrWhiteSpace(message.Operation)
            || message.Operation.Length > 32)
        {
            return;
        }

        var remoteUuid = message.InviterUuid == localUuid
            ? message.InvitedPlayerUuid
            : message.InviterUuid;
        var friend = await FindFriendAsync(delivery.ProfileId, remoteUuid, cancellationToken).ConfigureAwait(false);
        if (friend is null || friend.PeerId != delivery.SenderPeerId)
            return;

        var key = new InviteKey(delivery.ProfileId, message.InviteUuid);
        PruneWorldInvites();
        var now = DateTimeOffset.UtcNow;
        switch (message.Operation)
        {
            case "invite" when message.InvitedPlayerUuid == localUuid
                               && message.CreatedAt <= now.AddMinutes(1)
                               && message.ExpiresAt > now
                               && message.ExpiresAt <= now.Add(WorldInviteLifetime).AddMinutes(1)
                               && message.ExpiresAt > message.CreatedAt
                               && message.ExpiresAt - message.CreatedAt <= WorldInviteLifetime
                               && !string.IsNullOrWhiteSpace(message.InviteCode)
                               && message.InviteCode.Length <= MaximumInviteCodeLength:
                {
                    bool added;
                    lock (_worldInviteGate)
                    {
                        if (_resolvedInvites.ContainsKey(key))
                            return;
                        if (!_incomingInvites.ContainsKey(key)
                            && CountTrackedInvites(delivery.ProfileId) >= MaximumInvitesPerProfile)
                        {
                            return;
                        }

                        var invite = new WorldInviteSnapshot(
                            message.InviteUuid,
                            message.InviterUuid,
                            message.InvitedPlayerUuid,
                            message.CreatedAt,
                            message.ExpiresAt,
                            message.InviteCode,
                            true);
                        added = _incomingInvites.TryAdd(key, invite);
                    }

                    if (added)
                    {
                        await BroadcastNotificationAsync(
                            delivery.ProfileId,
                            "world.invite.received",
                            new
                            {
                                invite_uuid = message.InviteUuid,
                                inviter_uuid = message.InviterUuid,
                                invitedPlayerUuid = message.InvitedPlayerUuid,
                                timestamp = message.CreatedAt,
                                is_p2p = true
                            },
                            cancellationToken).ConfigureAwait(false);
                    }
                    break;
                }
            case "accepted" when message.InviterUuid == localUuid:
                if (_outgoingInvites.TryGetValue(key, out var acceptedRoute)
                    && acceptedRoute.RemotePeerId == delivery.SenderPeerId
                    && acceptedRoute.Invite.InvitedPlayerUuid == message.InvitedPlayerUuid
                    && _outgoingInvites.TryRemove(
                        new KeyValuePair<InviteKey, WorldInviteRoute>(key, acceptedRoute)))
                {
                    await BroadcastNotificationAsync(
                        delivery.ProfileId,
                        "world.invite.accepted",
                        new
                        {
                            invite_uuid = message.InviteUuid,
                            accepted_by_uuid = message.InvitedPlayerUuid
                        },
                        cancellationToken).ConfigureAwait(false);
                }
                break;
            case "rejected" when message.InviterUuid == localUuid:
                if (_outgoingInvites.TryGetValue(key, out var rejectedRoute)
                    && rejectedRoute.RemotePeerId == delivery.SenderPeerId
                    && rejectedRoute.Invite.InvitedPlayerUuid == message.InvitedPlayerUuid
                    && _outgoingInvites.TryRemove(
                        new KeyValuePair<InviteKey, WorldInviteRoute>(key, rejectedRoute)))
                {
                    await BroadcastNotificationAsync(
                        delivery.ProfileId,
                        "world.invite.rejected",
                        new
                        {
                            invite_uuid = message.InviteUuid,
                            rejected_by_uuid = message.InvitedPlayerUuid
                        },
                        cancellationToken).ConfigureAwait(false);
                }
                break;
            case "canceled" when message.InvitedPlayerUuid == localUuid
                                 && message.ExpiresAt > now
                                 && message.ExpiresAt <= now.Add(WorldInviteLifetime).AddMinutes(1):
                bool removed;
                lock (_worldInviteGate)
                {
                    removed = _incomingInvites.TryGetValue(key, out var canceledInvite)
                              && canceledInvite.InviterUuid == message.InviterUuid
                              && _incomingInvites.TryRemove(
                                  new KeyValuePair<InviteKey, WorldInviteSnapshot>(key, canceledInvite));
                    if (removed
                        || _resolvedInvites.ContainsKey(key)
                        || CountTrackedInvites(delivery.ProfileId) < MaximumInvitesPerProfile)
                    {
                        _resolvedInvites[key] = message.ExpiresAt;
                    }
                }
                if (removed)
                {
                    await BroadcastNotificationAsync(
                        delivery.ProfileId,
                        "world.invite.canceled",
                        new { invite_uuid = message.InviteUuid },
                        cancellationToken).ConfigureAwait(false);
                }
                break;
        }
    }

    private async Task<bool> ResolveIncomingWorldInviteAsync(
        string profileId,
        Guid inviteUuid,
        string operation,
        CancellationToken cancellationToken)
    {
        PruneWorldInvites();
        var key = new InviteKey(profileId, inviteUuid);
        WorldInviteSnapshot invite;
        lock (_worldInviteGate)
        {
            if (!_incomingInvites.TryRemove(key, out invite!))
                return false;
            _resolvedInvites[key] = invite.ExpiresAt;
        }
        var friend = await FindFriendAsync(profileId, invite.InviterUuid, cancellationToken).ConfigureAwait(false);
        if (friend is not null)
        {
            await SendWorldInviteOutcomeAsync(
                profileId,
                friend.PeerId,
                invite,
                operation,
                cancellationToken).ConfigureAwait(false);
        }
        return true;
    }

    private Task<bool> SendWorldInviteOutcomeAsync(
        string profileId,
        string peerId,
        WorldInviteSnapshot invite,
        string operation,
        CancellationToken cancellationToken)
        => SendWorldInviteMessageAsync(
            profileId,
            peerId,
            new MeshWorldInviteMessage(
                GatewayProtocolVersion,
                operation,
                invite.InviteUuid,
                invite.InviterUuid,
                invite.InvitedPlayerUuid,
                invite.CreatedAt,
                invite.ExpiresAt,
                invite.InviteCode),
            cancellationToken);

    private async Task<bool> SendWorldInviteMessageAsync(
        string profileId,
        string peerId,
        MeshWorldInviteMessage message,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, MeshJson);
        var delivered = false;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            delivered |= await meshNetwork.SendAsync(
                profileId,
                peerId,
                MeshMessageKind.WorldInvite,
                payload,
                cancellationToken).ConfigureAwait(false);
            if (attempt < 2)
                await Task.Delay(TimeSpan.FromMilliseconds(40), cancellationToken).ConfigureAwait(false);
        }
        return delivered;
    }

    private async Task<MeshFriend?> FindFriendAsync(
        string profileId,
        Guid playerUuid,
        CancellationToken cancellationToken)
        => (await meshFriends.GetFriendsAsync(profileId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => Guid.TryParse(item.PlayerUuid, out var candidate)
                                    && candidate == playerUuid);

    private void PruneWorldInvites()
    {
        lock (_worldInviteGate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var item in _incomingInvites.Where(item => item.Value.ExpiresAt <= now))
                _incomingInvites.TryRemove(item.Key, out _);
            foreach (var item in _outgoingInvites.Where(item => item.Value.Invite.ExpiresAt <= now))
                _outgoingInvites.TryRemove(item.Key, out _);
            foreach (var item in _resolvedInvites.Where(item => item.Value <= now))
                _resolvedInvites.TryRemove(item.Key, out _);
        }
    }

    private int CountTrackedInvites(string profileId)
        => _incomingInvites.Count(item => string.Equals(
               item.Key.ProfileId,
               profileId,
               StringComparison.OrdinalIgnoreCase))
           + _resolvedInvites.Count(item => string.Equals(
               item.Key.ProfileId,
               profileId,
               StringComparison.OrdinalIgnoreCase));

    private Task BroadcastNotificationAsync(
        string profileId,
        string type,
        object data,
        CancellationToken cancellationToken)
        => BroadcastAsync(profileId, "gateway.notification", new
        {
            id = Guid.NewGuid(),
            type,
            timestamp = DateTimeOffset.UtcNow,
            data
        }, cancellationToken);

    private async Task<bool> SendMeshAsync(
        string profileId,
        string peerId,
        MeshPeerMessage message,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, MeshJson);
        return await meshNetwork.SendAsync(
            profileId,
            peerId,
            MeshMessageKind.IceSignal,
            payload,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task BroadcastAsync(
        string profileId,
        string type,
        object data,
        CancellationToken cancellationToken)
    {
        if (!_connections.TryGetValue(profileId, out var connections))
            return;

        foreach (var connection in connections.Values)
        {
            try
            {
                await connection.SendAsync(type, data, cancellationToken).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                connections.TryRemove(connection.Id, out _);
            }
        }
    }

    private async Task CloseProfileSessionsAsync(string profileId, CancellationToken cancellationToken)
    {
        var sessions = _sessions
            .Where(item => string.Equals(item.Key.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var session in sessions)
        {
            if (!_sessions.TryRemove(session.Key, out var route))
                continue;
            try
            {
                await SendMeshAsync(
                    profileId,
                    route.RemotePeerId,
                    new MeshPeerMessage(
                        GatewayProtocolVersion,
                        "close",
                        session.Key.SessionId,
                        profileId,
                        null,
                        0,
                        null,
                        false),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                log?.Warning($"Could not close Mesh peer session: {exception.Message}");
            }
        }
    }

    private bool HasConnections(string profileId)
        => _connections.TryGetValue(profileId, out var connections) && !connections.IsEmpty;

    private static Task SendErrorAsync(
        GatewayConnection connection,
        string? sessionId,
        string? clientRef,
        string code,
        string reason,
        CancellationToken cancellationToken)
        => connection.SendAsync("peer.error", new
        {
            session_id = sessionId,
            client_ref = clientRef,
            code,
            reason
        }, cancellationToken);

    private static string? ReadString(JsonElement value, string name)
        => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private readonly record struct SessionKey(string ProfileId, string SessionId);
    private readonly record struct InviteKey(string ProfileId, Guid InviteUuid);
    private sealed record PeerSessionRoute(string RemotePeerId, string RemotePlayerUuid, string Kind);
    private sealed record WorldInviteRoute(WorldInviteSnapshot Invite, string RemotePeerId);
    private sealed record MeshPeerMessage(
        int Version,
        string Operation,
        string SessionId,
        string FromUuid,
        string? Kind,
        long Sequence,
        JsonElement? Payload,
        bool Accepted);
    private sealed record MeshWorldInviteMessage(
        int Version,
        string Operation,
        Guid InviteUuid,
        Guid InviterUuid,
        Guid InvitedPlayerUuid,
        DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt,
        string InviteCode);

    private sealed class GatewayConnection(string id, WebSocket socket) : IAsyncDisposable
    {
        private readonly SemaphoreSlim _sendGate = new(1, 1);

        public string Id { get; } = id;
        public WebSocket Socket { get; } = socket;

        public async Task SendAsync(string type, object data, CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new { type, data }, MeshJson);
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Socket.State == WebSocketState.Open)
                {
                    await Socket.SendAsync(
                        payload,
                        WebSocketMessageType.Text,
                        true,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _sendGate.Release();
            }
        }

        public async Task CloseAsync(
            WebSocketCloseStatus status,
            string description,
            CancellationToken cancellationToken)
        {
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Socket.State == WebSocketState.Open)
                    await Socket.CloseOutputAsync(status, description, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendGate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Socket.State == WebSocketState.Open)
                {
                    await Socket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Socket gateway closed",
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (WebSocketException)
            {
                // The peer already disconnected
            }
            finally
            {
                Socket.Dispose();
                _sendGate.Dispose();
            }
        }
    }
}

/// <summary>
/// One short-lived P2P world invitation exposed through the local social API
/// </summary>
public sealed record WorldInviteSnapshot(
    Guid InviteUuid,
    Guid InviterUuid,
    Guid InvitedPlayerUuid,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string InviteCode,
    bool IsP2P);

/// <summary>
/// Connection metadata returned when joining or accepting a world invitation
/// </summary>
public sealed record WorldJoinSnapshot(
    string? ServerHost,
    int? ServerPort,
    string? InviteCode,
    Guid? ServerUuid,
    string? ServerName,
    bool IsP2P);
