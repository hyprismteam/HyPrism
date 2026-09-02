// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HyPrism.Mesh;

/// <summary>
/// Creates and validates bounded friend-specific discovery announcements
/// </summary>
public sealed class MeshDiscoveryService : IDisposable
{
    private static ReadOnlySpan<byte> Magic => "HPD2"u8;
    private const byte ProtocolVersion = 2;
    private const int RoutingTagLength = 16;
    private const int NonceLength = 12;
    private const int AuthenticationTagLength = 16;
    private const int RotationSeconds = 60;
    private const int HeaderLength = 4 + 1 + 8 + RoutingTagLength + NonceLength + 2;
    private const int TrailerLength = AuthenticationTagLength + MeshCryptography.SignatureLength;
    private const int MinimumPlaintextLength = 8 + 2 + 1 + 1;

    private readonly ReaderWriterLockSlim _contextGate = new(LockRecursionPolicy.NoRecursion);
    private readonly MeshFriendService _friends;
    private readonly MeshSecurityLimits _limits;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private IReadOnlyDictionary<string, CachedPeer> _peers = new Dictionary<string, CachedPeer>(StringComparer.Ordinal);
    private int _disposeState;

    public MeshDiscoveryService(
        MeshFriendService friends,
        TimeProvider? timeProvider = null,
        MeshSecurityLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(friends);
        _friends = friends;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _limits = limits ?? new MeshSecurityLimits();
        _limits.Validate();
    }

