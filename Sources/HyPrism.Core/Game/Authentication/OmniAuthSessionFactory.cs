// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace HyPrism.Core.Game.Authentication;

/// <summary>
/// Creates self-contained OmniAuth sessions for autonomous launches
/// </summary>
public static class OmniAuthSessionFactory
{
    private const string DefaultIssuer = "http://127.0.0.1:12345";

    /// <summary>
    /// Creates identity and session tokens signed by a new ephemeral Ed25519 key
    /// </summary>
    public static OmniAuthSession Create(string playerUuid, string playerName)
        => Create(playerUuid, playerName, DefaultIssuer);

    /// <summary>
    /// Creates identity and session tokens signed by a new ephemeral Ed25519 key for an explicit issuer
    /// </summary>
    public static OmniAuthSession Create(string playerUuid, string playerName, string issuer)
        => new OmniAuthSessionIssuer(playerUuid, playerName, issuer).CreateSession();
}

/// <summary>
/// Owns the ephemeral Ed25519 key used throughout one OmniAuth game session
/// </summary>
public sealed class OmniAuthSessionIssuer
{
    private const string ServerAudience = "hytale-server";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(10);

    private readonly Ed25519PrivateKeyParameters _privateKey;
    private readonly Ed25519PublicKeyParameters _publicKey;
    private readonly string _playerUuid;
    private readonly string _playerName;
    private readonly string _issuer;

