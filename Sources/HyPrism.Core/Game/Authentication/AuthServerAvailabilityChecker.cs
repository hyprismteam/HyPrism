// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Text;
using System.Text.Json;

namespace HyPrism.Core.Game.Authentication;

public readonly record struct AuthServerAvailabilityResult(bool IsAvailable, long PingMs);

/// <summary>
/// Verifies that a URL exposes the custom Hytale authentication API
/// </summary>
public static class AuthServerAvailabilityChecker
{
    private static readonly string[] ProbeEndpoints = ["/game-session/child", "/game-session"];
    private static readonly string[] AuthTokenProperties =
    [
        "identityToken",
        "identity_token",
        "token",
        "accessToken",
        "jwt_token",
        "sessionToken",
        "session_token"
    ];
    private static readonly string[] AuthErrorProperties =
    [
        "error",
        "message"
    ];

    public static async Task<AuthServerAvailabilityResult> CheckAsync(
        HttpClient httpClient,
        string authServer,
        CancellationToken cancellationToken)
    {
        var candidates = BuildAuthServerCandidates(authServer);
        // An intentionally invalid payload verifies the API contract without creating a game session
        const string probePayload = "{}";

        foreach (var candidate in candidates)
        {
            foreach (var endpoint in ProbeEndpoints)
            {
                var started = System.Diagnostics.Stopwatch.GetTimestamp();

                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    using var request = new HttpRequestMessage(HttpMethod.Post, $"{candidate}{endpoint}")
                    {
                        Content = new StringContent(probePayload, Encoding.UTF8, "application/json")
                    };
                    using var response = await httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeout.Token);
                    var responseBody = await response.Content.ReadAsStringAsync(timeout.Token);

                    if (IsAuthApiResponse(response, responseBody))
                    {
                        return new(true, (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (HttpRequestException)
                {
                    break;
                }
            }
        }

        return new(false, -1);
    }

    private static bool IsAuthApiResponse(HttpResponseMessage response, string responseBody)
    {
        if (response.StatusCode is not (HttpStatusCode.OK
            or HttpStatusCode.BadRequest
            or HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden
            or HttpStatusCode.UnprocessableEntity))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(responseBody))
            return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
                return false;

            var properties = document.RootElement.EnumerateObject().Select(property => property.Name);
            var expectedProperties = response.StatusCode == HttpStatusCode.OK
                ? AuthTokenProperties
                : AuthErrorProperties;
            return properties.Any(property =>
                expectedProperties.Contains(property, StringComparer.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string[] BuildAuthServerCandidates(string authServer)
    {
        var value = (authServer ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(value))
            return ["https://sessions.sanasol.ws"];

        var hasScheme = value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        var primary = hasScheme ? value : $"https://{value}";
        var candidates = new List<string> { primary.TrimEnd('/') };

        if (Uri.TryCreate(primary, UriKind.Absolute, out var primaryUri)
            && !primaryUri.Host.StartsWith("sessions.", StringComparison.OrdinalIgnoreCase))
        {
            var fallbackBuilder = new UriBuilder(primaryUri)
            {
                Host = $"sessions.{primaryUri.Host}"
            };
            candidates.Add(fallbackBuilder.Uri.ToString().TrimEnd('/'));
        }

        return [.. candidates.Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
