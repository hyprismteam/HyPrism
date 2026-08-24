// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Models;
using HyPrism.Core.Infrastructure;
using HyPrism.Core.Accounts;
using System.Text.Json;

namespace HyPrism.Core.Tests.Accounts.Authentication;

public sealed class HytaleAuthenticatorTests
{
    [Fact]
    public void SessionJson_WritesPascalCaseAndReadsLegacySnakeCase()
    {
        var session = new HytaleAuthSession
        {
            AccessToken = "access",
            RefreshToken = "refresh",
            ExpiresAt = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc),
            SessionToken = "session",
            IdentityToken = "identity",
            Username = "Player",
            UUID = "550e8400-e29b-41d4-a716-446655440000",
            AccountOwnerId = "owner"
        };

        var json = JsonSerializer.Serialize(session);
        using var document = JsonDocument.Parse(json);
        Assert.All(
            document.RootElement.EnumerateObject(),
            property => Assert.True(char.IsUpper(property.Name[0]), property.Name));

        var restored = JsonSerializer.Deserialize<HytaleAuthSession>("""
            {
              "access_token": "legacy-access",
              "refresh_token": "legacy-refresh",
              "expires_at": "2026-08-24T12:00:00Z",
              "session_token": "legacy-session",
              "identity_token": "legacy-identity",
              "username": "LegacyPlayer",
              "uuid": "660e8400-e29b-41d4-a716-446655440000",
              "account_owner_id": "legacy-owner"
            }
            """);

        Assert.NotNull(restored);
        Assert.Equal("legacy-access", restored.AccessToken);
        Assert.Equal("legacy-refresh", restored.RefreshToken);
        Assert.Equal("legacy-session", restored.SessionToken);
        Assert.Equal("legacy-identity", restored.IdentityToken);
        Assert.Equal("LegacyPlayer", restored.Username);
        Assert.Equal("660e8400-e29b-41d4-a716-446655440000", restored.UUID);
        Assert.Equal("legacy-owner", restored.AccountOwnerId);
    }

    [Fact]
    public async Task LoginAsync_PresentsGeneratedAuthorizationUriThroughHostCallback()
    {
        var appDir = Path.Combine(Path.GetTempPath(), $"hyprism-auth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDir);

        try
        {
            var config = new Mock<IConfigStore>();
            config.SetupGet(service => service.Configuration).Returns(new Config());
            using var httpClient = new HttpClient();
            var auth = new HytaleAuthenticator(httpClient, appDir, config.Object);
            Uri? presentedUri = null;

            var session = await auth.LoginAsync((uri, _) =>
            {
                presentedUri = uri;
                return Task.FromResult(false);
            });

            Assert.Null(session);
            Assert.NotNull(presentedUri);
            Assert.Equal(Uri.UriSchemeHttps, presentedUri.Scheme);
            Assert.Equal("oauth.accounts.hytale.com", presentedUri.Host);
            Assert.Contains("code_challenge=", presentedUri.Query, StringComparison.Ordinal);
            Assert.Contains("state=", presentedUri.Query, StringComparison.Ordinal);
            Assert.Contains(
                "&scope=openid%20offline%20auth%3Alauncher&",
                presentedUri.AbsoluteUri,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoginAsync_ServesOfficialCallbackBeforePresentingAuthorizationUri()
    {
        var appDir = Path.Combine(Path.GetTempPath(), $"hyprism-auth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDir);

        try
        {
            var config = new Mock<IConfigStore>();
            config.SetupGet(service => service.Configuration).Returns(new Config());
            using var httpClient = new HttpClient();
            var auth = new HytaleAuthenticator(httpClient, appDir, config.Object);

            var session = await auth.LoginAsync(async (uri, cancellationToken) =>
            {
                var state = GetQueryValue(uri, "state");
                using var stateDocument = JsonDocument.Parse(Convert.FromBase64String(state));
                var port = stateDocument.RootElement.GetProperty("port").GetString();
                var callbackState = stateDocument.RootElement.GetProperty("state").GetString();
                Assert.False(string.IsNullOrWhiteSpace(port));
                Assert.False(string.IsNullOrWhiteSpace(callbackState));

                using var callbackClient = new HttpClient();
                using var response = await callbackClient.GetAsync(
                    $"http://127.0.0.1:{port}/authorization-callback" +
                    $"?error=access_denied&state={Uri.EscapeDataString(callbackState)}",
                    cancellationToken);

                Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
                return true;
            });

            Assert.Null(session);
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoginAsync_UsesHostRendererForSuccessfulCallbackPage()
    {
        var appDir = Path.Combine(Path.GetTempPath(), $"hyprism-auth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDir);

        try
        {
            var config = new Mock<IConfigStore>();
            config.SetupGet(service => service.Configuration).Returns(new Config());
            var renderer = new Mock<IOAuthCallbackPageRenderer>();
            renderer
                .Setup(value => value.Render(true, It.IsAny<string>()))
                .Returns("<!doctype html><html><body>branded-success-page</body></html>");
            using var httpClient = new HttpClient();
            var auth = new HytaleAuthenticator(httpClient, appDir, config.Object, renderer.Object);

            var session = await auth.LoginAsync(async (uri, cancellationToken) =>
            {
                var state = GetQueryValue(uri, "state");
                using var stateDocument = JsonDocument.Parse(Convert.FromBase64String(state));
                var port = stateDocument.RootElement.GetProperty("port").GetString();
                var callbackState = stateDocument.RootElement.GetProperty("state").GetString();
                Assert.False(string.IsNullOrWhiteSpace(port));
                Assert.False(string.IsNullOrWhiteSpace(callbackState));

                using var callbackClient = new HttpClient();
                using var response = await callbackClient.GetAsync(
                    $"http://127.0.0.1:{port}/authorization-callback" +
                    $"?code=test-code&state={Uri.EscapeDataString(callbackState!)}",
                    cancellationToken);
                var responseHtml = await response.Content.ReadAsStringAsync(cancellationToken);

                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("branded-success-page", responseHtml, StringComparison.Ordinal);
                return false;
            });

            Assert.Null(session);
            renderer.Verify(
                value => value.Render(
                    true,
                    "Authorization completed successfully"),
                Times.Once);
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoginAsync_RejectsMissingHostCallback()
    {
        var appDir = Path.Combine(Path.GetTempPath(), $"hyprism-auth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDir);

        try
        {
            var config = new Mock<IConfigStore>();
            config.SetupGet(service => service.Configuration).Returns(new Config());
            using var httpClient = new HttpClient();
            var auth = new HytaleAuthenticator(httpClient, appDir, config.Object);

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => auth.LoginAsync(null!));
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    private static string GetQueryValue(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var encodedName = separator >= 0 ? pair[..separator] : pair;
            if (!string.Equals(Uri.UnescapeDataString(encodedName), name, StringComparison.Ordinal))
                continue;

            var encodedValue = separator >= 0 ? pair[(separator + 1)..] : string.Empty;
            return Uri.UnescapeDataString(encodedValue.Replace("+", " ", StringComparison.Ordinal));
        }

        throw new InvalidOperationException($"Query parameter '{name}' was not found");
    }
}