    public async Task<MeshDiscoveryCycle> CreateCycleAsync(
        string profileId,
        int transportPort,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        if (transportPort is < 1 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(transportPort));

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var contexts = await _friends.GetPairwiseContextsAsync(profileId, cancellationToken)
                .ConfigureAwait(false);
            Dictionary<string, CachedPeer>? peers = new(contexts.Count, StringComparer.Ordinal);
            try
            {
                foreach (var context in contexts)
                {
                    var peer = CachedPeer.Create(context);
                    if (!peers.TryAdd(peer.Friend.PeerId, peer))
                        peer.Dispose();
                }

                var now = _timeProvider.GetUtcNow();
                var epoch = GetEpoch(now);
                var packets = peers.Values
                    .Select(peer => CreateUnsignedAnnouncement(peer, transportPort, now, epoch))
                    .ToArray();
                var signedMessages = packets
                    .Select(packet => (ReadOnlyMemory<byte>)packet.AsMemory(
                        0,
                        packet.Length - MeshCryptography.SignatureLength))
                    .ToArray();
                var signatures = await _friends.SignManyAsync(
                    profileId,
                    signedMessages,
                    cancellationToken).ConfigureAwait(false);
                if (signatures.Count != packets.Length)
                    throw new InvalidOperationException("The mesh discovery signature count is invalid");

                for (var index = 0; index < packets.Length; index++)
                {
                    signatures[index].CopyTo(
                        packets[index],
                        packets[index].Length - MeshCryptography.SignatureLength);
                }

                var routes = CreateInboundRoutes(peers.Values, now);
                ReplacePeers(peers);
                peers = null;
                return new MeshDiscoveryCycle(packets, routes);
            }
            finally
            {
                if (peers is not null)
                {
                    foreach (var peer in peers.Values)
                        peer.Dispose();
                }

                foreach (var context in contexts)
                    context.Dispose();
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public MeshResult<MeshDiscoveryAnnouncement> VerifyAnnouncement(
        ReadOnlySpan<byte> packet,
        MeshDiscoveryRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        var parsed = ParseHeader(packet);
        if (!parsed.IsSuccess)
        {
            return MeshResult<MeshDiscoveryAnnouncement>.Failed(
                parsed.Failure.Code,
                parsed.Failure.Message);
        }

        var header = parsed.Value;
        if (route.Key.Epoch != header.Epoch
            || !string.Equals(
                route.Key.RoutingTag,
                MeshCryptography.Base64UrlEncode(header.RoutingTag),
                StringComparison.Ordinal))
        {
            return MeshResult<MeshDiscoveryAnnouncement>.Failed(
                "wrong_route",
                "The mesh discovery packet does not match the selected route");
        }

        _contextGate.EnterReadLock();
        try
        {
            if (Volatile.Read(ref _disposeState) != 0
                || !_peers.TryGetValue(route.SenderPeerId, out var peer))
            {
                return MeshResult<MeshDiscoveryAnnouncement>.Failed(
                    "unknown_peer",
                    "The mesh discovery sender is not a current friend");
            }

            var expectedTag = CreateRoutingTag(
                peer.RoutingKey,
                peer.Friend.PeerId,
                peer.LocalPeerId,
                header.Epoch);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(expectedTag, header.RoutingTag))
                {
                    return MeshResult<MeshDiscoveryAnnouncement>.Failed(
                        "wrong_route",
                        "The mesh discovery routing tag is invalid");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(expectedTag);
            }

            var signedLength = packet.Length - MeshCryptography.SignatureLength;
            if (!MeshCryptography.Verify(
                    peer.SigningPublicKey,
                    packet[..signedLength],
                    packet[signedLength..]))
            {
                return MeshResult<MeshDiscoveryAnnouncement>.Failed(
                    "invalid_signature",
                    "The mesh discovery signature is invalid");
            }

            var plaintext = new byte[header.CiphertextLength];
            try
            {
                using (var cipher = new AesGcm(peer.InboundEncryptionKey, AuthenticationTagLength))
                {
                    cipher.Decrypt(
                        header.Nonce,
                        packet.Slice(HeaderLength, header.CiphertextLength),
                        packet.Slice(HeaderLength + header.CiphertextLength, AuthenticationTagLength),
                        plaintext,
                        packet[..HeaderLength]);
                }

                return ParsePlaintext(plaintext, peer, header.Epoch);
            }
            catch (CryptographicException)
            {
                return MeshResult<MeshDiscoveryAnnouncement>.Failed(
                    "decryption_failed",
                    "The mesh discovery packet could not be authenticated");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        finally
        {
            _contextGate.ExitReadLock();
        }
    }

    public static bool TryReadRoute(ReadOnlySpan<byte> packet, out MeshDiscoveryRouteKey route)
    {
        route = default;
        if (packet.Length < HeaderLength + MinimumPlaintextLength + TrailerLength
            || !packet[..Magic.Length].SequenceEqual(Magic)
            || packet[4] != ProtocolVersion)
        {
            return false;
        }

        var ciphertextLength = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(41, 2));
        if (ciphertextLength < MinimumPlaintextLength
            || HeaderLength + ciphertextLength + TrailerLength != packet.Length)
        {
            return false;
        }

        route = new MeshDiscoveryRouteKey(
            BinaryPrimitives.ReadInt64BigEndian(packet.Slice(5, 8)),
            MeshCryptography.Base64UrlEncode(packet.Slice(13, RoutingTagLength)));
        return true;
    }

    private byte[] CreateUnsignedAnnouncement(
        CachedPeer peer,
        int transportPort,
        DateTimeOffset issuedAt,
        long epoch)
    {
        var senderPeerId = Encoding.UTF8.GetBytes(peer.LocalPeerId);
        var recipientPeerId = Encoding.UTF8.GetBytes(peer.Friend.PeerId);
        if (senderPeerId.Length is 0 or > byte.MaxValue
            || recipientPeerId.Length is 0 or > byte.MaxValue)
        {
            throw new InvalidOperationException("A mesh Peer ID exceeds the discovery wire-format limit");
        }

        var plaintext = new byte[
            MinimumPlaintextLength + senderPeerId.Length + recipientPeerId.Length];
        var offset = 0;
        BinaryPrimitives.WriteInt64BigEndian(
            plaintext.AsSpan(offset, 8),
            issuedAt.ToUnixTimeSeconds());
        offset += 8;
        BinaryPrimitives.WriteUInt16BigEndian(plaintext.AsSpan(offset, 2), (ushort)transportPort);
        offset += 2;
        plaintext[offset++] = (byte)senderPeerId.Length;
        senderPeerId.CopyTo(plaintext, offset);
        offset += senderPeerId.Length;
        plaintext[offset++] = (byte)recipientPeerId.Length;
        recipientPeerId.CopyTo(plaintext, offset);

        var routingTag = CreateRoutingTag(
            peer.RoutingKey,
            peer.LocalPeerId,
            peer.Friend.PeerId,
            epoch);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        try
        {
            var packet = new byte[HeaderLength + plaintext.Length + TrailerLength];
            Magic.CopyTo(packet);
            packet[4] = ProtocolVersion;
            BinaryPrimitives.WriteInt64BigEndian(packet.AsSpan(5, 8), epoch);
            routingTag.CopyTo(packet, 13);
            nonce.CopyTo(packet, 29);
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(41, 2), (ushort)plaintext.Length);

            using (var cipher = new AesGcm(peer.OutboundEncryptionKey, AuthenticationTagLength))
            {
                cipher.Encrypt(
                    nonce,
                    plaintext,
                    packet.AsSpan(HeaderLength, plaintext.Length),
                    packet.AsSpan(HeaderLength + plaintext.Length, AuthenticationTagLength),
                    packet.AsSpan(0, HeaderLength));
            }

            if (packet.Length > _limits.MaximumDiscoveryPacketBytes)
                throw new InvalidOperationException("The mesh discovery packet exceeds the configured size limit");
            return packet;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(routingTag);
            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    private IReadOnlyList<MeshDiscoveryRoute> CreateInboundRoutes(
        IEnumerable<CachedPeer> peers,
        DateTimeOffset now)
    {
        var firstEpoch = GetEpoch(now.Subtract(_limits.DiscoveryAnnouncementLifetime));
        var lastEpoch = GetEpoch(now.Add(_limits.DiscoveryClockSkew));
        var routes = new List<MeshDiscoveryRoute>();
        foreach (var peer in peers)
        {
            for (var epoch = firstEpoch; epoch <= lastEpoch; epoch++)
            {
                var routingTag = CreateRoutingTag(
                    peer.RoutingKey,
                    peer.Friend.PeerId,
                    peer.LocalPeerId,
                    epoch);
                try
                {
                    routes.Add(new MeshDiscoveryRoute(
                        new MeshDiscoveryRouteKey(
                            epoch,
                            MeshCryptography.Base64UrlEncode(routingTag)),
                        peer.Friend.PeerId));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(routingTag);
                }
            }
        }
        return routes;
    }

    private MeshResult<ParsedDiscoveryHeader> ParseHeader(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < HeaderLength + MinimumPlaintextLength + TrailerLength
            || packet.Length > _limits.MaximumDiscoveryPacketBytes
            || !packet[..Magic.Length].SequenceEqual(Magic)
            || packet[4] != ProtocolVersion)
        {
            return MeshResult<ParsedDiscoveryHeader>.Failed(
                "invalid_discovery",
                "The mesh discovery packet is malformed");
        }

        var ciphertextLength = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(41, 2));
        if (ciphertextLength < MinimumPlaintextLength
            || HeaderLength + ciphertextLength + TrailerLength != packet.Length)
        {
            return MeshResult<ParsedDiscoveryHeader>.Failed(
                "invalid_discovery",
                "The mesh discovery packet length is invalid");
        }

        return MeshResult<ParsedDiscoveryHeader>.Success(new ParsedDiscoveryHeader(
            BinaryPrimitives.ReadInt64BigEndian(packet.Slice(5, 8)),
            packet.Slice(13, RoutingTagLength).ToArray(),
            packet.Slice(29, NonceLength).ToArray(),
            ciphertextLength));
    }

    private MeshResult<MeshDiscoveryAnnouncement> ParsePlaintext(
        ReadOnlySpan<byte> plaintext,
        CachedPeer peer,
        long epoch)
    {
        if (plaintext.Length < MinimumPlaintextLength)
        {
            return MeshResult<MeshDiscoveryAnnouncement>.Failed(
                "invalid_discovery",
                "The mesh discovery plaintext is malformed");
        }

        DateTimeOffset issuedAt;
        try
        {
            issuedAt = DateTimeOffset.FromUnixTimeSeconds(
                BinaryPrimitives.ReadInt64BigEndian(plaintext[..8]));
        }
        catch (ArgumentOutOfRangeException)
        {
            return MeshResult<MeshDiscoveryAnnouncement>.Failed(
                "invalid_time",
                "The mesh discovery timestamp is invalid");
        }

        var port = BinaryPrimitives.ReadUInt16BigEndian(plaintext.Slice(8, 2));
        var offset = 10;
        if (port == 0
            || !TryReadPeerId(plaintext, ref offset, out var senderPeerId)
            || !TryReadPeerId(plaintext, ref offset, out var recipientPeerId)
            || offset != plaintext.Length)
        {
            return MeshResult<MeshDiscoveryAnnouncement>.Failed(
                "invalid_discovery",
                "The mesh discovery identity or transport port is invalid");
        }

        if (!string.Equals(senderPeerId, peer.Friend.PeerId, StringComparison.Ordinal)
            || !string.Equals(recipientPeerId, peer.LocalPeerId, StringComparison.Ordinal))
        {
            return MeshResult<MeshDiscoveryAnnouncement>.Failed(
                "wrong_recipient",
                "The mesh discovery identity binding is invalid");
        }

        var now = _timeProvider.GetUtcNow();
        if (GetEpoch(issuedAt) != epoch
            || issuedAt > now.Add(_limits.DiscoveryClockSkew)
            || now - issuedAt > _limits.DiscoveryAnnouncementLifetime)
        {
            return MeshResult<MeshDiscoveryAnnouncement>.Failed(
                "expired",
                "The mesh discovery packet has expired");
        }

        return MeshResult<MeshDiscoveryAnnouncement>.Success(
            new MeshDiscoveryAnnouncement(senderPeerId, port, issuedAt));
    }

    private static bool TryReadPeerId(ReadOnlySpan<byte> plaintext, ref int offset, out string peerId)
    {
        peerId = string.Empty;
        if (offset >= plaintext.Length)
            return false;
        var length = plaintext[offset++];
        if (length == 0 || offset + length > plaintext.Length)
            return false;

        try
        {
            peerId = new UTF8Encoding(false, true).GetString(plaintext.Slice(offset, length));
            offset += length;
            return peerId.Length <= 128 && !peerId.Any(char.IsControl);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private void ReplacePeers(IReadOnlyDictionary<string, CachedPeer> peers)
    {
        _contextGate.EnterWriteLock();
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
            var previous = _peers;
            _peers = peers;
            foreach (var peer in previous.Values)
                peer.Dispose();
        }
        finally
        {
            _contextGate.ExitWriteLock();
        }
    }

    private static long GetEpoch(DateTimeOffset value)
        => value.ToUnixTimeSeconds() / RotationSeconds;

    private static byte[] CreateRoutingTag(
        ReadOnlySpan<byte> routingKey,
        string senderPeerId,
        string recipientPeerId,
        long epoch)
    {
        var context = Encoding.UTF8.GetBytes(
            $"HyPrism discovery route v2\0{senderPeerId}\0{recipientPeerId}\0{epoch}");
        var digest = HMACSHA256.HashData(routingKey, context);
        try
        {
            return digest.AsSpan(0, RoutingTagLength).ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(context);
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    private static byte[] DeriveKey(ReadOnlySpan<byte> pairwiseKey, string purpose)
    {
        var context = Encoding.UTF8.GetBytes(purpose);
        try
        {
            return HMACSHA256.HashData(pairwiseKey, context);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(context);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        _contextGate.EnterWriteLock();
        try
        {
            foreach (var peer in _peers.Values)
                peer.Dispose();
            _peers = new Dictionary<string, CachedPeer>(StringComparer.Ordinal);
        }
        finally
        {
            _contextGate.ExitWriteLock();
        }

        _refreshGate.Dispose();
        _contextGate.Dispose();
    }

    private sealed class CachedPeer : IDisposable
    {
        private CachedPeer(
            string localPeerId,
            MeshFriend friend,
            byte[] routingKey,
            byte[] outboundEncryptionKey,
            byte[] inboundEncryptionKey,
            byte[] signingPublicKey)
        {
            LocalPeerId = localPeerId;
            Friend = friend;
            RoutingKey = routingKey;
            OutboundEncryptionKey = outboundEncryptionKey;
            InboundEncryptionKey = inboundEncryptionKey;
            SigningPublicKey = signingPublicKey;
        }

        public string LocalPeerId { get; }
        public MeshFriend Friend { get; }
        public byte[] RoutingKey { get; }
        public byte[] OutboundEncryptionKey { get; }
        public byte[] InboundEncryptionKey { get; }
        public byte[] SigningPublicKey { get; }

        public static CachedPeer Create(MeshPairwiseContext context)
        {
            if (!MeshCryptography.TryBase64UrlDecode(
                    context.RemoteFriend.SigningPublicKey,
                    MeshCryptography.PublicKeyLength,
                    out var signingPublicKey))
            {
                throw new InvalidDataException("The friend signing key is malformed");
            }

            byte[]? routingKey = null;
            byte[]? outboundEncryptionKey = null;
            byte[]? inboundEncryptionKey = null;
            try
            {
                routingKey = DeriveKey(context.Key, "HyPrism discovery routing key v2");
                outboundEncryptionKey = DeriveKey(
                    context.Key,
                    $"HyPrism discovery encryption v2\0{context.LocalIdentity.PeerId}\0{context.RemoteFriend.PeerId}");
                inboundEncryptionKey = DeriveKey(
                    context.Key,
                    $"HyPrism discovery encryption v2\0{context.RemoteFriend.PeerId}\0{context.LocalIdentity.PeerId}");
                return new CachedPeer(
                    context.LocalIdentity.PeerId,
                    context.RemoteFriend,
                    routingKey,
                    outboundEncryptionKey,
                    inboundEncryptionKey,
                    signingPublicKey);
            }
            catch
            {
                if (routingKey is not null)
                    CryptographicOperations.ZeroMemory(routingKey);
                if (outboundEncryptionKey is not null)
                    CryptographicOperations.ZeroMemory(outboundEncryptionKey);
                if (inboundEncryptionKey is not null)
                    CryptographicOperations.ZeroMemory(inboundEncryptionKey);
                CryptographicOperations.ZeroMemory(signingPublicKey);
                throw;
            }
        }

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(RoutingKey);
            CryptographicOperations.ZeroMemory(OutboundEncryptionKey);
            CryptographicOperations.ZeroMemory(InboundEncryptionKey);
            CryptographicOperations.ZeroMemory(SigningPublicKey);
        }
    }

    private sealed record ParsedDiscoveryHeader(
        long Epoch,
        byte[] RoutingTag,
        byte[] Nonce,
        int CiphertextLength);
}