    /// <summary>
    /// Creates a session issuer with a new in-memory key pair
    /// </summary>
    public OmniAuthSessionIssuer(string playerUuid, string playerName, string issuer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerUuid);
        ArgumentException.ThrowIfNullOrWhiteSpace(playerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);

        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri))
        {
            throw new ArgumentException("The OmniAuth issuer must be an absolute URI", nameof(issuer));
        }

        _playerUuid = playerUuid;
        _playerName = playerName;
        _issuer = issuerUri.GetLeftPart(UriPartial.Authority);
        _privateKey = new Ed25519PrivateKeyParameters(new SecureRandom());
        _publicKey = _privateKey.GeneratePublicKey();
        KeyId = Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Gets the key identifier shared by tokens from this session
    /// </summary>
    public string KeyId { get; }

    /// <summary>
    /// Gets the normalized issuer URL
    /// </summary>
    public string Issuer => _issuer;

    /// <summary>
    /// Gets the stable local profile UUID
    /// </summary>
    public string PlayerUuid => _playerUuid;

    /// <summary>
    /// Gets the local profile display name
    /// </summary>
    public string PlayerName => _playerName;

    /// <summary>
    /// Gets the public key representation exposed by the local JWKS endpoint
    /// </summary>
    public OmniAuthPublicJwk PublicJwk => new(
        "OKP",
        "Ed25519",
        Base64UrlEncode(_publicKey.GetEncoded()),
        "sig",
        "EdDSA",
        KeyId);

    /// <summary>
    /// Issues the initial identity and session tokens
    /// </summary>
    public OmniAuthSession CreateSession(
        string identityScope = "hytale:server hytale:client",
        string[]? entitlements = null,
        string? skin = null)
    {
        entitlements ??= ["game.base"];
        var identityClaims = new Dictionary<string, object>
        {
            ["sub"] = _playerUuid,
            ["name"] = _playerName,
            ["username"] = _playerName,
            ["profile"] = new Dictionary<string, object> { ["username"] = _playerName },
            ["entitlements"] = entitlements,
            ["scope"] = identityScope,
            ["aud"] = ServerAudience
        };
        AddSkinClaim(identityClaims, skin);

        var sessionClaims = new Dictionary<string, object>
        {
            ["sub"] = _playerUuid,
            ["name"] = _playerName,
            ["scope"] = "hytale:server",
            ["aud"] = ServerAudience
        };

        var expiresAt = DateTimeOffset.UtcNow.Add(SessionLifetime);
        return new OmniAuthSession(
            SignToken(identityClaims, expiresAt),
            SignToken(sessionClaims, expiresAt, includePrivateKey: false),
            expiresAt);
    }

    /// <summary>
    /// Issues a server-scoped authorization grant
    /// </summary>
    public string CreateAuthorizationGrant(
        string audience,
        string? scope = null,
        string? skin = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        var claims = new Dictionary<string, object>
        {
            ["sub"] = _playerUuid,
            ["name"] = _playerName,
            ["username"] = _playerName,
            ["aud"] = audience,
            ["scope"] = NormalizeScope(scope)
        };
        AddSkinClaim(claims, skin);
        return SignToken(claims, includePrivateKey: false);
    }

    /// <summary>
    /// Issues the final server access token with optional certificate binding
    /// </summary>
    public string CreateAccessToken(
        string audience,
        string? certificateFingerprint,
        string? scope = null,
        string? skin = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        var claims = new Dictionary<string, object>
        {
            ["sub"] = _playerUuid,
            ["name"] = _playerName,
            ["username"] = _playerName,
            ["aud"] = audience,
            ["scope"] = NormalizeScope(scope),
            ["entitlements"] = new[] { "game.base" }
        };
        AddSkinClaim(claims, skin);

        if (!string.IsNullOrWhiteSpace(certificateFingerprint))
        {
            claims["cnf"] = new Dictionary<string, string>
            {
                ["x5t#S256"] = certificateFingerprint
            };
        }

        return SignToken(claims, includePrivateKey: false);
    }

    private static void AddSkinClaim(Dictionary<string, object> claims, string? skin)
    {
        if (!string.IsNullOrWhiteSpace(skin))
            claims["skin"] = skin;
    }

    /// <summary>
    /// Validates a token created by this session issuer
    /// </summary>
    public bool TryValidateToken(string token, out JsonElement claims)
    {
        claims = default;
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                return false;

            using var headerDocument = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            var header = headerDocument.RootElement;
            if (!header.TryGetProperty("alg", out var algorithm) || algorithm.GetString() != "EdDSA")
                return false;
            if (!header.TryGetProperty("kid", out var keyId) || keyId.GetString() != KeyId)
                return false;

            var verifier = new Ed25519Signer();
            verifier.Init(false, _publicKey);
            var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            verifier.BlockUpdate(signingInput, 0, signingInput.Length);
            if (!verifier.VerifySignature(Base64UrlDecode(parts[2])))
                return false;

            using var claimsDocument = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            var root = claimsDocument.RootElement;
            if (!root.TryGetProperty("iss", out var tokenIssuer) || tokenIssuer.GetString() != _issuer)
                return false;
            if (!root.TryGetProperty("sub", out var subject) || subject.GetString() != _playerUuid)
                return false;
            if (!root.TryGetProperty("exp", out var expiresAt) || expiresAt.GetInt64() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                return false;

            claims = root.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string SignToken(
        IReadOnlyDictionary<string, object> sessionClaims,
        DateTimeOffset? explicitExpiration = null,
        bool includePrivateKey = true)
    {
        var now = DateTimeOffset.UtcNow;
        var claims = new Dictionary<string, object>(sessionClaims)
        {
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = (explicitExpiration ?? now.Add(SessionLifetime)).ToUnixTimeSeconds(),
            ["iss"] = _issuer,
            ["jti"] = Guid.NewGuid().ToString(),
            ["omni"] = true
        };

        var jwk = new Dictionary<string, object>
        {
            ["kty"] = "OKP",
            ["crv"] = "Ed25519",
            ["x"] = Base64UrlEncode(_publicKey.GetEncoded()),
            ["use"] = "sig",
            ["alg"] = "EdDSA",
            ["kid"] = KeyId
        };
        if (includePrivateKey)
            jwk["d"] = Base64UrlEncode(_privateKey.GetEncoded());
        var header = new Dictionary<string, object>
        {
            ["alg"] = "EdDSA",
            ["typ"] = "JWT",
            ["kid"] = KeyId,
            ["jwk"] = jwk
        };

        var encodedHeader = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
        var encodedClaims = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(claims));
        var signingInput = Encoding.ASCII.GetBytes($"{encodedHeader}.{encodedClaims}");

        var signer = new Ed25519Signer();
        signer.Init(true, _privateKey);
        signer.BlockUpdate(signingInput, 0, signingInput.Length);
        return $"{encodedHeader}.{encodedClaims}.{Base64UrlEncode(signer.GenerateSignature())}";
    }

    private static string NormalizeScope(string? scope)
        => string.IsNullOrWhiteSpace(scope) ? "hytale:server hytale:client" : scope;

    private static string Base64UrlEncode(ReadOnlySpan<byte> value)
        => Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}

/// <summary>
/// Contains the locally generated tokens used for an OmniAuth launch
/// </summary>
public sealed record OmniAuthSession(
    string IdentityToken,
    string SessionToken,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Public Ed25519 key returned by the Local Node JWKS endpoint
/// </summary>
public sealed record OmniAuthPublicJwk(
    string Kty,
    string Crv,
    string X,
    string Use,
    string Alg,
    string Kid);
