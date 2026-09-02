// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HyPrism.Mesh;

/// <summary>
/// Creates and verifies bounded, signed, out-of-band friendship capabilities
/// </summary>
public sealed class MeshFriendService
{
    private const int ProtocolVersion = 2;
    private const string InvitePrefix = "hyprism://friend/v2/invite/";
    private const string AcceptancePrefix = "hyprism://friend/v2/accept/";
    private static readonly JsonSerializerOptions TokenSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly MeshIdentityStore _identities;
    private readonly MeshStateStore _state;
    private readonly MeshSecurityLimits _limits;
    private readonly TimeProvider _timeProvider;

    public MeshFriendService(
        string dataDirectory,
        TimeProvider? timeProvider = null,
        MeshSecurityLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _identities = new MeshIdentityStore(dataDirectory);
        _state = new MeshStateStore(dataDirectory);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _limits = limits ?? new MeshSecurityLimits();
        _limits.Validate();
    }

    public Task<MeshPublicIdentity> GetIdentityAsync(
        string profileId,
        CancellationToken cancellationToken = default)
        => _identities.GetPublicIdentityAsync(profileId, cancellationToken);

    public async Task<IReadOnlyList<MeshFriend>> GetFriendsAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ValidateProfileId(profileId);
        return await _state.ReadAsync(
            profileId,
            profile => (IReadOnlyList<MeshFriend>)profile.Friends
                .Select(friend => friend.ToFriend())
                .OrderBy(friend => friend.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            cancellationToken).ConfigureAwait(false);
    }

    internal Task<byte[]> SignAsync(
        string profileId,
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken)
        => _identities.SignAsync(profileId, message, cancellationToken);

    internal Task<IReadOnlyList<byte[]>> SignManyAsync(
        string profileId,
        IReadOnlyList<ReadOnlyMemory<byte>> messages,
        CancellationToken cancellationToken)
        => _identities.SignManyAsync(profileId, messages, cancellationToken);

    internal async Task<IReadOnlyList<MeshPairwiseContext>> GetPairwiseContextsAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        ValidateProfileId(profileId);
        var friends = await _state.ReadAsync(
            profileId,
            profile => profile.Friends
                .Select(friend => friend.ToFriend())
                .Where(friend => !string.IsNullOrWhiteSpace(friend.AgreementPublicKey))
                .ToArray(),
            cancellationToken).ConfigureAwait(false);
        if (friends.Length == 0)
            return [];

