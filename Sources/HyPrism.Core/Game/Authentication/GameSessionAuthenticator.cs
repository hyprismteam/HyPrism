// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HyPrism.Core.Infrastructure;

namespace HyPrism.Core.Game.Authentication;

/// <summary>
/// Handles authentication with the custom Hytale auth server.
/// Manages session creation, token retrieval, and JWT handling
/// </summary>
/// <remarks>
/// Supports both the /game-session/child and /game-session endpoints
/// for backwards compatibility with different auth server versions
/// </remarks>
public class GameSessionAuthenticator
{
    private readonly HttpClient _httpClient;
    private readonly string[] _authServerUrls;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameSessionAuthenticator"/> class.
    /// Normalizes auth domain and builds fallback candidates
    /// </summary>
    /// <param name="httpClient">The HTTP client for making auth requests</param>
    /// <param name="authDomain">The auth server domain (e.g., "auth.example.com" or "sessions.sanasol.ws")</param>
    public GameSessionAuthenticator(HttpClient httpClient, string authDomain)
    {
        _httpClient = httpClient;
        _authServerUrls = BuildAuthServerCandidates(authDomain);
        Logger.Info("Auth", $"Auth server candidates: {string.Join(", ", _authServerUrls)}");
    }

    private static string[] BuildAuthServerCandidates(string authDomain)
    {
        var value = (authDomain ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(value))
        {
            return ["https://sessions.sanasol.ws"];
        }

        var candidates = new List<string>();
        var hasScheme = value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        var primary = hasScheme ? value : $"https://{value}";
        candidates.Add(primary.TrimEnd('/'));

        // Compatibility fallback: if user entered non-sessions host, also try sessions.<host>
        if (Uri.TryCreate(primary, UriKind.Absolute, out var primaryUri)
            && !primaryUri.Host.StartsWith("sessions.", StringComparison.OrdinalIgnoreCase))
        {
            var fallbackBuilder = new UriBuilder(primaryUri)
            {
                Host = $"sessions.{primaryUri.Host}"
            };
            candidates.Add(fallbackBuilder.Uri.ToString().TrimEnd('/'));
        }

        return [.. candidates
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Creates a game session and retrieves authentication tokens
    /// </summary>
    /// <param name="uuid">The player's unique identifier</param>
    /// <param name="playerName">The player's display name</param>
    /// <returns>An <see cref="AuthTokenResult"/> containing the session tokens or error information</returns>
    public async Task<AuthTokenResult> GetGameSessionTokenAsync(string uuid, string playerName)
    {
        try
        {
            Logger.Info("Auth", $"Requesting game session for {playerName} ({uuid})...");

            var requestBody = new GameSessionRequest
            {
                UUID = uuid,
                Name = playerName,
                Scopes = ["hytale:client", "hytale:server"]
            };

            var json = JsonSerializer.Serialize(requestBody, JsonDefaults.CamelCase);

            string[] endpoints = ["/game-session/child", "/game-session"];
            string? lastError = null;

            foreach (var authServerUrl in _authServerUrls)
            {
                Logger.Info("Auth", $"Trying auth server: {authServerUrl}");

                foreach (var endpoint in endpoints)
                {
                    var requestUrl = $"{authServerUrl}{endpoint}";
                    try
                    {
                        using var content = new StringContent(json, Encoding.UTF8, "application/json");
                        var response = await _httpClient.PostAsync(requestUrl, content);
                        var responseBody = await response.Content.ReadAsStringAsync();

                        if (!response.IsSuccessStatusCode)
                        {
                            lastError = $"Auth server returned {response.StatusCode}";
                            Logger.Warning("Auth", $"{requestUrl} -> {response.StatusCode}: {responseBody}");
                            continue;
                        }

                        Logger.Info("Auth", $"Auth response received from {requestUrl} ({responseBody.Length} chars)");

                        var result = JsonSerializer.Deserialize<GameSessionResponse>(
                            responseBody,
                            JsonDefaults.CamelCaseInsensitive);

                        if (result == null)
                        {
                            lastError = "Failed to parse auth response";
                            Logger.Warning("Auth", $"{requestUrl} -> failed to parse response");
                            continue;
                        }

                        string? token = result.IdentityToken ?? result.IdentityTokenAlt ?? result.Token ?? result.AccessToken ?? result.JwtToken ?? result.SessionToken ?? result.SessionTokenAlt;
                        if (string.IsNullOrEmpty(token) && responseBody.StartsWith("eyJ"))
                        {
                            token = responseBody.Trim().Trim('"');
                        }

                        if (string.IsNullOrEmpty(token))
                        {
                            lastError = "No token in response";
                            Logger.Warning("Auth", $"{requestUrl} -> no token in response");
                            continue;
                        }

                        Logger.Success("Auth", "Game session token obtained successfully");
                        return new AuthTokenResult
                        {
                            Success = true,
                            Token = token,
                            SessionToken = result.SessionToken ?? result.SessionTokenAlt ?? token,
                            UUID = result.UUID ?? uuid,
                            Name = result.Name ?? result.Username ?? result.Profile?.Username ?? result.Profile?.Name ?? playerName
                        };
                    }
                    catch (HttpRequestException ex)
                    {
                        lastError = $"Network error: {ex.Message}";
                        Logger.Warning("Auth", $"Network error for {requestUrl}: {ex.Message}");
                        break;
                    }
                }
            }

            Logger.Error("Auth", $"Auth failed on all endpoints: {lastError ?? "unknown error"}");
            return new AuthTokenResult
            {
                Success = false,
                Error = lastError ?? "Auth failed on all endpoints"
            };
        }
        catch (Exception ex)
        {
            Logger.Error("Auth", $"Auth error: {ex.Message}");
            return new AuthTokenResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Validate an existing token is still valid
    /// </summary>
    /// <returns>A task that completes with true when the operation succeeds; otherwise false</returns>
    public async Task<bool> ValidateTokenAsync(string token)
    {
        foreach (var authServerUrl in _authServerUrls)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{authServerUrl}/validate");
                request.Headers.Add("Authorization", $"Bearer {token}");

                using var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                    return true;
            }
            catch
            {
            }
        }

        return false;
    }
}

/// <summary>
/// Request body used to create a game session on a custom authentication server
/// </summary>
public class GameSessionRequest
{
    /// <summary>Player UUID sent to the authentication server</summary>
    [JsonPropertyName("uuid")]
    public string UUID { get; set; } = "";

    /// <summary>Player display name sent to the authentication server</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Scopes requested for the game session</summary>
    [JsonPropertyName("scopes")]
    public string[] Scopes { get; set; } = [];
}

/// <summary>
/// Response body returned by a custom authentication server
/// </summary>
public class GameSessionResponse
{
    /// <summary>Identity token using the camel-case response name</summary>
    [JsonPropertyName("identityToken")]
    public string? IdentityToken { get; set; }

    /// <summary>Identity token using the snake-case response name</summary>
    [JsonPropertyName("identity_token")]
    public string? IdentityTokenAlt { get; set; }

    /// <summary>Generic token returned by the server</summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    /// <summary>Access token returned by the server</summary>
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    /// <summary>JWT token returned by the server</summary>
    [JsonPropertyName("jwt_token")]
    public string? JwtToken { get; set; }

    /// <summary>Session token using the camel-case response name</summary>
    [JsonPropertyName("sessionToken")]
    public string? SessionToken { get; set; }

    /// <summary>Session token using the snake-case response name</summary>
    [JsonPropertyName("session_token")]
    public string? SessionTokenAlt { get; set; }

    /// <summary>Player UUID returned by the server</summary>
    [JsonPropertyName("uuid")]
    public string? UUID { get; set; }

    /// <summary>Player display name returned by the server</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Player username returned by the server</summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>Nested player profile returned by the server</summary>
    [JsonPropertyName("profile")]
    public GameSessionProfile? Profile { get; set; }

    /// <summary>UTC time when the returned session expires</summary>
    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Lifetime of the returned session in seconds</summary>
    [JsonPropertyName("expiresIn")]
    public int? ExpiresIn { get; set; }

    /// <summary>Token type declared by the server</summary>
    [JsonPropertyName("tokenType")]
    public string? TokenType { get; set; }
}

/// <summary>
/// Player profile nested in a custom authentication response
/// </summary>
public class GameSessionProfile
{
    /// <summary>Player username</summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>Player display name</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// Normalized result of a custom authentication request
/// </summary>
public class AuthTokenResult
{
    /// <summary>Whether the authentication request succeeded</summary>
    public bool Success { get; set; }
    /// <summary>Primary access or identity token, when available</summary>
    public string? Token { get; set; }
    /// <summary>Game session token, when available</summary>
    public string? SessionToken { get; set; }
    /// <summary>Player UUID returned by the authentication server</summary>
    public string? UUID { get; set; }
    /// <summary>Player display name returned by the authentication server</summary>
    public string? Name { get; set; }
    /// <summary>Error description when <see cref="Success"/> is <see langword="false"/></summary>
    public string? Error { get; set; }
}
