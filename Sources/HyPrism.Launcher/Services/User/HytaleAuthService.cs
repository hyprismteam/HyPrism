using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Core.Integration;
using HyPrism.Services.Core.Platform;

namespace HyPrism.Services.User;

/// <summary>
/// Handles Hytale OAuth 2.0 Authorization Code flow with PKCE.
/// Used to authenticate official Hytale accounts and obtain session tokens
/// for launching the game in authenticated mode.
/// </summary>
/// <remarks>
/// Session data is stored per-profile in the profile's folder to support
/// multiple Hytale accounts across different launcher profiles.
/// </remarks>
public class HytaleAuthService : IHytaleAuthService
{
    private const string AuthUrl = "https://oauth.accounts.hytale.com/oauth2/auth";
    private const string TokenUrl = "https://oauth.accounts.hytale.com/oauth2/token";
    private const string LauncherDataUrl = "https://account-data.hytale.com/my-account/get-launcher-data";
    private const string SessionUrl = "https://sessions.hytale.com/game-session/new";
    private const string ClientId = "hytale-launcher";
    private const string RedirectUri = "https://accounts.hytale.com/consent/client";
    private const string Scopes = "openid offline auth:launcher";

    private readonly HttpClient _httpClient;
    private readonly string _appDir;
    private readonly IBrowserService _browserService;
    private readonly IConfigService _configService;

    private string? _pendingCodeVerifier;
    private string? _pendingState;
    private TaskCompletionSource<string>? _authCodeTcs;
    private System.Net.HttpListener? _callbackListener;

    /// <summary>
    /// The current auth session, or null if not logged in.
    /// </summary>
    public HytaleAuthSession? CurrentSession { get; private set; }

    public HytaleAuthService(HttpClient httpClient, string appDir, IBrowserService browserService, IConfigService configService)
    {
        _httpClient = httpClient;
        _appDir = appDir;
        _browserService = browserService;
        _configService = configService;

        // Try to restore session from disk for current profile
        LoadSession();

        // Also try migrating old global session if exists
        MigrateOldSessionIfNeeded();
    }

    /// <summary>
    /// Starts the OAuth login flow: opens browser, waits for callback, exchanges code for tokens,
    /// fetches profile data.
    /// </summary>
    /// <returns>The authenticated session, or null on failure/cancellation.</returns>
    public async Task<HytaleAuthSession?> LoginAsync(CancellationToken cancellationToken = default)
    {
        // Stop any previous listener to avoid port conflicts
        StopListener();

        try
        {
            // Step 1: Generate PKCE
            var codeVerifier = GenerateCodeVerifier();
            var codeChallenge = GenerateCodeChallenge(codeVerifier);
            _pendingCodeVerifier = codeVerifier;

            // Step 2: Start local HTTP listener for callback
            var listener = new System.Net.HttpListener();
            var port = FindAvailablePort();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            _callbackListener = listener;

            _authCodeTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Step 3: Build auth URL
            var state = GenerateState(port);
            _pendingState = state;

            var authUrl = $"{AuthUrl}?access_type=offline" +
                          $"&client_id={Uri.EscapeDataString(ClientId)}" +
                          $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
                          $"&code_challenge_method=S256" +
                          $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
                          $"&response_type=code" +
                          $"&scope={Uri.EscapeDataString(Scopes)}" +
                          $"&state={Uri.EscapeDataString(state)}";

            Logger.Info("HytaleAuth", "Opening browser for Hytale login...");
            _browserService.OpenURL(authUrl);

            // Step 4: Listen for callback (async)
            _ = ListenForCallbackAsync(listener, cancellationToken);

            // Wait for auth code with generous timeout (user may need to sign in via Google/other OAuth providers)
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            linkedCts.Token.Register(() => _authCodeTcs.TrySetCanceled());

            string authCode;
            try
            {
                authCode = await _authCodeTcs.Task;
            }
            catch (OperationCanceledException)
            {
                Logger.Warning("HytaleAuth", "Login cancelled or timed out");
                return null;
            }
            finally
            {
                StopListener();
            }

            // Step 5: Exchange code for tokens
            Logger.Info("HytaleAuth", "Exchanging auth code for tokens...");
            var tokenResponse = await ExchangeCodeForTokensAsync(authCode);
            if (tokenResponse == null)
            {
                Logger.Error("HytaleAuth", "Failed to exchange auth code for tokens");
                return null;
            }

            // Step 6: Fetch all profiles on the account
            Logger.Info("HytaleAuth", "Fetching Hytale profiles...");
            List<HytaleProfile> allProfiles = await FetchProfileAsync(tokenResponse.AccessToken);
            HytaleProfile primaryProfile = allProfiles[0];

            // Step 7: Create game session for primary profile
            Logger.Info("HytaleAuth", "Creating game session...");
            var gameSession = await CreateGameSessionAsync(tokenResponse.AccessToken, primaryProfile.Uuid);

            var session = new HytaleAuthSession
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn),
                Username = primaryProfile.Username,
                UUID = primaryProfile.Uuid,
                AccountOwnerId = primaryProfile.Owner,
                SessionToken = gameSession?.SessionToken ?? "",
                IdentityToken = gameSession?.IdentityToken ?? "",
                AccountProfiles = allProfiles.Select(p => (p.Username, p.Uuid)).ToList()
            };

