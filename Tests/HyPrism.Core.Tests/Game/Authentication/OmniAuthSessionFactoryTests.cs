// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using System.Text.Json;
using HyPrism.Core.Game.Authentication;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace HyPrism.Core.Tests.Game.Authentication;

public sealed class OmniAuthSessionFactoryTests
{
    [Fact]
    public void Create_ProducesSignedIdentityAndSessionTokens()
    {
        var session = OmniAuthSessionFactory.Create(
            "550e8400-e29b-41d4-a716-446655440000",
            "TestPlayer");

        AssertToken(session.IdentityToken, "TestPlayer", "hytale:server hytale:client");
        AssertToken(session.SessionToken, null, "hytale:server");
        Assert.True(session.ExpiresAt > DateTimeOffset.UtcNow.AddHours(9));
    }

    [Fact]
    public void Create_WithExplicitIssuer_UsesNormalizedIssuerAndServerAudience()
    {
        var session = OmniAuthSessionFactory.Create(
            "550e8400-e29b-41d4-a716-446655440000",
            "TestPlayer",
            "https://127.0.0.1:8443/some-path");

        AssertToken(
            session.IdentityToken,
            "TestPlayer",
            "hytale:server hytale:client",
            "https://127.0.0.1:8443");
        AssertToken(session.SessionToken, null, "hytale:server", "https://127.0.0.1:8443");
    }

    [Theory]
    [InlineData("", "Player")]
    [InlineData("uuid", "")]
    public void Create_EmptyIdentityPart_ThrowsArgumentException(string uuid, string name)
    {
        Assert.Throws<ArgumentException>(() => OmniAuthSessionFactory.Create(uuid, name));
    }

    private static void AssertToken(
        string token,
        string? expectedName,
        string expectedScope,
        string expectedIssuer = "http://127.0.0.1:12345")
    {
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);

        using var header = JsonDocument.Parse(Base64UrlDecode(parts[0]));
        using var claims = JsonDocument.Parse(Base64UrlDecode(parts[1]));

        var jwk = header.RootElement.GetProperty("jwk");
        var publicKey = new Ed25519PublicKeyParameters(
            Base64UrlDecode(jwk.GetProperty("x").GetString()!),
            0);
        var verifier = new Ed25519Signer();
        verifier.Init(false, publicKey);

        var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        verifier.BlockUpdate(signingInput, 0, signingInput.Length);

        Assert.True(verifier.VerifySignature(Base64UrlDecode(parts[2])));
        Assert.Equal(expectedIssuer, claims.RootElement.GetProperty("iss").GetString());
        Assert.Equal("550e8400-e29b-41d4-a716-446655440000", claims.RootElement.GetProperty("sub").GetString());
        Assert.Equal(expectedScope, claims.RootElement.GetProperty("scope").GetString());
        Assert.Equal("hytale-server", claims.RootElement.GetProperty("aud").GetString());
        Assert.True(claims.RootElement.GetProperty("omni").GetBoolean());

        if (expectedName is not null)
        {
            Assert.Equal(expectedName, claims.RootElement.GetProperty("username").GetString());
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}
