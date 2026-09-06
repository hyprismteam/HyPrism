// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Net.Http;
using System.Text;
using HyPrism.Core.Game.Authentication;

namespace HyPrism.Core.Tests.Game.Authentication;

/// <summary>
/// Tests for <see cref="GameSessionAuthenticator"/> using a stubbed <see cref="HttpMessageHandler"/>
/// so no real network connections are made.
/// </summary>
public class GameSessionAuthenticatorTests
{

    private static HttpClient BuildClient(HttpStatusCode status, string body)
    {
        var handler = new StubHttpHandler(status, body);
        return new HttpClient(handler);
    }


    [Fact]
    public async Task GetGameSessionTokenAsync_SuccessResponse_ReturnsToken()
    {
        const string responseJson = """
            {
              "identityToken": "eyJhbGciOiJSUzI1NiJ9.test.sig",
              "uuid": "some-uuid",
              "name": "TestPlayer"
            }
            """;

        var svc = new GameSessionAuthenticator(BuildClient(HttpStatusCode.OK, responseJson), "auth.example.com");

        var result = await svc.GetGameSessionTokenAsync("some-uuid", "TestPlayer");

        Assert.True(result.Success);
        Assert.Equal("eyJhbGciOiJSUzI1NiJ9.test.sig", result.Token);
        Assert.Equal("TestPlayer", result.Name);
    }

    [Fact]
    public async Task GetGameSessionTokenAsync_ServerError_ReturnsFailure()
    {
        var svc = new GameSessionAuthenticator(BuildClient(HttpStatusCode.InternalServerError, "error"), "auth.example.com");

        var result = await svc.GetGameSessionTokenAsync("uuid", "Player");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Error ?? "");
    }

    [Fact]
    public async Task GetGameSessionTokenAsync_RawJwtResponse_ExtractsToken()
    {
        // GameSessionAuthenticator extracts the token from the identityToken field.
        // A raw (non-JSON) JWT body causes JsonException before the StartsWith("eyJ")
        // fallback is reached, so the token must be wrapped in a JSON envelope.
        const string rawJwt = "eyJhbGciOiJSUzI1NiJ9.payload.signature";
        var jsonBody = $"{{\"identityToken\":\"{rawJwt}\"}}";
        var svc = new GameSessionAuthenticator(BuildClient(HttpStatusCode.OK, jsonBody), "auth.example.com");

        var result = await svc.GetGameSessionTokenAsync("uuid", "Player");

        Assert.True(result.Success);
        Assert.Equal(rawJwt, result.Token);
    }

    [Fact]
    public async Task GetGameSessionTokenAsync_EmptyAuthDomain_UsesDefaultServer()
    {
        const string responseJson = """{"identityToken":"tok","uuid":"u","name":"n"}""";
        var svc = new GameSessionAuthenticator(BuildClient(HttpStatusCode.OK, responseJson), "");

        var result = await svc.GetGameSessionTokenAsync("u", "n");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task GetGameSessionTokenAsync_NetworkError_ReturnsFailure()
    {
        var handler = new ThrowingHttpHandler();
        var svc = new GameSessionAuthenticator(new HttpClient(handler), "auth.example.com");

        var result = await svc.GetGameSessionTokenAsync("uuid", "Player");

        Assert.False(result.Success);
    }


    [Fact]
    public async Task ValidateTokenAsync_ServerReturnsOk_ReturnsTrue()
    {
        var svc = new GameSessionAuthenticator(BuildClient(HttpStatusCode.OK, ""), "auth.example.com");

        var valid = await svc.ValidateTokenAsync("some-token");

        Assert.True(valid);
    }

    [Fact]
    public async Task ValidateTokenAsync_ServerReturnsUnauthorized_ReturnsFalse()
    {
        var svc = new GameSessionAuthenticator(BuildClient(HttpStatusCode.Unauthorized, ""), "auth.example.com");

        var valid = await svc.ValidateTokenAsync("expired-token");

        Assert.False(valid);
    }


    private sealed class StubHttpHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Simulated network failure");
    }
}