            CurrentSession = session;

            Logger.Success("HytaleAuth", $"Logged in as {session.Username} ({session.UUID})");
            return session;
        }
        catch (Exception ex)
        {
            Logger.Error("HytaleAuth", $"Login failed: {ex.Message}");
            return null;
        }
        finally
        {
            // Ensure listener is always stopped
            StopListener();
        }
    }

    /// <summary>
    /// Logs out: clears session from memory and disk.
    /// </summary>
    public void Logout()
    {
        CurrentSession = null;
        TokenStore.Delete(GetSessionFilePath());
        Logger.Info("HytaleAuth", "Logged out");
    }

    /// <summary>
    /// Returns the current auth status (logged in, username, uuid).
    /// </summary>
    public object GetAuthStatus()
    {
        if (CurrentSession != null)
        {
            return new
            {
                loggedIn = true,
                username = CurrentSession.Username,
                uuid = CurrentSession.UUID,
                accountProfiles = CurrentSession.AccountProfiles.Select(p => new { username = p.Username, uuid = p.Uuid }).ToList()
            };
        }
        return new { loggedIn = false, username = (string?)null, uuid = (string?)null };
    }

    /// <summary>
    /// Refreshes the access token if expired. Returns a valid session or null.
    /// </summary>
    public async Task<HytaleAuthSession?> GetValidSessionAsync()
    {
        if (CurrentSession == null) return null;

        if (CurrentSession.ExpiresAt <= DateTime.UtcNow.AddMinutes(1))
        {
            Logger.Info("HytaleAuth", "Access token expired, refreshing...");
            var refreshed = await RefreshTokenAsync();
            if (!refreshed)
            {
                Logger.Warning("HytaleAuth", "Token refresh failed, session invalid");
                Logout();
                return null;
            }
        }

        return CurrentSession;
    }

    /// <summary>
    /// Forces a token refresh regardless of expiry time.
    /// Used when API returns 401/403 indicating token is invalid.
    /// </summary>
    public async Task<bool> ForceRefreshAsync()
    {
        if (CurrentSession == null) return false;

        Logger.Info("HytaleAuth", "Forcing token refresh...");
        var refreshed = await RefreshTokenAsync();
        if (!refreshed)
        {
            Logger.Warning("HytaleAuth", "Forced token refresh failed");
            return false;
        }

        Logger.Success("HytaleAuth", "Token refreshed successfully");
        return true;
    }

    /// <summary>
    /// Ensures a valid session with fresh game session tokens for launching.
    /// Always creates a new game session even if the access token is still valid,
    /// because game session tokens (identityToken/sessionToken) expire independently
    /// and must be refreshed before each launch.
    /// </summary>
    public async Task<HytaleAuthSession?> EnsureFreshSessionForLaunchAsync()
    {
        // Step 1: Ensure we have a valid access token
        var session = await GetValidSessionAsync();
        if (session == null) return null;

        // Step 2: Always create a fresh game session before launch
        Logger.Info("HytaleAuth", "Creating fresh game session for launch...");
        try
        {
            var gameSession = await CreateGameSessionAsync(session.AccessToken, session.UUID);
            if (gameSession != null)
            {
                session.SessionToken = gameSession.SessionToken;
                session.IdentityToken = gameSession.IdentityToken;
                SaveSession();
                Logger.Success("HytaleAuth", "Fresh game session tokens obtained");
            }
            else
            {
                Logger.Warning("HytaleAuth", "Failed to create fresh game session — will use cached tokens");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("HytaleAuth", $"Error creating fresh game session: {ex.Message}");
            // Fall through — cached tokens may still work
        }

        return session;
    }

    #region OAuth Helpers

    private async Task ListenForCallbackAsync(System.Net.HttpListener listener, CancellationToken ct)
    {
        try
        {
            while (listener.IsListening && !ct.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync();
                var request = context.Request;
                var response = context.Response;

                var query = request.QueryString;
                var code = query["code"];
                var error = query["error"];

                string responseHtml;
                if (!string.IsNullOrEmpty(code))
                {
                    responseHtml = @"<html><head><script>window.close();</script></head><body><h1>Authorization successful!</h1><p>You can close this window and return to HyPrism.</p></body></html>";
                    _authCodeTcs?.TrySetResult(code);
                }
                else if (!string.IsNullOrEmpty(error))
                {
                    responseHtml = $@"<html><head><script>setTimeout(function(){{window.close();}},3000);</script></head><body><h1>Authorization failed</h1><p>{error}</p><p>This window will close automatically...</p></body></html>";
                    _authCodeTcs?.TrySetException(new Exception($"OAuth error: {error}"));
                }
                else
                {
                    responseHtml = "<html><body><h1>Waiting for authorization...</h1></body></html>";
                }

                var buffer = Encoding.UTF8.GetBytes(responseHtml);
                response.ContentType = "text/html";
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, ct);
                response.Close();

                if (!string.IsNullOrEmpty(code) || !string.IsNullOrEmpty(error))
                    break;
            }
        }
        catch (ObjectDisposedException) { /* listener stopped */ }
        catch (Exception ex)
        {
            Logger.Warning("HytaleAuth", $"Callback listener error: {ex.Message}");
            _authCodeTcs?.TrySetException(ex);
        }
    }

    private void StopListener()
    {
        try
        {
            _callbackListener?.Stop();
            _callbackListener?.Close();
        }
        catch { /* ignore */ }
        _callbackListener = null;
    }

    private async Task<TokenResponse?> ExchangeCodeForTokensAsync(string code)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = ClientId,
            ["code_verifier"] = _pendingCodeVerifier ?? ""
        });

        try
        {
            var response = await _httpClient.PostAsync(TokenUrl, content);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Logger.Error("HytaleAuth", $"Token exchange failed ({response.StatusCode}): {json}");
                return null;
            }

            return JsonSerializer.Deserialize<TokenResponse>(json);
        }
        catch (Exception ex)
        {
            Logger.Error("HytaleAuth", $"Token exchange exception: {ex.Message}");
            return null;
        }
    }

    private async Task<bool> RefreshTokenAsync()
    {
        if (CurrentSession?.RefreshToken == null) return false;

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = CurrentSession.RefreshToken,
            ["client_id"] = ClientId
        });

        try
        {
            var response = await _httpClient.PostAsync(TokenUrl, content);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Logger.Error("HytaleAuth", $"Token refresh failed ({response.StatusCode}): {json}");
                return false;
            }

            var tokenResp = JsonSerializer.Deserialize<TokenResponse>(json);
            if (tokenResp == null) return false;

            CurrentSession.AccessToken = tokenResp.AccessToken;
            if (!string.IsNullOrEmpty(tokenResp.RefreshToken))
                CurrentSession.RefreshToken = tokenResp.RefreshToken;
            CurrentSession.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResp.ExpiresIn);

            SaveSession();
            Logger.Info("HytaleAuth", "Token refreshed successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("HytaleAuth", $"Token refresh exception: {ex.Message}");
            return false;
        }
    }

    private async Task<List<HytaleProfile>> FetchProfileAsync(string accessToken)
    {
        try
        {
            var profileUrl = HytaleLauncherHeaderHelper.BuildLauncherDataUrlWithClientId(LauncherDataUrl);
            var request = new HttpRequestMessage(HttpMethod.Get, profileUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            await HytaleLauncherHeaderHelper.ApplyOfficialHeadersAsync(request, _httpClient, "release");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Logger.Error("HytaleAuth", $"Profile fetch failed ({response.StatusCode}): {json}");
                throw new HytaleAuthException("profile_fetch_failed", $"HTTP {response.StatusCode}");
            }

            var profilesResp = JsonSerializer.Deserialize<ProfilesResponse>(json);
            if (profilesResp?.Profiles == null || profilesResp.Profiles.Count == 0)
            {
                Logger.Warning("HytaleAuth", "No profiles found in Hytale account");
                throw new HytaleNoProfileException("No game profiles found in this Hytale account");
            }

            return profilesResp.Profiles.Select(p => new HytaleProfile
            {
                Username = p.Username,
                Uuid = p.Uuid,
                Owner = profilesResp.Owner
            }).ToList();
        }
        catch (HytaleNoProfileException) { throw; }
        catch (HytaleAuthException) { throw; }
        catch (Exception ex)
        {
            Logger.Error("HytaleAuth", $"Profile fetch exception: {ex.Message}");
            throw new HytaleAuthException("profile_fetch_error", ex.Message);
        }
    }

    private async Task<GameSessionResponse?> CreateGameSessionAsync(string accessToken, string uuid)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, SessionUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            await HytaleLauncherHeaderHelper.ApplyOfficialHeadersAsync(request, _httpClient, "release");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { uuid }),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("HytaleAuth", $"Game session creation failed ({response.StatusCode}): {json}");
                return null;
            }

            return JsonSerializer.Deserialize<GameSessionResponse>(json);
        }
        catch (Exception ex)
        {
            Logger.Warning("HytaleAuth", $"Game session creation exception: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region PKCE & State Helpers

    private static string GenerateCodeVerifier()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string GenerateCodeChallenge(string verifier)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    private static string GenerateState(int port)
    {
        var stateObj = new { state = GenerateRandomString(26), port = port.ToString() };
        var json = JsonSerializer.Serialize(stateObj);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static string GenerateRandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        var result = new char[length];
        for (int i = 0; i < length; i++)
            result[i] = chars[bytes[i] % chars.Length];
        return new string(result);
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static int FindAvailablePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    #endregion

    #region Session Persistence

    /// <summary>
    /// Reads all profiles from the profiles.json cache on disk.
    /// </summary>
    private List<Profile> ReadProfilesCache()
    {
        try
        {
            var path = Path.Combine(UtilityService.GetProfilesRoot(_appDir), "profiles.json");
            if (!File.Exists(path)) return new();
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(path), opts) ?? new();
        }
        catch { return new(); }
    }

    /// <summary>
    /// Gets the profile folder path for the current active profile.
    /// Returns null if no profile is active.
    /// </summary>
    private string? GetCurrentProfileFolder()
    {
        var config = _configService.Configuration;
        var selectedId = config.SelectedProfileId;
        if (string.IsNullOrEmpty(selectedId))
            return null;

        var profile = ReadProfilesCache().FirstOrDefault(p => p.Id == selectedId);
        if (profile == null)
            return null;

        return UtilityService.GetProfileFolderPath(_appDir, profile);
    }

    /// <summary>
    /// Gets the session file path for the current profile.
    /// Falls back to app root if no profile is active (legacy behavior).
    /// </summary>
    private string GetSessionFilePath() => TokenStore.GetSessionFilePath(GetCurrentProfileFolder(), _appDir);

    /// <summary>
    /// Gets the old (legacy) session file path at app root.
    /// </summary>
    private string GetLegacySessionFilePath() => TokenStore.GetLegacySessionFilePath(_appDir);

    /// <summary>
    /// Migrates old global session file to current profile folder if needed.
    /// </summary>
    private void MigrateOldSessionIfNeeded()
    {
        try
        {
            var legacyPath = GetLegacySessionFilePath();
            var profileFolder = GetCurrentProfileFolder();

            // Only migrate if: old file exists, profile folder exists, and no session loaded yet
            if (profileFolder == null || CurrentSession != null || !File.Exists(legacyPath))
                return;

            var profileSessionPath = Path.Combine(profileFolder, "hytale_session.json");

            // Don't overwrite existing profile session
            if (File.Exists(profileSessionPath))
                return;

            // Copy to profile folder
            Directory.CreateDirectory(profileFolder);
            File.Copy(legacyPath, profileSessionPath);
            Logger.Info("HytaleAuth", $"Migrated session from root to profile folder");

            // Reload session from new location
            LoadSession();

            // Mark the profile as official if session loaded successfully
            if (CurrentSession != null)
            {
                var config = _configService.Configuration;
                var selectedId = config.SelectedProfileId;
                if (!string.IsNullOrEmpty(selectedId))
                {
                    var profilesPath = Path.Combine(UtilityService.GetProfilesRoot(_appDir), "profiles.json");
                    if (File.Exists(profilesPath))
                    {
                        try
                        {
                            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
                            var profiles = JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(profilesPath), opts) ?? new();
                            var activeProfile = profiles.FirstOrDefault(p => p.Id == selectedId);
                            if (activeProfile != null)
                            {
                                activeProfile.IsOfficial = true;
                                File.WriteAllText(profilesPath, JsonSerializer.Serialize(profiles, opts));
                                Logger.Info("HytaleAuth", $"Marked profile '{activeProfile.Name}' as official after migration");
                            }
                        }
                        catch (Exception ex2)
                        {
                            Logger.Warning("HytaleAuth", $"Failed to mark profile as official: {ex2.Message}");
                        }
                    }
                }
            }

            // Delete old file after successful migration
            try { File.Delete(legacyPath); }
            catch (Exception ex) { Logger.Warning("HytaleAuth", $"Could not delete old session file: {ex.Message}"); }
        }
        catch (Exception ex)
        {
            Logger.Warning("HytaleAuth", $"Session migration failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Reloads session for the current profile. Call this after switching profiles.
    /// </summary>
    public void ReloadSessionForCurrentProfile()
    {
        CurrentSession = null;
        LoadSession();
        Logger.Info("HytaleAuth", CurrentSession != null
            ? $"Reloaded session for profile, user: {CurrentSession.Username}"
            : "No session for current profile");
    }

    /// <summary>
    /// Gets a valid session from any official profile (not just the active one).
    /// Used for fetching version info when the current profile may not be official.
    /// </summary>
    public async Task<HytaleAuthSession?> GetValidOfficialSessionAsync()
    {
        // First, try current session if available
        if (CurrentSession != null)
        {
            var validSession = await GetValidSessionAsync();
            if (validSession != null)
            {
                return validSession;
            }
        }

        // If current profile doesn't have a valid session, search all official profiles
        var officialProfiles = ReadProfilesCache().Where(p => p.IsOfficial).ToList();
        if (officialProfiles.Count == 0)
        {
            Logger.Debug("HytaleAuth", "No official profiles found");
            return null;
        }

        // Try to load and validate session from each official profile
        foreach (var profile in officialProfiles)
        {
            var profileDir = UtilityService.GetProfileFolderPath(_appDir, profile, createIfMissing: false, migrateLegacyByName: true);
            var sessionPath = Path.Combine(profileDir, "hytale_session.json");

            if (!File.Exists(sessionPath))
            {
                continue;
            }

            try
            {
                var json = File.ReadAllText(sessionPath);
                var session = JsonSerializer.Deserialize<HytaleAuthSession>(json);
                if (session == null || string.IsNullOrEmpty(session.RefreshToken))
                {
                    continue;
                }

                // Check if token is expired
                if (session.ExpiresAt <= DateTime.UtcNow.AddMinutes(1))
                {
                    // Need to refresh this token
                    Logger.Info("HytaleAuth", $"Refreshing token for official profile '{profile.Name}'...");
                    var refreshed = await RefreshTokenForSessionAsync(session);
                    if (!refreshed)
                    {
                        Logger.Warning("HytaleAuth", $"Failed to refresh token for profile '{profile.Name}'");
                        continue;
                    }

                    // Save refreshed session back to file
                    var updatedJson = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(sessionPath, updatedJson);
                }

                Logger.Info("HytaleAuth", $"Using official profile '{profile.Name}' for API access");
                return session;
            }
            catch (Exception ex)
            {
                Logger.Warning("HytaleAuth", $"Failed to load session for profile '{profile.Name}': {ex.Message}");
            }
        }

        Logger.Warning("HytaleAuth", "No valid official session found in any profile");
        return null;
    }

    /// <summary>
    /// Refreshes token for a specific session (not necessarily the current one).
    /// </summary>
    private async Task<bool> RefreshTokenForSessionAsync(HytaleAuthSession session)
    {
        if (string.IsNullOrEmpty(session.RefreshToken)) return false;

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = session.RefreshToken,
            ["client_id"] = ClientId
        });

        try
        {
            var response = await _httpClient.PostAsync(TokenUrl, content);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Logger.Error("HytaleAuth", $"Token refresh failed ({response.StatusCode}): {json}");
                return false;
            }

            var tokenResp = JsonSerializer.Deserialize<TokenResponse>(json);
            if (tokenResp == null) return false;

            session.AccessToken = tokenResp.AccessToken;
            if (!string.IsNullOrEmpty(tokenResp.RefreshToken))
                session.RefreshToken = tokenResp.RefreshToken;
            session.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResp.ExpiresIn);

            Logger.Info("HytaleAuth", "Token refreshed successfully for session");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("HytaleAuth", $"Token refresh exception: {ex.Message}");
            return false;
        }
    }

    private void SaveSession()
    {
        if (CurrentSession == null) return;
        TokenStore.Save(GetSessionFilePath(), CurrentSession);
    }

    private void LoadSession()
    {
        CurrentSession = TokenStore.Load(GetSessionFilePath());
        if (CurrentSession != null)
            Logger.Info("HytaleAuth", $"Restored session for {CurrentSession.Username}");
    }

    /// <summary>
    /// Saves current session to the active profile's folder.
    /// Use this after LoginAsync() when re-authenticating within an existing official profile.
    /// </summary>
    public void SaveCurrentSession()
    {
        SaveSession();
    }

    /// <summary>
    /// Saves the current session to a specific profile's folder.
    /// Used when creating a new official profile to enable Hytale source access.
    /// </summary>
    /// <param name="profile">The profile to save the session to.</param>
    /// <returns>True if the session was saved successfully.</returns>
    public bool SaveSessionToProfile(Profile profile)
    {
        if (CurrentSession == null)
        {
            Logger.Warning("HytaleAuth", "Cannot save session: no current session");
            return false;
        }

        try
        {
            var profileDir = UtilityService.GetProfileFolderPath(_appDir, profile, createIfMissing: true);
            var sessionPath = Path.Combine(profileDir, "hytale_session.json");

            var json = JsonSerializer.Serialize(CurrentSession, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(sessionPath, json);

            Logger.Success("HytaleAuth", $"Saved Hytale session to profile '{profile.Name}'");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("HytaleAuth", $"Failed to save session to profile: {ex.Message}");
            return false;
        }
    }

    #endregion
}