        var derived = await _identities.DerivePairwiseKeysAsync(
            profileId,
            friends,
            cancellationToken).ConfigureAwait(false);
        var contexts = new MeshPairwiseContext[friends.Length];
        for (var index = 0; index < friends.Length; index++)
            contexts[index] = new MeshPairwiseContext(derived.Identity, friends[index], derived.Keys[index]);
        return contexts;
    }

    internal async Task<MeshResult<MeshPairwiseContext>> GetPairwiseContextAsync(
        string profileId,
        string remotePeerId,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateProfileId(profileId);
        }
        catch (ArgumentException exception)
        {
            return MeshResult<MeshPairwiseContext>.Failed("invalid_profile", exception.Message);
        }

        if (string.IsNullOrWhiteSpace(remotePeerId) || remotePeerId.Length > 128)
            return MeshResult<MeshPairwiseContext>.Failed("invalid_peer", "A valid remote Peer ID is required");

        var friend = await _state.ReadAsync(
            profileId,
            profile => profile.Friends
                .FirstOrDefault(item => string.Equals(item.PeerId, remotePeerId, StringComparison.Ordinal))
                ?.ToFriend(),
            cancellationToken).ConfigureAwait(false);
        if (friend is null)
            return MeshResult<MeshPairwiseContext>.Failed("unknown_friend", "The remote Peer ID is not a friend");
        if (string.IsNullOrWhiteSpace(friend.AgreementPublicKey))
        {
            return MeshResult<MeshPairwiseContext>.Failed(
                "agreement_key_missing",
                "The friendship predates pairwise encryption and must be paired again");
        }

        var identity = await _identities.GetPublicIdentityAsync(profileId, cancellationToken).ConfigureAwait(false);
        var key = await _identities.DerivePairwiseKeyAsync(
            profileId,
            friend.PeerId,
            friend.AgreementPublicKey,
            cancellationToken).ConfigureAwait(false);
        return MeshResult<MeshPairwiseContext>.Success(new MeshPairwiseContext(identity, friend, key));
    }

    public async Task<MeshResult<MeshFriendInvite>> CreateInviteAsync(
        string profileId,
        string displayName,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateInput(profileId, displayName);
        if (validation is not null)
            return MeshResult<MeshFriendInvite>.Failed(validation.Code, validation.Message);
        if (lifetime <= TimeSpan.Zero || lifetime > _limits.MaximumInviteLifetime)
        {
            return MeshResult<MeshFriendInvite>.Failed(
                "invalid_lifetime",
                $"Invite lifetime must be between one tick and {_limits.MaximumInviteLifetime}");
        }

        var identity = await _identities.GetPublicIdentityAsync(profileId, cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.Add(lifetime);
        var capability = RandomNumberGenerator.GetBytes(MeshCryptography.CapabilityLength);
        try
        {
            var payload = new InvitePayload(
                ProtocolVersion,
                identity.PeerId,
                identity.SigningPublicKey,
                identity.AgreementPublicKey,
                displayName,
                MeshCryptography.Base64UrlEncode(capability),
                now.ToUnixTimeSeconds(),
                expiresAt.ToUnixTimeSeconds());
            var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, TokenSerializerOptions);
            var signature = await _identities.SignAsync(profileId, payloadBytes, cancellationToken).ConfigureAwait(false);
            var token = InvitePrefix
                        + MeshCryptography.Base64UrlEncode(payloadBytes)
                        + "."
                        + MeshCryptography.Base64UrlEncode(signature);
            if (token.Length > _limits.MaximumTokenLength)
            {
                return MeshResult<MeshFriendInvite>.Failed(
                    "token_too_large",
                    "The generated friendship invitation exceeds the configured size limit");
            }

            var inviteHash = MeshCryptography.HashCapability(capability);
            var storeResult = await _state.UpdateAsync(
                profileId,
                profile => StoreIssuedInvite(profile, inviteHash, expiresAt, now),
                cancellationToken).ConfigureAwait(false);
            return storeResult.IsSuccess
                ? MeshResult<MeshFriendInvite>.Success(new MeshFriendInvite(token, expiresAt))
                : MeshResult<MeshFriendInvite>.Failed(storeResult.Failure.Code, storeResult.Failure.Message);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(capability);
        }
    }

    public async Task<MeshResult<MeshFriendAcceptance>> AcceptInviteAsync(
        string profileId,
        string displayName,
        string inviteToken,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateInput(profileId, displayName);
        if (validation is not null)
            return MeshResult<MeshFriendAcceptance>.Failed(validation.Code, validation.Message);

        var now = _timeProvider.GetUtcNow();
        var inviteResult = ParseInvite(inviteToken, now);
        if (!inviteResult.IsSuccess)
        {
            return MeshResult<MeshFriendAcceptance>.Failed(
                inviteResult.Failure.Code,
                inviteResult.Failure.Message);
        }

        var invite = inviteResult.Value;
        var localIdentity = await _identities.GetPublicIdentityAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (string.Equals(localIdentity.PeerId, invite.PeerId, StringComparison.Ordinal))
        {
            return MeshResult<MeshFriendAcceptance>.Failed(
                "self_invite",
                "A profile cannot accept its own friendship invitation");
        }

        var acceptanceExpiresAt = invite.ExpiresAt < now.AddMinutes(10)
            ? invite.ExpiresAt
            : now.AddMinutes(10);
        var acceptancePayload = new AcceptancePayload(
            ProtocolVersion,
            invite.InviteHash,
            invite.PeerId,
            localIdentity.PeerId,
            localIdentity.SigningPublicKey,
            localIdentity.AgreementPublicKey,
            displayName,
            now.ToUnixTimeSeconds(),
            acceptanceExpiresAt.ToUnixTimeSeconds());
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(acceptancePayload, TokenSerializerOptions);
        var signature = await _identities.SignAsync(profileId, payloadBytes, cancellationToken).ConfigureAwait(false);
        var acceptanceToken = AcceptancePrefix
                              + MeshCryptography.Base64UrlEncode(payloadBytes)
                              + "."
                              + MeshCryptography.Base64UrlEncode(signature);
        if (acceptanceToken.Length > _limits.MaximumTokenLength)
        {
            return MeshResult<MeshFriendAcceptance>.Failed(
                "token_too_large",
                "The generated friendship acceptance exceeds the configured size limit");
        }

        var friend = new MeshFriend(
            invite.PeerId,
            invite.PublicKey,
            invite.AgreementPublicKey,
            invite.DisplayName,
            now);
        var stateResult = await _state.UpdateAsync(
            profileId,
            profile => StoreAcceptedInvite(profile, invite, friend, now),
            cancellationToken).ConfigureAwait(false);
        return stateResult.IsSuccess
            ? MeshResult<MeshFriendAcceptance>.Success(
                new MeshFriendAcceptance(friend, acceptanceToken, acceptanceExpiresAt))
            : MeshResult<MeshFriendAcceptance>.Failed(stateResult.Failure.Code, stateResult.Failure.Message);
    }

    public async Task<MeshResult<MeshFriend>> CompleteInviteAsync(
        string profileId,
        string acceptanceToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateProfileId(profileId);
        }
        catch (ArgumentException exception)
        {
            return MeshResult<MeshFriend>.Failed("invalid_profile", exception.Message);
        }

        var now = _timeProvider.GetUtcNow();
        var acceptanceResult = ParseAcceptance(acceptanceToken, now);
        if (!acceptanceResult.IsSuccess)
            return MeshResult<MeshFriend>.Failed(acceptanceResult.Failure.Code, acceptanceResult.Failure.Message);

        var acceptance = acceptanceResult.Value;
        var localIdentity = await _identities.GetPublicIdentityAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(localIdentity.PeerId, acceptance.InviterPeerId, StringComparison.Ordinal))
        {
            return MeshResult<MeshFriend>.Failed(
                "wrong_recipient",
                "The friendship acceptance was issued for another profile");
        }

        var friend = new MeshFriend(
            acceptance.PeerId,
            acceptance.PublicKey,
            acceptance.AgreementPublicKey,
            acceptance.DisplayName,
            now);
        return await _state.UpdateAsync(
            profileId,
            profile => CompleteIssuedInvite(profile, acceptance, friend, now),
            cancellationToken).ConfigureAwait(false);
    }

    private MeshResult<VerifiedInvite> ParseInvite(string token, DateTimeOffset now)
    {
        var envelope = ParseEnvelope(token, InvitePrefix);
        if (!envelope.IsSuccess)
            return MeshResult<VerifiedInvite>.Failed(envelope.Failure.Code, envelope.Failure.Message);

        InvitePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<InvitePayload>(envelope.Value.Payload, TokenSerializerOptions);
        }
        catch (JsonException)
        {
            return MeshResult<VerifiedInvite>.Failed("invalid_invite", "The friendship invitation is malformed");
        }

        if (payload is null
            || payload.Version != ProtocolVersion
            || !IsValidDisplayName(payload.DisplayName)
            || !MeshCryptography.TryBase64UrlDecode(
                payload.PublicKey,
                MeshCryptography.PublicKeyLength,
                out var publicKey)
            || !MeshCryptography.TryBase64UrlDecode(
                payload.AgreementPublicKey,
                MeshCryptography.AgreementKeyLength,
                out var agreementPublicKey)
            || !MeshCryptography.TryBase64UrlDecode(
                payload.Capability,
                MeshCryptography.CapabilityLength,
                out var capability))
        {
            return MeshResult<VerifiedInvite>.Failed("invalid_invite", "The friendship invitation is malformed");
        }

        try
        {
            if (!string.Equals(payload.PeerId, MeshCryptography.GetPeerId(publicKey), StringComparison.Ordinal)
                || !MeshCryptography.Verify(publicKey, envelope.Value.Payload, envelope.Value.Signature))
            {
                return MeshResult<VerifiedInvite>.Failed(
                    "invalid_signature",
                    "The friendship invitation signature is invalid");
            }

            var timeResult = ValidateTokenTime(payload.IssuedAt, payload.ExpiresAt, now);
            if (timeResult is not null)
                return MeshResult<VerifiedInvite>.Failed(timeResult.Code, timeResult.Message);

            return MeshResult<VerifiedInvite>.Success(new VerifiedInvite(
                payload.PeerId,
                payload.PublicKey,
                payload.AgreementPublicKey,
                payload.DisplayName,
                MeshCryptography.HashCapability(capability),
                DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAt)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(publicKey);
            CryptographicOperations.ZeroMemory(agreementPublicKey);
            CryptographicOperations.ZeroMemory(capability);
        }
    }

    private MeshResult<VerifiedAcceptance> ParseAcceptance(string token, DateTimeOffset now)
    {
        var envelope = ParseEnvelope(token, AcceptancePrefix);
        if (!envelope.IsSuccess)
            return MeshResult<VerifiedAcceptance>.Failed(envelope.Failure.Code, envelope.Failure.Message);

        AcceptancePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<AcceptancePayload>(envelope.Value.Payload, TokenSerializerOptions);
        }
        catch (JsonException)
        {
            return MeshResult<VerifiedAcceptance>.Failed("invalid_acceptance", "The friendship acceptance is malformed");
        }

        if (payload is null
            || payload.Version != ProtocolVersion
            || payload.InviteHash.Length is < 32 or > 128
            || !IsValidDisplayName(payload.DisplayName)
            || !MeshCryptography.TryBase64UrlDecode(
                payload.PublicKey,
                MeshCryptography.PublicKeyLength,
                out var publicKey)
            || !MeshCryptography.TryBase64UrlDecode(
                payload.AgreementPublicKey,
                MeshCryptography.AgreementKeyLength,
                out var agreementPublicKey))
        {
            return MeshResult<VerifiedAcceptance>.Failed(
                "invalid_acceptance",
                "The friendship acceptance is malformed");
        }

        try
        {
            if (!string.Equals(payload.PeerId, MeshCryptography.GetPeerId(publicKey), StringComparison.Ordinal)
                || !MeshCryptography.Verify(publicKey, envelope.Value.Payload, envelope.Value.Signature))
            {
                return MeshResult<VerifiedAcceptance>.Failed(
                    "invalid_signature",
                    "The friendship acceptance signature is invalid");
            }

            var timeResult = ValidateTokenTime(payload.IssuedAt, payload.ExpiresAt, now);
            if (timeResult is not null)
                return MeshResult<VerifiedAcceptance>.Failed(timeResult.Code, timeResult.Message);

            return MeshResult<VerifiedAcceptance>.Success(new VerifiedAcceptance(
                payload.InviteHash,
                payload.InviterPeerId,
                payload.PeerId,
                payload.PublicKey,
                payload.AgreementPublicKey,
                payload.DisplayName));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(publicKey);
            CryptographicOperations.ZeroMemory(agreementPublicKey);
        }
    }

    private MeshResult<TokenEnvelope> ParseEnvelope(string? token, string prefix)
    {
        if (string.IsNullOrWhiteSpace(token)
            || token.Length > _limits.MaximumTokenLength
            || !token.StartsWith(prefix, StringComparison.Ordinal))
        {
            return MeshResult<TokenEnvelope>.Failed("invalid_token", "The friendship token is invalid");
        }

        var encodedEnvelope = token[prefix.Length..];
        var separator = encodedEnvelope.IndexOf('.');
        if (separator <= 0
            || separator != encodedEnvelope.LastIndexOf('.')
            || separator == encodedEnvelope.Length - 1)
        {
            return MeshResult<TokenEnvelope>.Failed("invalid_token", "The friendship token is invalid");
        }

        if (!TryDecodeVariable(encodedEnvelope[..separator], _limits.MaximumTokenLength, out var payload)
            || !MeshCryptography.TryBase64UrlDecode(
                encodedEnvelope[(separator + 1)..],
                MeshCryptography.SignatureLength,
                out var signature))
        {
            return MeshResult<TokenEnvelope>.Failed("invalid_token", "The friendship token is invalid");
        }

        return MeshResult<TokenEnvelope>.Success(new TokenEnvelope(payload, signature));
    }

    private MeshFailure? ValidateTokenTime(long issuedAtValue, long expiresAtValue, DateTimeOffset now)
    {
        DateTimeOffset issuedAt;
        DateTimeOffset expiresAt;
        try
        {
            issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtValue);
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresAtValue);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new MeshFailure("invalid_time", "The friendship token contains an invalid timestamp");
        }

        if (issuedAt > now.Add(_limits.MaximumClockSkew)
            || expiresAt <= issuedAt
            || expiresAt - issuedAt > _limits.MaximumInviteLifetime)
        {
            return new MeshFailure("invalid_time", "The friendship token lifetime is invalid");
        }

        return expiresAt <= now
            ? new MeshFailure("expired", "The friendship token has expired")
            : null;
    }

    private MeshResult<bool> StoreIssuedInvite(
        MeshProfileState profile,
        string inviteHash,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        Prune(profile, now);
        if (profile.IssuedInvites.Count >= _limits.MaximumIssuedInvitesPerProfile)
        {
            return MeshResult<bool>.Failed(
                "invite_limit_reached",
                "Too many active friendship invitations exist for this profile");
        }

        profile.IssuedInvites.Add(new MeshIssuedInviteState
        {
            InviteHash = inviteHash,
            ExpiresAt = expiresAt
        });
        return MeshResult<bool>.Success(true);
    }

    private MeshResult<bool> StoreAcceptedInvite(
        MeshProfileState profile,
        VerifiedInvite invite,
        MeshFriend friend,
        DateTimeOffset now)
    {
        Prune(profile, now);
        if (profile.ConsumedInvites.Any(item =>
                string.Equals(item.InviteHash, invite.InviteHash, StringComparison.Ordinal)))
        {
            return MeshResult<bool>.Failed("replayed_invite", "This friendship invitation was already consumed");
        }

        if (profile.ConsumedInvites.Count >= _limits.MaximumConsumedInvitesPerProfile)
        {
            return MeshResult<bool>.Failed(
                "replay_cache_full",
                "The friendship replay cache reached its safety limit");
        }

        var existing = profile.Friends.FirstOrDefault(item =>
            string.Equals(item.PeerId, friend.PeerId, StringComparison.Ordinal));
        if (existing is null && profile.Friends.Count >= _limits.MaximumFriendsPerProfile)
            return MeshResult<bool>.Failed("friend_limit_reached", "The local friend limit was reached");

        profile.ConsumedInvites.Add(new MeshConsumedInviteState
        {
            InviteHash = invite.InviteHash,
            ExpiresAt = invite.ExpiresAt
        });
        UpsertFriend(profile, friend);
        return MeshResult<bool>.Success(true);
    }

    private MeshResult<MeshFriend> CompleteIssuedInvite(
        MeshProfileState profile,
        VerifiedAcceptance acceptance,
        MeshFriend friend,
        DateTimeOffset now)
    {
        Prune(profile, now);
        var issued = profile.IssuedInvites.FirstOrDefault(item =>
            string.Equals(item.InviteHash, acceptance.InviteHash, StringComparison.Ordinal));
        if (issued is null)
        {
            return MeshResult<MeshFriend>.Failed(
                "unknown_invite",
                "The original friendship invitation is unknown or expired");
        }
        if (issued.AcceptedAt is not null)
            return MeshResult<MeshFriend>.Failed("replayed_acceptance", "This friendship acceptance was already used");

        var existing = profile.Friends.FirstOrDefault(item =>
            string.Equals(item.PeerId, friend.PeerId, StringComparison.Ordinal));
        if (existing is null && profile.Friends.Count >= _limits.MaximumFriendsPerProfile)
            return MeshResult<MeshFriend>.Failed("friend_limit_reached", "The local friend limit was reached");

        issued.AcceptedAt = now;
        UpsertFriend(profile, friend);
        return MeshResult<MeshFriend>.Success(friend);
    }

    private void Prune(MeshProfileState profile, DateTimeOffset now)
    {
        profile.IssuedInvites.RemoveAll(invite => invite.ExpiresAt <= now);
        profile.ConsumedInvites.RemoveAll(invite => invite.ExpiresAt <= now);
    }

    private static void UpsertFriend(MeshProfileState profile, MeshFriend friend)
    {
        var existing = profile.Friends.FirstOrDefault(item =>
            string.Equals(item.PeerId, friend.PeerId, StringComparison.Ordinal));
        if (existing is null)
        {
            profile.Friends.Add(new MeshFriendState
            {
                PeerId = friend.PeerId,
                SigningPublicKey = friend.SigningPublicKey,
                AgreementPublicKey = friend.AgreementPublicKey,
                DisplayName = friend.DisplayName,
                AddedAt = friend.AddedAt
            });
            return;
        }

        existing.SigningPublicKey = friend.SigningPublicKey;
        existing.AgreementPublicKey = friend.AgreementPublicKey;
        existing.DisplayName = friend.DisplayName;
    }

    private MeshFailure? ValidateInput(string profileId, string displayName)
    {
        try
        {
            ValidateProfileId(profileId);
        }
        catch (ArgumentException exception)
        {
            return new MeshFailure("invalid_profile", exception.Message);
        }

        return IsValidDisplayName(displayName)
            ? null
            : new MeshFailure("invalid_display_name", "A valid display name is required");
    }

    private bool IsValidDisplayName(string? displayName)
        => !string.IsNullOrWhiteSpace(displayName)
           && displayName.Length <= _limits.MaximumDisplayNameLength
           && !displayName.Any(char.IsControl);

    private static void ValidateProfileId(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId)
            || profileId.Length > 128
            || profileId.Any(char.IsControl))
        {
            throw new ArgumentException("A valid launcher profile ID is required", nameof(profileId));
        }
    }

    private static bool TryDecodeVariable(string value, int maximumLength, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
            return false;

        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized += (normalized.Length % 4) switch
            {
                2 => "==",
                3 => "=",
                _ => string.Empty
            };
            bytes = Convert.FromBase64String(normalized);
            if (bytes.Length <= maximumLength)
                return true;
            CryptographicOperations.ZeroMemory(bytes);
            bytes = [];
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed record TokenEnvelope(byte[] Payload, byte[] Signature);
    private sealed record VerifiedInvite(
        string PeerId,
        string PublicKey,
        string AgreementPublicKey,
        string DisplayName,
        string InviteHash,
        DateTimeOffset ExpiresAt);
    private sealed record VerifiedAcceptance(
        string InviteHash,
        string InviterPeerId,
        string PeerId,
        string PublicKey,
        string AgreementPublicKey,
        string DisplayName);

    private sealed record InvitePayload(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("peerId")] string PeerId,
        [property: JsonPropertyName("publicKey")] string PublicKey,
        [property: JsonPropertyName("agreementPublicKey")] string AgreementPublicKey,
        [property: JsonPropertyName("displayName")] string DisplayName,
        [property: JsonPropertyName("capability")] string Capability,
        [property: JsonPropertyName("issuedAt")] long IssuedAt,
        [property: JsonPropertyName("expiresAt")] long ExpiresAt);

    private sealed record AcceptancePayload(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("inviteHash")] string InviteHash,
        [property: JsonPropertyName("inviterPeerId")] string InviterPeerId,
        [property: JsonPropertyName("peerId")] string PeerId,
        [property: JsonPropertyName("publicKey")] string PublicKey,
        [property: JsonPropertyName("agreementPublicKey")] string AgreementPublicKey,
        [property: JsonPropertyName("displayName")] string DisplayName,
        [property: JsonPropertyName("issuedAt")] long IssuedAt,
        [property: JsonPropertyName("expiresAt")] long ExpiresAt);
}

internal sealed class MeshPairwiseContext : IDisposable
{
    public MeshPairwiseContext(MeshPublicIdentity localIdentity, MeshFriend remoteFriend, byte[] key)
    {
        LocalIdentity = localIdentity;
        RemoteFriend = remoteFriend;
        Key = key;
    }

    public MeshPublicIdentity LocalIdentity { get; }
    public MeshFriend RemoteFriend { get; }
    public byte[] Key { get; }

    public void Dispose() => CryptographicOperations.ZeroMemory(Key);
}
