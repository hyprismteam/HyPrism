// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Mesh;

/// <summary>
/// Public, non-secret identity of one autonomous HyPrism profile
/// </summary>
public sealed record MeshPublicIdentity(
    string PeerId,
    string SigningPublicKey,
    string AgreementPublicKey,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// Short stable identifier accepted by Hytale's add-friend dialog
    /// </summary>
    public string FriendId => MeshFriendId.FromIdentity(this);
}

/// <summary>
/// A locally confirmed friend identity
/// </summary>
public sealed record MeshFriend(
    string PeerId,
    string SigningPublicKey,
    string AgreementPublicKey,
    string DisplayName,
    DateTimeOffset AddedAt,
    string? PlayerUuid = null);

/// <summary>
/// Application message carried by an authenticated pairwise mesh channel
/// </summary>
public sealed record MeshMessage(
    string MessageId,
    string SenderPeerId,
    string RecipientPeerId,
    MeshMessageKind Kind,
    DateTimeOffset IssuedAt,
    ReadOnlyMemory<byte> Payload);

/// <summary>
/// Authenticated message plus bounded opaque metadata supplied by its network transport
/// </summary>
public sealed record MeshInboundDelivery(
    MeshMessage Message,
    string? TransportContext);

/// <summary>
/// A signed LAN announcement that binds a mesh identity to its current UDP endpoint
/// </summary>
public sealed record MeshDiscoveryAnnouncement(
    string PeerId,
    int TransportPort,
    DateTimeOffset IssuedAt);

/// <summary>
/// Public lookup key for one rotating friend-specific discovery route
/// </summary>
public readonly record struct MeshDiscoveryRouteKey(long Epoch, string RoutingTag);

/// <summary>
/// Binds one opaque discovery route to the confirmed friend expected on it
/// </summary>
public sealed record MeshDiscoveryRoute(
    MeshDiscoveryRouteKey Key,
    string SenderPeerId);

/// <summary>
/// Personalized discovery packets and bounded inbound routes for one announcement cycle
/// </summary>
public sealed record MeshDiscoveryCycle(
    IReadOnlyList<byte[]> Announcements,
    IReadOnlyList<MeshDiscoveryRoute> InboundRoutes);

/// <summary>
/// Last authenticated presence received from a confirmed friend
/// </summary>
public sealed record MeshPeerPresence(
    string PeerId,
    string DisplayName,
    bool IsOnline,
    DateTimeOffset LastSeenAt);

public enum MeshMessageKind : byte
{
    Presence = 1,
    FriendState = 2,
    WorldInvite = 3,
    IceSignal = 4,
    Mailbox = 5
}

/// <summary>
/// A short-lived invitation that can be transferred out of band
/// </summary>
public sealed record MeshFriendInvite(
    string Token,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Verified sender metadata carried by a friendship invitation
/// </summary>
public sealed record MeshFriendInviteDetails(
    string PeerId,
    string FriendId,
    string DisplayName,
    string? PlayerUuid,
    string? TargetFriendId,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Signed public identity record used while two profiles are not friends yet
/// </summary>
public sealed record MeshPeerRecord(
    string Token,
    string PeerId,
    string FriendId,
    string DisplayName,
    string? PlayerUuid,
    string RequestId,
    string Purpose,
    DateTimeOffset ExpiresAt);

/// <summary>
/// The local acceptance of an invitation and the response token for its issuer
/// </summary>
public sealed record MeshFriendAcceptance(
    MeshFriend Friend,
    string AcceptanceToken,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Expected failure returned by a mesh friendship operation
/// </summary>
public sealed record MeshFailure(string Code, string Message);

/// <summary>
/// Result of a mesh friendship operation without exception-based control flow
/// </summary>
public readonly record struct MeshResult<T>
{
    private readonly T? _value;
    private readonly MeshFailure? _failure;

    private MeshResult(T value)
    {
        _value = value;
        _failure = null;
        IsSuccess = true;
    }

    private MeshResult(MeshFailure failure)
    {
        _value = default;
        _failure = failure;
        IsSuccess = false;
    }

    public bool IsSuccess { get; }
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed mesh result does not contain a value");
    public MeshFailure Failure => !IsSuccess
        ? _failure!
        : throw new InvalidOperationException("A successful mesh result does not contain a failure");

    public static MeshResult<T> Success(T value) => new(value);
    public static MeshResult<T> Failed(string code, string message) => new(new MeshFailure(code, message));
}

/// <summary>
/// Bounded resource policy for local mesh friendship state
/// </summary>
public sealed record MeshSecurityLimits
{
    public const int ProtocolCandidateLimit = 16;

    public int MaximumFriendsPerProfile { get; init; } = 512;
    public int MaximumIssuedInvitesPerProfile { get; init; } = 128;
    public int MaximumConsumedInvitesPerProfile { get; init; } = 1024;
    public int MaximumTokenLength { get; init; } = 4096;
    public int MaximumDisplayNameLength { get; init; } = 64;
    public int MaximumEnvelopePayloadBytes { get; init; } = 16 * 1024;
    public int MaximumInboundEnvelopeBytes { get; init; } = 24 * 1024;
    public int MaximumInboundQueueDepth { get; init; } = 256;
    public int MaximumReplayEntriesPerFriend { get; init; } = 128;
    public int MaximumDiscoveryPacketBytes { get; init; } = 512;
    public int MaximumActiveProfiles { get; init; } = 8;
    public int MaximumTrackedNetworkSources { get; init; } = 1024;
    public int NetworkPacketsPerSecond { get; init; } = 24;
    public int NetworkPacketBurst { get; init; } = 48;
    public TimeSpan MaximumInviteLifetime { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan MaximumClockSkew { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan MaximumEnvelopeLifetime { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan DiscoveryAnnouncementLifetime { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan DiscoveryClockSkew { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan NetworkSourceIdleLifetime { get; init; } = TimeSpan.FromMinutes(5);

    public void Validate()
    {
        if (MaximumFriendsPerProfile <= 0
            || MaximumIssuedInvitesPerProfile <= 0
            || MaximumConsumedInvitesPerProfile <= 0
            || MaximumTokenLength < 512
            || MaximumDisplayNameLength <= 0
            || MaximumEnvelopePayloadBytes <= 0
            || MaximumInboundEnvelopeBytes <= MaximumEnvelopePayloadBytes
            || MaximumInboundQueueDepth <= 0
            || MaximumReplayEntriesPerFriend <= 0
            || MaximumDiscoveryPacketBytes < 256
            || MaximumActiveProfiles <= 0
            || MaximumTrackedNetworkSources <= 0
            || NetworkPacketsPerSecond <= 0
            || NetworkPacketBurst < NetworkPacketsPerSecond
            || MaximumInviteLifetime <= TimeSpan.Zero
            || MaximumClockSkew < TimeSpan.Zero
            || MaximumEnvelopeLifetime <= TimeSpan.Zero
            || DiscoveryAnnouncementLifetime <= TimeSpan.Zero
            || DiscoveryAnnouncementLifetime > TimeSpan.FromMinutes(1)
            || DiscoveryClockSkew < TimeSpan.Zero
            || DiscoveryClockSkew > TimeSpan.FromMinutes(1)
            || NetworkSourceIdleLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MeshSecurityLimits), "Mesh security limits must be positive");
        }
    }
}