#region Models

/// <summary>
/// Persisted auth session for Hytale account.
/// </summary>
public class HytaleAuthSession
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = "";

    [JsonPropertyName("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("session_token")]
    public string SessionToken { get; set; } = "";

    [JsonPropertyName("identity_token")]
    public string IdentityToken { get; set; } = "";

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("uuid")]
    public string UUID { get; set; } = "";

    [JsonPropertyName("account_owner_id")]
    public string AccountOwnerId { get; set; } = "";

    [JsonIgnore]
    public List<(string Username, string Uuid)> AccountProfiles { get; set; } = new();
}

internal class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = "";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

internal class ProfilesResponse
{
    [JsonPropertyName("owner")]
    public string Owner { get; set; } = "";

    [JsonPropertyName("profiles")]
    public List<ProfileEntry> Profiles { get; set; } = new();
}

internal class ProfileEntry
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = "";

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";
}

internal class HytaleProfile
{
    public string Username { get; set; } = "";
    public string Uuid { get; set; } = "";
    public string Owner { get; set; } = "";
}

internal class GameSessionResponse
{
    [JsonPropertyName("sessionToken")]
    public string SessionToken { get; set; } = "";

    [JsonPropertyName("identityToken")]
    public string IdentityToken { get; set; } = "";

    [JsonPropertyName("expiresAt")]
    public string ExpiresAt { get; set; } = "";
}

/// <summary>
/// Thrown when no game profiles are found in the Hytale account.
/// This is a non-critical warning — the user simply has no game profile yet.
/// </summary>
public class HytaleNoProfileException : Exception
{
    public HytaleNoProfileException(string message) : base(message) { }
}

/// <summary>
/// Thrown when a general auth error occurs (network, HTTP status, etc.).
/// </summary>
public class HytaleAuthException : Exception
{
    public string ErrorType { get; }
    public HytaleAuthException(string errorType, string message) : base(message)
    {
        ErrorType = errorType;
    }
}

#endregion
