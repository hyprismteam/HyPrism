using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.Game.Auth;

/// <summary>
/// Handles authentication with the custom Hytale auth server.
/// Manages session creation, token retrieval, and JWT handling.
/// </summary>
/// <remarks>
/// Supports both the /game-session/child and /game-session endpoints
/// for backwards compatibility with different auth server versions.
/// </remarks>
public class AuthService : IAuthService
{
    private readonly HttpClient _httpClient;
    private readonly string[] _authServerUrls;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthService"/> class.
    /// Normalizes auth domain and builds fallback candidates.
    /// </summary>
    /// <param name="httpClient">The HTTP client for making auth requests.</param>
    /// <param name="authDomain">The auth server domain (e.g., "auth.example.com" or "sessions.sanasol.ws").</param>
    public AuthService(HttpClient httpClient, string authDomain)
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

        return candidates
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Creates a game session and retrieves authentication tokens.
    /// </summary>
    /// <param name="uuid">The player's unique identifier.</param>
    /// <param name="playerName">The player's display name.</param>
    /// <returns>An <see cref="AuthTokenResult"/> containing the session tokens or error information.</returns>
    public async Task<AuthTokenResult> GetGameSessionTokenAsync(string uuid, string playerName)
    {
        try
        {
            Logger.Info("Auth", $"Requesting game session for {playerName} ({uuid})...");

            var requestBody = new GameSessionRequest
            {
                UUID = uuid,
                Name = playerName,
                Scopes = new[] { "hytale:client", "hytale:server" }  // Request both client and server scopes
            };

            var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

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

                        var result = JsonSerializer.Deserialize<GameSessionResponse>(responseBody, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                            PropertyNameCaseInsensitive = true
                        });

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
    /// Requests an offline mode token from the auth server.
    /// Used when the game client requires HYTALE_OFFLINE_TOKEN for offline/singleplayer mode.
    /// </summary>
    /// <param name="uuid">The player's unique identifier.</param>
    /// <param name="playerName">The player's display name.</param>
    /// <returns>The offline token string, or null if unavailable.</returns>
    public async Task<string?> GetOfflineTokenAsync(string uuid, string playerName, CancellationToken ct = default)
    {
        try
        {
            Logger.Info("Auth", $"Requesting offline token for {playerName} ({uuid})...");

            var requestBody = new GameSessionRequest
            {
                UUID = uuid,
                Name = playerName,
                Scopes = new[] { "hytale:offline", "hytale:client" }
            };

            var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Use /auth endpoint — lightweight token generation without session creation
            foreach (var authServerUrl in _authServerUrls)
            {
                ct.ThrowIfCancellationRequested();
                var requestUrl = $"{authServerUrl}/auth";
                try
                {
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(requestUrl, content, ct);
                    var responseBody = await response.Content.ReadAsStringAsync(ct);

                    if (!response.IsSuccessStatusCode)
                        continue;

                    var result = JsonSerializer.Deserialize<GameSessionResponse>(responseBody, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        PropertyNameCaseInsensitive = true
                    });

                    string? token = result?.IdentityToken ?? result?.IdentityTokenAlt ?? result?.Token ?? result?.AccessToken;
                    if (string.IsNullOrEmpty(token) && responseBody.StartsWith("eyJ"))
                        token = responseBody.Trim().Trim('"');

                    if (!string.IsNullOrEmpty(token))
                    {
                        Logger.Success("Auth", "Offline token obtained successfully");
                        return token;
                    }
                }
                catch (HttpRequestException)
                {
                    break;
                }
            }

            Logger.Warning("Auth", "Could not obtain offline token from any endpoint");
            return null;
        }
        catch (OperationCanceledException)
        {
            throw; // Let caller handle timeout
        }
        catch (Exception ex)
        {
            Logger.Warning("Auth", $"Error fetching offline token: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Validate an existing token is still valid
    /// </summary>
    public async Task<bool> ValidateTokenAsync(string token)
    {
        foreach (var authServerUrl in _authServerUrls)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{authServerUrl}/validate");
                request.Headers.Add("Authorization", $"Bearer {token}");

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                    return true;
            }
            catch
            {
                // Try next candidate
            }
        }

        return false;
    }
}

public class GameSessionRequest
{
    [JsonPropertyName("uuid")]
    public string UUID { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("scopes")]
    public string[] Scopes { get; set; } = Array.Empty<string>();
}

public class GameSessionResponse
{
    // Primary token field (from /game-session/child endpoint)
    [JsonPropertyName("identityToken")]
    public string? IdentityToken { get; set; }

    // Snake_case variant (from /auth endpoint)
    [JsonPropertyName("identity_token")]
    public string? IdentityTokenAlt { get; set; }

    // Alternative token fields for compatibility
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("jwt_token")]
    public string? JwtToken { get; set; }

    [JsonPropertyName("sessionToken")]
    public string? SessionToken { get; set; }

    [JsonPropertyName("session_token")]
    public string? SessionTokenAlt { get; set; }

    [JsonPropertyName("uuid")]
    public string? UUID { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("profile")]
    public GameSessionProfile? Profile { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    [JsonPropertyName("expiresIn")]
    public int? ExpiresIn { get; set; }

    [JsonPropertyName("tokenType")]
    public string? TokenType { get; set; }
}

public class GameSessionProfile
{
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class AuthTokenResult
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public string? SessionToken { get; set; }
    public string? UUID { get; set; }
    public string? Name { get; set; }
    public string? Error { get; set; }
}
