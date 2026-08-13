// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Concurrent;
using System.Text.Json;
using HyPrism.Core.Game.Authentication;

namespace HyPrism.LocalNode;

/// <summary>
/// Keeps ephemeral OmniAuth signing keys in memory for active local profiles
/// </summary>
public sealed class LocalSessionRegistry
{
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _issuer;

    /// <summary>
    /// Creates a registry for one canonical Local Node issuer
    /// </summary>
    public LocalSessionRegistry(string issuer)
        => _issuer = issuer;

    /// <summary>
    /// Creates a fresh session and replaces any earlier key for the profile
    /// </summary>
    public OmniAuthSession Renew(
        string playerUuid,
        string playerName,
        string identityScope = "hytale:server hytale:client",
        string[]? entitlements = null,
        string? skin = null)
    {
        var issuer = new OmniAuthSessionIssuer(playerUuid, playerName, _issuer);
        var session = issuer.CreateSession(identityScope, entitlements, skin);
        _sessions[playerUuid] = new SessionState(issuer, session);
        return session;
    }

    /// <summary>
    /// Returns the current valid session or creates one when the profile is not active
    /// </summary>
    public OmniAuthSession GetOrCreate(string playerUuid, string playerName)
    {
        if (_sessions.TryGetValue(playerUuid, out var existing)
            && existing.Session.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)
            && string.Equals(existing.Issuer.PlayerName, playerName, StringComparison.Ordinal))
        {
            return existing.Session;
        }

        return Renew(playerUuid, playerName);
    }

    /// <summary>
    /// Issues fresh tokens with the existing session key after validating a caller token
    /// </summary>
    public OmniAuthSession? RenewByToken(
        string token,
        string identityScope = "hytale:server hytale:client",
        string[]? entitlements = null,
        string? skin = null)
    {
        var state = FindByToken(token);
        if (state is null || !state.Issuer.TryValidateToken(token, out var claims))
            return null;

        skin ??= ReadStringClaim(claims, "skin");
        var renewed = state.Issuer.CreateSession(identityScope, entitlements, skin);
        _sessions[state.Issuer.PlayerUuid] = new SessionState(state.Issuer, renewed);
        return renewed;
    }

    /// <summary>
    /// Issues an authorization grant after validating the supplied identity token
    /// </summary>
    public string? CreateAuthorizationGrant(
        string identityToken,
        string audience,
        string? scope,
        string? skin = null)
    {
        var state = FindByToken(identityToken);
        if (state is null || !state.Issuer.TryValidateToken(identityToken, out var claims))
            return null;

        skin ??= ReadStringClaim(claims, "skin");
        return state.Issuer.CreateAuthorizationGrant(audience, scope, skin);
    }

    /// <summary>
    /// Exchanges a local authorization grant for a certificate-bound access token
    /// </summary>
    public string? ExchangeAuthorizationGrant(
        string authorizationGrant,
        string? certificateFingerprint,
        string? requestedScope,
        out string? scope,
        out string? refreshToken)
    {
        scope = null;
        refreshToken = null;
        var state = FindByToken(authorizationGrant);
        if (state is null || !state.Issuer.TryValidateToken(authorizationGrant, out var claims))
            return null;

        var audience = ReadAudience(claims);
        if (string.IsNullOrWhiteSpace(audience))
            return null;

        scope = !string.IsNullOrWhiteSpace(requestedScope)
            ? requestedScope
            : claims.TryGetProperty("scope", out var scopeClaim) ? scopeClaim.GetString() : null;
        refreshToken = state.Session.SessionToken;
        var skin = ReadStringClaim(claims, "skin");
        return state.Issuer.CreateAccessToken(audience, certificateFingerprint, scope, skin);
    }

    /// <summary>
    /// Validates a bearer token and returns its claims
    /// </summary>
    public bool TryValidate(string token, out JsonElement claims)
    {
        claims = default;
        var state = FindByToken(token);
        return state is not null && state.Issuer.TryValidateToken(token, out claims);
    }

    /// <summary>
    /// Removes an active session identified by one of its tokens
    /// </summary>
    public void RemoveByToken(string token)
    {
        if (TryReadTokenIdentity(token, out var playerUuid, out _)
            && _sessions.TryGetValue(playerUuid, out var state)
            && state.Issuer.TryValidateToken(token, out _))
        {
            _sessions.TryRemove(playerUuid, out _);
        }
    }

    /// <summary>
    /// Gets public keys for all active session issuers
    /// </summary>
    public IReadOnlyList<OmniAuthPublicJwk> GetPublicKeys()
        => _sessions.Values
            .Where(state => state.Session.ExpiresAt > DateTimeOffset.UtcNow)
            .Select(state => state.Issuer.PublicJwk)
            .DistinctBy(key => key.Kid)
            .ToArray();

    private SessionState? FindByToken(string token)
    {
        if (!TryReadTokenIdentity(token, out var playerUuid, out var keyId))
            return null;
        if (!_sessions.TryGetValue(playerUuid, out var state))
            return null;
        return state.Issuer.KeyId == keyId ? state : null;
    }

    private static bool TryReadTokenIdentity(string token, out string playerUuid, out string keyId)
    {
        playerUuid = string.Empty;
        keyId = string.Empty;
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                return false;

            using var header = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            using var claims = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            keyId = header.RootElement.GetProperty("kid").GetString() ?? string.Empty;
            playerUuid = claims.RootElement.GetProperty("sub").GetString() ?? string.Empty;
            return playerUuid.Length > 0 && keyId.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadAudience(JsonElement claims)
    {
        if (!claims.TryGetProperty("aud", out var audienceClaim))
            return null;
        if (audienceClaim.ValueKind == JsonValueKind.String)
            return audienceClaim.GetString();
        if (audienceClaim.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in audienceClaim.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                return item.GetString();
        }
        return null;
    }

    private static string? ReadStringClaim(JsonElement claims, string name)
        => claims.TryGetProperty(name, out var claim) && claim.ValueKind == JsonValueKind.String
            ? claim.GetString()
            : null;

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    private sealed record SessionState(OmniAuthSessionIssuer Issuer, OmniAuthSession Session);
}
