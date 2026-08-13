// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HyPrism.Core.Models;
using HyPrism.Core.Infrastructure;
using HyPrism.Core.Application.Progress;
using HyPrism.Core.Application.Ports;
using HyPrism.Core.Game.Assets;
using HyPrism.Core.Game.Authentication;
using HyPrism.Core.Game.Instances;
using HyPrism.Core.Accounts;

namespace HyPrism.Core.Game.Launch;

/// <summary>
/// Handles the game launch process including client patching, authentication,
/// process creation and monitoring, and Discord Rich Presence updates
/// </summary>
/// <remarks>
/// Extracted from the former monolithic GameInstallationWorkflow for better separation of concerns.
/// Coordinates between multiple services to prepare and launch the game
/// </remarks>
public class GameLauncher : IGameLauncher
{
    private const string DefaultCustomAuthDomain = "sessions.sanasol.ws";

    private readonly IConfigStore _configStore;
    private readonly IRuntimeProvisioner _runtime;
    private readonly IInstanceRepository _instances;
    private readonly IGameProcessTracker _gameProcess;
    private readonly IProgressReporter _progress;
    private readonly IDiscordPresence _discord;
    private readonly ISkinRepository _skins;
    private readonly IAvatarCache _avatars;
    private readonly HttpClient _httpClient;
    private readonly IHytaleAuthenticator _hytaleGameSessionAuthenticator;
    private readonly IGpuProvider _gpuProvider;
    private readonly IProfileManager _profiles;
    private readonly ILocalNodeService _localNode;
    private readonly string _appDir;

    private Config _config => _configStore.Configuration;

    /// <summary>
    /// Stores the DualAuth agent path after download, used when building process start info
    /// </summary>
    private string? _dualAuthAgentPath;
    private string? _launchedProfileUuid;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameLauncher"/> class
    /// </summary>
    /// <param name="configStore">Service for accessing configuration</param>
    /// <param name="runtime">Service for launch prerequisites (JRE, VC++ Redist)</param>
    /// <param name="instances">Service for instance path management</param>
    /// <param name="gameProcess">Service for game process tracking</param>
    /// <param name="progress">Service for progress notifications</param>
    /// <param name="discord">Service for Discord Rich Presence</param>
    /// <param name="skins">Service for skin protection</param>
    /// <param name="avatars">Service for avatar backup</param>
    /// <param name="httpClient">HTTP client for authentication requests</param>
    /// <param name="hytaleGameSessionAuthenticator">Service for official Hytale OAuth authentication</param>
    /// <param name="gpuProvider">Service for GPU detection</param>
    /// <param name="appPath">Application path configuration</param>
    /// <param name="profiles">Service for the active launcher profile</param>
    /// <param name="localNode">Loopback authentication service for autonomous profiles</param>
    public GameLauncher(
        IConfigStore configStore,
        IRuntimeProvisioner runtime,
        IInstanceRepository instances,
        IGameProcessTracker gameProcess,
        IProgressReporter progress,
        IDiscordPresence discord,
        ISkinRepository skins,
        IAvatarCache avatars,
        HttpClient httpClient,
        IHytaleAuthenticator hytaleGameSessionAuthenticator,
        IGpuProvider gpuProvider,
        AppPathConfiguration appPath,
        IProfileManager profiles,
        ILocalNodeService localNode)
    {
        _configStore = configStore;
        _runtime = runtime;
        _instances = instances;
        _gameProcess = gameProcess;
        _progress = progress;
        _discord = discord;
        _skins = skins;
        _avatars = avatars;
        _httpClient = httpClient;
        _hytaleGameSessionAuthenticator = hytaleGameSessionAuthenticator;
        _gpuProvider = gpuProvider;
        _appDir = appPath.AppDir;
        _profiles = profiles;
        _localNode = localNode;
        _gameProcess.ProcessExited += OnGameProcessExited;
    }


    private void OnGameProcessExited(object? sender, EventArgs e)
    {
        try
        {
            Logger.Info("Game", "Game process exited, performing cleanup...");

            _skins.StopSkinProtection();
            if (!string.IsNullOrWhiteSpace(_launchedProfileUuid))
            {
                _skins.BackupProfileSkinData(_launchedProfileUuid);

                // Copy the latest game avatar to persistent backup
                _avatars.BackupAvatar(_launchedProfileUuid);
            }

            _launchedProfileUuid = null;

            _discord.SetPresence(PresenceState.Idle);
            _progress.ReportGameStateChanged("stopped", 0);
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Error during game exit cleanup: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task LaunchGameAsync(
        string versionPath,
        string branch,
        CancellationToken ct = default,
        AuthUriPresenter? authorizationUriPresenter = null)
    {
        Logger.Info("Game", $"Preparing to launch from {versionPath}");

        // Resolve identity exclusively through the explicitly selected profile
        var currentProfile = GetSelectedProfileOrThrow();
        string sessionUuid = currentProfile.UUID;
        string profileName = currentProfile.Name;
        bool isOfficialProfile = currentProfile.IsOfficial;

        var effectiveAuthDomain = GetEffectiveCustomAuthDomain(logFallback: false);

        if (!isOfficialProfile && IsOfficialDomain(_config.AuthDomain) && _config.OnlineMode)
        {
            Logger.Warning("Game", $"Unofficial profile with official auth domain '{_config.AuthDomain}'. Falling back to custom auth domain '{DefaultCustomAuthDomain}' for this launch.");
        }

        // Check auth server availability before proceeding (only for online mode with custom auth)
        if (_config.OnlineMode && !isOfficialProfile)
        {
            var authAvailable = await CheckAuthServerAvailabilityAsync(effectiveAuthDomain!, ct);
            if (!authAvailable)
            {
                var errorMessage = $"Authentication server '{effectiveAuthDomain}' is not reachable. Please check your network connection or auth server settings.";
                Logger.Error("Game", errorMessage);
                throw new Exception(errorMessage);
            }
        }

        if (!isOfficialProfile && !_config.OnlineMode)
        {
            Logger.Info("Game", $"Starting autonomous Local Node at {_localNode.Issuer}");
            await _localNode.EnsureReadyAsync(versionPath, ct);
        }

        var (executable, workingDir) = ResolveExecutablePaths(versionPath);

        if (!File.Exists(executable))
        {
            Logger.Error("Game", $"Game client not found at {executable}");
            throw new Exception($"Game client not found at {executable}");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string appBundle = Path.Combine(versionPath, "Client", "Hytale.app");
            LauncherUtilities.ClearMacQuarantine(appBundle);
            Logger.Info("Game", "Cleared macOS quarantine attributes before patching");
        }

        ct.ThrowIfCancellationRequested();

        await PatchClientIfNeededAsync(versionPath);

        ct.ThrowIfCancellationRequested();

        _progress.ReportDownloadProgress("launching", 0, "launch.detail.authenticating_generic", null, 0, 0);

        Logger.Info("Game", $"Using UUID for selected profile '{profileName}': {sessionUuid}");

        var (identityToken, sessionToken, authPlayerName) = await AuthenticateAsync(
            sessionUuid,
            profileName,
            authorizationUriPresenter,
            ct);
        string launchPlayerName = ResolveLaunchPlayerName(profileName, authPlayerName, identityToken);

        string javaPath = ResolveJavaPath();
        if (!File.Exists(javaPath)) throw new Exception($"Java not found at {javaPath}");

        string userDataDir = _instances.GetInstanceUserDataPath(versionPath);
        Directory.CreateDirectory(userDataDir);

        InvalidateAotCacheIfNeeded(versionPath);

        RestoreProfileSkinData(sessionUuid, userDataDir);

        LogLaunchInfo(executable, javaPath, versionPath, userDataDir, sessionUuid, launchPlayerName);

        var startInfo = BuildProcessStartInfo(executable, workingDir, versionPath, userDataDir, javaPath, sessionUuid, identityToken, sessionToken, launchPlayerName);

        ct.ThrowIfCancellationRequested();

        await StartAndMonitorProcessAsync(startInfo, sessionUuid, launchPlayerName);
    }

    private static (string executable, string workingDir) ResolveExecutablePaths(string versionPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return (
                Path.Combine(versionPath, "Client", "Hytale.app", "Contents", "MacOS", "HytaleClient"),
                Path.Combine(versionPath, "Client", "Hytale.app", "Contents", "MacOS")
            );
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return (
                Path.Combine(versionPath, "Client", "HytaleClient.exe"),
                Path.Combine(versionPath, "Client")
            );
        }

        return (
            Path.Combine(versionPath, "Client", "HytaleClient"),
            Path.Combine(versionPath, "Client")
        );
    }

    private string ResolveJavaPath()
    {
        if (_config.UseCustomJava)
        {
            var customJavaPath = _config.CustomJavaPath?.Trim();
            if (string.IsNullOrWhiteSpace(customJavaPath))
            {
                throw new Exception("Custom Java is enabled, but no executable path is configured.");
            }

            if (!File.Exists(customJavaPath))
            {
                throw new Exception($"Custom Java executable was not found: {customJavaPath}");
            }

            Logger.Info("Game", $"Using custom Java executable: {customJavaPath}");
            return customJavaPath;
        }

        var bundledJavaPath = _runtime.GetJavaPath();
        Logger.Info("Game", $"Using bundled Java executable: {bundledJavaPath}");
        return bundledJavaPath;
    }

    /// <summary>
    /// Determines whether the current AuthDomain setting points to official Hytale servers
    /// (i.e. no custom patching is needed)
    /// </summary>
    private bool IsOfficialServerMode()
    {
        var currentProfile = _profiles.GetProfiles()
            .FirstOrDefault(profile => profile.Id == _config.SelectedProfileId);
        return currentProfile?.IsOfficial == true;
    }

    private Profile GetSelectedProfileOrThrow()
    {
        if (string.IsNullOrWhiteSpace(_config.SelectedProfileId))
        {
            throw new InvalidOperationException("Create or select a profile before launching the game");
        }

        var profile = _profiles.GetProfiles()
            .FirstOrDefault(candidate => candidate.Id == _config.SelectedProfileId);
        if (profile is null)
        {
            throw new InvalidOperationException(
                $"Selected profile '{_config.SelectedProfileId}' does not exist");
        }

        if (string.IsNullOrWhiteSpace(profile.Name) || !Guid.TryParse(profile.UUID, out _))
        {
            throw new InvalidOperationException(
                $"Selected profile '{profile.Id}' has an invalid name or UUID");
        }

        return profile;
    }

    private static bool IsOfficialDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return false;
        var value = domain.Trim();
        return value.Equals("official", StringComparison.OrdinalIgnoreCase)
            || value.Contains("hytale.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if the custom authentication server is reachable before launching the game
    /// </summary>
    /// <param name="authDomain">The auth server domain to check</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if the auth server is reachable, false otherwise</returns>
    private async Task<bool> CheckAuthServerAvailabilityAsync(string authDomain, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(authDomain))
        {
            authDomain = DefaultCustomAuthDomain;
        }

        var normalized = authDomain.Trim().TrimEnd('/');
        if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = $"https://{normalized}";
        }

        var pingUrl = $"{normalized}/health";
        Logger.Info("Game", $"Checking auth server availability: {pingUrl}");

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            using var response = await _httpClient.GetAsync(pingUrl, cts.Token);

            // Consider server reachable if we get any response (including 404, 401, 403)
            var isAvailable = response.IsSuccessStatusCode ||
                (int)response.StatusCode == 404 ||
                (int)response.StatusCode == 401 ||
                (int)response.StatusCode == 403;

            Logger.Info("Game", $"Auth server check result: {(isAvailable ? "available" : "unavailable")} (status: {(int)response.StatusCode})");
            return isAvailable;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Warning("Game", $"Auth server availability check failed: {ex.Message}");
            return false;
        }
    }

    private string GetEffectiveCustomAuthDomain(bool logFallback)
    {
        var configuredDomain = _config.AuthDomain?.Trim();
        if (string.IsNullOrWhiteSpace(configuredDomain))
        {
            if (logFallback)
            {
                Logger.Warning("Game", $"No custom auth domain is configured. Using '{DefaultCustomAuthDomain}'.");
            }

            return DefaultCustomAuthDomain;
        }

        if (IsOfficialDomain(configuredDomain))
        {
            if (logFallback)
            {
                Logger.Warning("Game", $"Configured auth domain '{configuredDomain}' is official, but active profile is not official. Using fallback custom auth domain '{DefaultCustomAuthDomain}'.");
            }

            return DefaultCustomAuthDomain;
        }

        return configuredDomain;
    }

    private async Task PatchClientIfNeededAsync(string versionPath)
    {
        if (IsOfficialServerMode())
        {
            bool clientPatched = ClientPatcher.IsClientPatched(versionPath);
            bool serverPatched = ClientPatcher.IsServerJarPatched(versionPath);

            if (clientPatched || serverPatched)
            {
                Logger.Info("Game", "Official server mode: restoring original unpatched binaries");
                _progress.ReportDownloadProgress("patching", 0, "launch.detail.restoring_originals", null, 0, 0);

                try
                {
                    var restoreResult = ClientPatcher.RestoreAllFromBackup(versionPath, (msg, progress) =>
                    {
                        Logger.Info("Patcher", progress.HasValue ? $"{msg} ({progress}%)" : msg);
                        if (progress.HasValue)
                            _progress.ReportDownloadProgress("patching", (int)progress.Value, msg, null, 0, 0);
                    });

                    if (restoreResult.Success)
                        Logger.Success("Game", "Original binaries restored. Official servers need no patching");
                    else
                        Logger.Warning("Game", $"Restore had issues: {restoreResult.Error}");

                    _progress.ReportDownloadProgress("patching", 100, "launch.detail.patching_complete", null, 0, 0);
                }
                catch (Exception ex)
                {
                    Logger.Warning("Game", $"Error restoring originals: {ex.Message}");
                }
            }
            else
            {
                Logger.Info("Game", "Official server mode: binaries are already unpatched");
            }

            return;
        }

        var effectiveAuthDomain = _config.OnlineMode
            ? GetEffectiveCustomAuthDomain(logFallback: true)
            : _localNode.EndpointDomain;

        _progress.ReportDownloadProgress("patching", 0, "launch.detail.patching_init", null, 0, 0);
        try
        {
            string baseDomain = effectiveAuthDomain;
            if (baseDomain.StartsWith("sessions."))
            {
                baseDomain = baseDomain["sessions.".Length..];
            }

            Logger.Info("Game", $"Patching binary: hytale.com -> {baseDomain}");
            Logger.Info("Game", "Server authentication mode: DualAuth Agent");
            _progress.ReportDownloadProgress("patching", 10, "launch.detail.patching_client", null, 0, 0);

            var patcher = new ClientPatcher(baseDomain);

            // Restore installations created by releases that statically patched the server JAR.
            // The current launch path always keeps the original server binary intact.
            if (ClientPatcher.IsServerJarPatched(versionPath))
            {
                Logger.Info("Game", "Restoring server JAR from a legacy static patch");
                var restoreResult = ClientPatcher.RestoreServerJarFromBackup(versionPath, (msg, progress) =>
                {
                    Logger.Info("Patcher", progress.HasValue ? $"{msg} ({progress}%)" : msg);
                });

                if (!restoreResult.Success)
                {
                    throw new InvalidOperationException(
                        $"Could not restore the original HytaleServer.jar: {restoreResult.Error}");
                }
            }

            var patchResult = patcher.EnsureClientPatched(versionPath, (msg, progress) =>
            {
                Logger.Info("Patcher", progress.HasValue ? $"{msg} ({progress}%)" : msg);
                if (progress.HasValue)
                {
                    int mapped = 10 + (int)(progress.Value * 0.5);
                    _progress.ReportDownloadProgress("patching", mapped, msg, null, 0, 0);
                }
            });

            if (!patchResult.Success)
            {
                throw new InvalidOperationException($"Could not prepare the Hytale client: {patchResult.Error}");
            }

            Logger.Info("Game", $"Preparing DualAuth agent for auth domain: {baseDomain}");
            _progress.ReportDownloadProgress("patching", 65, "launch.detail.dualauth_setup", null, 0, 0);

            var dualAuthResult = _config.OnlineMode
                ? await DualAuthAgent.EnsureAgentUpToDateAsync(_appDir, ReportDualAuthProgress)
                : await DualAuthAgent.EnsureAgentAvailableAsync(_appDir, ReportDualAuthProgress);

            if (!dualAuthResult.Success || string.IsNullOrWhiteSpace(dualAuthResult.AgentPath))
            {
                throw new InvalidOperationException(
                    $"DualAuth Agent is required for local profiles: {dualAuthResult.Error}");
            }

            _dualAuthAgentPath = dualAuthResult.AgentPath;
            Logger.Success("Game", $"DualAuth agent ready: {_dualAuthAgentPath}");

            if (patchResult.PatchCount > 0 && RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                _progress.ReportDownloadProgress("patching", 95, "launch.detail.resigning", null, 0, 0);
                Logger.Info("Game", "Re-signing patched binary...");
                string appBundle = Path.Combine(versionPath, "Client", "Hytale.app");
                bool signed = ClientPatcher.SignMacOSBinary(appBundle);
                if (signed) Logger.Success("Game", "Binary re-signed successfully");
                else Logger.Warning("Game", "Binary signing failed - game may not launch");
            }

            _progress.ReportDownloadProgress("patching", 100, "launch.detail.patching_complete", null, 0, 0);

            // Force GC to reclaim the large byte[] arrays used during binary patching
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Error while preparing authentication: {ex.Message}");
            throw;
        }

        void ReportDualAuthProgress(string message, int? progress)
        {
            Logger.Info("DualAuth", progress.HasValue ? $"{message} ({progress}%)" : message);
            if (progress.HasValue)
            {
                int mapped = 65 + (int)(progress.Value * 0.25);
                _progress.ReportDownloadProgress("patching", mapped, message, null, 0, 0);
            }
        }
    }

    private async Task<(string? identityToken, string? sessionToken, string? authPlayerName)> AuthenticateAsync(
        string sessionUuid,
        string profileName,
        AuthUriPresenter? authorizationUriPresenter,
        CancellationToken cancellationToken)
    {
        string? identityToken = null;
        string? sessionToken = null;
        string? authPlayerName = null;

        // Check if the active profile is an official Hytale account
        var currentProfile = _profiles.GetProfiles().FirstOrDefault(p => p.UUID == sessionUuid);
        bool isOfficialProfile = currentProfile?.IsOfficial == true;

        if (isOfficialProfile)
        {
            // Use HytaleAuthenticator for OAuth tokens from an official account
            // Always create a fresh game session before launch to avoid SESSION EXPIRED errors
            _progress.ReportDownloadProgress("launching", 20, "launch.detail.authenticating_official", null, 0, 0);
            Logger.Info("Game", "Official profile detected. Refreshing tokens and creating a fresh game session");

            try
            {
                // EnsureFreshSessionForLaunchAsync: refreshes access token if expired + always creates new game session
                var session = await _hytaleGameSessionAuthenticator.EnsureFreshSessionForLaunchAsync();
                if (session == null)
                {
                    Logger.Warning("Game", "No valid Hytale session. Attempting full re-authentication...");
                    _progress.ReportDownloadProgress("launching", 25, "launch.detail.authenticating_browser", null, 0, 0);
                    if (authorizationUriPresenter is null)
                    {
                        throw new InvalidOperationException(
                            "Interactive Hytale authorization requires an external URI launcher from the application host");
                    }

                    session = await _hytaleGameSessionAuthenticator.LoginAsync(
                        authorizationUriPresenter,
                        cancellationToken);
                    if (session == null)
                    {
                        Logger.Error("Game", "Full re-authentication failed. Authenticated launch is unavailable");
                        throw new Exception("Official Hytale session expired and re-login failed. Please try logging in again from the profile settings.");
                    }
                    // Save session to the active profile after successful re-authentication
                    _hytaleGameSessionAuthenticator.SaveCurrentSession();
                }

                identityToken = session.IdentityToken;
                sessionToken = session.SessionToken;

                if (!string.IsNullOrEmpty(identityToken))
                    Logger.Success("Game", "Official Hytale identity token obtained");
                else
                    Logger.Warning("Game", "Could not obtain Hytale session tokens. The game may show SESSION EXPIRED");
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                Logger.Error("Game", $"Hytale auth error: {ex.Message}");
                throw;
            }

            return (identityToken, sessionToken, authPlayerName);
        }

        if (!_config.OnlineMode)
        {
            var omniSession = await _localNode.CreateSessionAsync(
                sessionUuid,
                profileName,
                cancellationToken);
            Logger.Success("Game", $"Local Node OmniAuth session created, expires at {omniSession.ExpiresAt:O}");
            return (omniSession.IdentityToken, omniSession.SessionToken, profileName);
        }

        // Use a configured custom authentication domain for connected non-official profiles
        var effectiveAuthDomain = GetEffectiveCustomAuthDomain(logFallback: true);

        _progress.ReportDownloadProgress("launching", 20, "launch.detail.authenticating", [effectiveAuthDomain], 0, 0);
        Logger.Info("Game", $"Online mode enabled - fetching auth tokens from {effectiveAuthDomain}...");

        try
        {
            var authService = new GameSessionAuthenticator(_httpClient, effectiveAuthDomain);
            var tokenResult = await authService.GetGameSessionTokenAsync(sessionUuid, profileName);

            if (tokenResult.Success && !string.IsNullOrEmpty(tokenResult.Token))
            {
                identityToken = tokenResult.Token;
                sessionToken = tokenResult.SessionToken ?? tokenResult.Token;
                authPlayerName = tokenResult.Name;
                Logger.Success("Game", "Identity token obtained successfully");
            }
            else
            {
                throw new InvalidOperationException(
                    $"Authentication service did not create a game session: {tokenResult.Error}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Error fetching auth token: {ex.Message}");
            throw;
        }

        return (identityToken, sessionToken, authPlayerName);
    }

    private static string ResolveLaunchPlayerName(
        string profileName,
        string? authPlayerName,
        string? identityToken)
    {
        string? tokenPlayerName = TryExtractPlayerNameFromJwt(identityToken);

        string resolved = !string.IsNullOrWhiteSpace(authPlayerName)
            ? authPlayerName.Trim()
            : !string.IsNullOrWhiteSpace(tokenPlayerName)
                ? tokenPlayerName.Trim()
                : profileName;

        if (!string.Equals(resolved, profileName, StringComparison.Ordinal))
        {
            Logger.Warning("Game", $"Using token player name '{resolved}' instead of selected profile name '{profileName}' to satisfy server authentication checks");
        }

        return resolved;
    }

    private static string? TryExtractPlayerNameFromJwt(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt))
            return null;

        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2)
                return null;

            string payload = parts[1]
                .Replace('-', '+')
                .Replace('_', '/');

            int padding = (4 - (payload.Length % 4)) % 4;
            if (padding > 0)
                payload = payload.PadRight(payload.Length + padding, '=');

            byte[] payloadBytes = Convert.FromBase64String(payload);
            string json = Encoding.UTF8.GetString(payloadBytes);

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("username", out var username) && username.ValueKind == JsonValueKind.String)
            {
                return username.GetString();
            }

            if (doc.RootElement.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            {
                return name.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private void RestoreProfileSkinData(string sessionUuid, string userDataDir)
    {
        var currentProfile = _profiles.GetProfiles().FirstOrDefault(p => p.UUID == sessionUuid);
        if (currentProfile == null) return;

        _skins.RestoreProfileSkinData(currentProfile);
        Logger.Info("Game", $"Restored skin data for profile '{currentProfile.Name}'");

        string skinCachePath = Path.Combine(userDataDir, "CachedPlayerSkins", $"{currentProfile.UUID}.json");
        if (File.Exists(skinCachePath))
        {
            _skins.StartSkinProtection(currentProfile, skinCachePath);
        }
    }

    /// <summary>
    /// Deletes the AOT (Ahead-Of-Time) cache in the Server directory when JVM flags have changed.
    /// The AOT cache can become invalid if the JRE version or JVM flags change
    /// (e.g., UseCompactObjectHeaders enabled vs disabled), causing the server to fail at startup.
    /// We store a hash of the current JVM flags and invalidate when it changes
    /// </summary>
    private void InvalidateAotCacheIfNeeded(string versionPath)
    {
        string serverDir = Path.Combine(versionPath, "Server");
        if (!Directory.Exists(serverDir))
            return;

        string markerPath = Path.Combine(serverDir, ".jvm-flags-hash");
        string currentFlags = _config.JavaArguments?.Trim() ?? "";
        string currentHash = ComputeSimpleHash(currentFlags);

        if (File.Exists(markerPath))
        {
            try
            {
                string storedHash = File.ReadAllText(markerPath).Trim();
                if (storedHash == currentHash)
                    return; // No change in JVM flags
            }
            catch { /* If we can't read, re-invalidate */ }
        }

        // Delete AOT cache files
        try
        {
            int deletedCount = 0;
            foreach (var aotFile in Directory.EnumerateFiles(serverDir, "*.aot", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(aotFile);
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    Logger.Warning("Game", $"Failed to delete AOT cache file '{Path.GetFileName(aotFile)}': {ex.Message}");
                }
            }

            // Also look for AOT-related directories (e.g., ".jsa" shared archives)
            foreach (var jsaFile in Directory.EnumerateFiles(serverDir, "*.jsa", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(jsaFile);
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    Logger.Warning("Game", $"Failed to delete shared archive '{Path.GetFileName(jsaFile)}': {ex.Message}");
                }
            }

            if (deletedCount > 0)
                Logger.Info("Game", $"Invalidated {deletedCount} AOT/shared archive cache file(s) due to JVM flags change");

            // Store current hash
            File.WriteAllText(markerPath, currentHash);
        }
        catch (Exception ex)
        {
            Logger.Warning("Game", $"AOT cache invalidation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Computes a simple deterministic hash string for JVM flags comparison
    /// </summary>
    private static string ComputeSimpleHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..16]; // 16 hex chars is sufficient for comparison
    }

    private void LogLaunchInfo(string executable, string javaPath, string gameDir, string userDataDir, string sessionUuid, string launchPlayerName)
    {
        Logger.Info("Game", $"Launching: {executable}");
        Logger.Info("Game", $"Java: {javaPath}");
        Logger.Info("Game", $"AppDir: {gameDir}");
        Logger.Info("Game", $"UserData: {userDataDir}");
        Logger.Info("Game", $"Online Mode: {_config.OnlineMode}");
        Logger.Info("Game", $"Session UUID: {sessionUuid}");
        Logger.Info("Game", $"Launch Player Name: {launchPlayerName}");
    }

    private ProcessStartInfo BuildProcessStartInfo(
        string executable, string workingDir, string versionPath,
        string userDataDir, string javaPath, string sessionUuid,
        string? identityToken, string? sessionToken, string launchPlayerName)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var startInfo = BuildWindowsStartInfo(executable, workingDir, versionPath, userDataDir, javaPath, sessionUuid, identityToken, sessionToken, launchPlayerName);
            ApplyGpuEnvironment(startInfo);
            ApplyDualAuthEnvironment(startInfo);
            ApplyUserJavaArguments(startInfo);
            if (!_config.OnlineMode && !IsOfficialServerMode())
                _localNode.ApplyClientTrust(startInfo);
            return startInfo;
        }

        var unixStartInfo = BuildUnixStartInfo(executable, workingDir, versionPath, userDataDir, javaPath, sessionUuid, identityToken, sessionToken, launchPlayerName);
        ApplyUserJavaArguments(unixStartInfo);
        if (!_config.OnlineMode && !IsOfficialServerMode())
            _localNode.ApplyClientTrust(unixStartInfo);
        return unixStartInfo;
    }

    private static string MergeJavaToolOptions(string? existing, string additional)
        => JvmArgumentBuilder.MergeToolOptions(existing, additional);

    /// <summary>
    /// Applies user-provided Java arguments via JAVA_TOOL_OPTIONS.
    /// This affects Java processes started by the game client while preserving existing flags (for example DualAuth javaagent)
    /// </summary>
    private void ApplyUserJavaArguments(ProcessStartInfo startInfo)
    {
        if (JvmArgumentBuilder.ApplyToProcess(startInfo, _config.JavaArguments))
            Logger.Info("Game", "Applied custom Java arguments from settings");
    }

    private static string SanitizeUserJavaArguments(string args)
        => JvmArgumentBuilder.Sanitize(args);

    /// <summary>
    /// Applies DualAuth environment variables for local-profile authentication
    /// </summary>
    private void ApplyDualAuthEnvironment(ProcessStartInfo startInfo)
    {
        if (string.IsNullOrEmpty(_dualAuthAgentPath) || IsOfficialServerMode())
            return;

        string authDomain = GetDualAuthDomain();

        DualAuthAgent.ApplyToProcess(startInfo, _dualAuthAgentPath, authDomain, trustOfficialIssuers: true);
        Logger.Info("Game", $"DualAuth environment applied to process (auth domain: {authDomain})");
    }

    /// <summary>
    /// Derives the DualAuth domain (used for JWKS discovery) from the sessions domain.
    /// For example, "sessions.sanasol.ws" → "auth.sanasol.ws"
    /// </summary>
    private static string DeriveAuthDomain(string? sessionsDomain)
    {
        if (string.IsNullOrWhiteSpace(sessionsDomain))
            return "";

        string baseDomain = sessionsDomain;
        if (baseDomain.StartsWith("sessions."))
            baseDomain = baseDomain["sessions.".Length..];

        return $"auth.{baseDomain}";
    }

    private string GetDualAuthDomain()
        => _config.OnlineMode
            ? DeriveAuthDomain(GetEffectiveCustomAuthDomain(logFallback: false))
            : _localNode.EndpointDomain;

    /// <summary>
    /// Applies GPU environment variables to a ProcessStartInfo based on the configured GPU preference.
    /// Used for Windows direct-launch mode. Linux/macOS uses the launch script approach
    /// </summary>
    private void ApplyGpuEnvironment(ProcessStartInfo startInfo)
    {
        var gpuPref = _config.GpuPreference?.ToLowerInvariant() ?? "dedicated";
        if (gpuPref == "auto") return;

        if (gpuPref == "dedicated")
        {
            // NVIDIA Optimus: request dedicated GPU
            startInfo.Environment["__NV_PRIME_RENDER_OFFLOAD"] = "1";
            startInfo.Environment["__GLX_VENDOR_LIBRARY_NAME"] = "nvidia";
            // AMD switchable graphics
            startInfo.Environment["DRI_PRIME"] = "1";
            // Windows: hint to driver to use high-performance GPU
            startInfo.Environment["DXGI_GPU_PREFERENCE"] = "2";
            Logger.Info("Game", "GPU preference: dedicated (NVIDIA/AMD env vars set)");
        }
        else if (gpuPref == "integrated")
        {
            startInfo.Environment["DRI_PRIME"] = "0";
            startInfo.Environment["__NV_PRIME_RENDER_OFFLOAD"] = "0";
            startInfo.Environment["DXGI_GPU_PREFERENCE"] = "1";
            Logger.Info("Game", "GPU preference: integrated (env vars set)");
        }
    }

    private ProcessStartInfo BuildWindowsStartInfo(
        string executable, string workingDir, string gameDir,
        string userDataDir, string javaPath, string sessionUuid,
        string? identityToken, string? sessionToken, string launchPlayerName)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("--app-dir");
        startInfo.ArgumentList.Add(gameDir);
        startInfo.ArgumentList.Add("--user-dir");
        startInfo.ArgumentList.Add(userDataDir);
        startInfo.ArgumentList.Add("--java-exec");
        startInfo.ArgumentList.Add(javaPath);
        startInfo.ArgumentList.Add("--name");
        startInfo.ArgumentList.Add(launchPlayerName);

        if (!string.IsNullOrEmpty(identityToken) && !string.IsNullOrEmpty(sessionToken))
        {
            startInfo.ArgumentList.Add("--auth-mode");
            startInfo.ArgumentList.Add("authenticated");
            startInfo.ArgumentList.Add("--uuid");
            startInfo.ArgumentList.Add(sessionUuid);
            startInfo.ArgumentList.Add("--identity-token");
            startInfo.ArgumentList.Add(identityToken);
            startInfo.ArgumentList.Add("--session-token");
            startInfo.ArgumentList.Add(sessionToken);
            Logger.Info("Game", $"Using authenticated mode with session UUID: {sessionUuid}");
        }
        else
        {
            startInfo.ArgumentList.Add("--auth-mode");
            startInfo.ArgumentList.Add("offline");
            startInfo.ArgumentList.Add("--uuid");
            startInfo.ArgumentList.Add(sessionUuid);

            Logger.Info("Game", $"Using offline mode with UUID: {sessionUuid}");
        }

        Logger.Info("Game", $"Windows launch args: {string.Join(" ", startInfo.ArgumentList)}");
        return startInfo;
    }

    private ProcessStartInfo BuildUnixStartInfo(
        string executable, string workingDir, string versionPath,
        string userDataDir, string javaPath, string sessionUuid,
        string? identityToken, string? sessionToken, string launchPlayerName)
    {
        var gameArgs = new List<string>
        {
            $"--app-dir \"{versionPath}\"",
            $"--user-dir \"{userDataDir}\"",
            $"--java-exec \"{javaPath}\"",
            $"--name \"{launchPlayerName}\""
        };

        if (!string.IsNullOrEmpty(identityToken) && !string.IsNullOrEmpty(sessionToken))
        {
            gameArgs.Add("--auth-mode authenticated");
            gameArgs.Add($"--uuid \"{sessionUuid}\"");
            gameArgs.Add($"--identity-token \"{identityToken}\"");
            gameArgs.Add($"--session-token \"{sessionToken}\"");
            Logger.Info("Game", $"Using authenticated mode with session UUID: {sessionUuid}");
        }
        else
        {
            gameArgs.Add("--auth-mode offline");
            gameArgs.Add($"--uuid \"{sessionUuid}\"");
            Logger.Info("Game", $"Using offline mode with UUID: {sessionUuid}");
        }

        string argsString = string.Join(" ", gameArgs);
        string launchScript = Path.Combine(versionPath, "launch.sh");
        string homeDir = Environment.GetEnvironmentVariable("HOME") ?? "/Users/" + Environment.UserName;
        string userName = Environment.GetEnvironmentVariable("USER") ?? Environment.UserName;
        string clientDir = Path.Combine(versionPath, "Client");

        string scriptContent = $@"#!/bin/bash
# Launch script generated by HyPrism
# Uses env to set a clean environment before launching game

# Set LD_LIBRARY_PATH to include Client directory for shared libraries
CLIENT_DIR=""{clientDir}""

{BuildGpuEnvLines()}{BuildDualAuthEnvLines()}
{BuildUserJavaEnvLines()}
# Build env args for a clean process environment
ENV_ARGS=()
ENV_ARGS+=(HOME=""{homeDir}"")
ENV_ARGS+=(USER=""{userName}"")
ENV_ARGS+=(PATH=""/usr/bin:/bin:/usr/sbin:/sbin:/usr/local/bin"")
ENV_ARGS+=(SHELL=""/bin/zsh"")
ENV_ARGS+=(TMPDIR=""{Path.GetTempPath().TrimEnd('/')}"")
ENV_ARGS+=(LD_LIBRARY_PATH=""$CLIENT_DIR:$LD_LIBRARY_PATH"")

# Add Java tool options (DualAuth + user-defined args)
COMBINED_JAVA_TOOL_OPTIONS=
if [[ -n ""$DUALAUTH_JAVA_TOOL_OPTIONS"" ]]; then
    COMBINED_JAVA_TOOL_OPTIONS=""$DUALAUTH_JAVA_TOOL_OPTIONS""
fi
if [[ -n ""$USER_JAVA_TOOL_OPTIONS"" ]]; then
    if [[ -n ""$COMBINED_JAVA_TOOL_OPTIONS"" ]]; then
        COMBINED_JAVA_TOOL_OPTIONS=""$COMBINED_JAVA_TOOL_OPTIONS $USER_JAVA_TOOL_OPTIONS""
    else
        COMBINED_JAVA_TOOL_OPTIONS=""$USER_JAVA_TOOL_OPTIONS""
    fi
fi
if [[ -n ""$HYPRISM_LOCAL_NODE_JAVA_OPTIONS"" ]]; then
    if [[ -n ""$COMBINED_JAVA_TOOL_OPTIONS"" ]]; then
        COMBINED_JAVA_TOOL_OPTIONS=""$COMBINED_JAVA_TOOL_OPTIONS $HYPRISM_LOCAL_NODE_JAVA_OPTIONS""
    else
        COMBINED_JAVA_TOOL_OPTIONS=""$HYPRISM_LOCAL_NODE_JAVA_OPTIONS""
    fi
fi
if [[ -n ""$COMBINED_JAVA_TOOL_OPTIONS"" ]]; then
    ENV_ARGS+=(""JAVA_TOOL_OPTIONS=$COMBINED_JAVA_TOOL_OPTIONS"")
fi
[[ -n ""$HYPRISM_LOCAL_NODE_CA_BUNDLE"" ]] && ENV_ARGS+=(""SSL_CERT_FILE=$HYPRISM_LOCAL_NODE_CA_BUNDLE"")
[[ -n ""$DUALAUTH_AUTH_DOMAIN"" ]] && ENV_ARGS+=(""HYTALE_AUTH_DOMAIN=$DUALAUTH_AUTH_DOMAIN"")
[[ -n ""$DUALAUTH_TRUST_ALL"" ]] && ENV_ARGS+=(""HYTALE_TRUST_ALL_ISSUERS=$DUALAUTH_TRUST_ALL"")
[[ -n ""$DUALAUTH_TRUST_OFFICIAL"" ]] && ENV_ARGS+=(""HYTALE_TRUST_OFFICIAL=$DUALAUTH_TRUST_OFFICIAL"")
{BuildCustomEnvLines()}
exec env ""${{ENV_ARGS[@]}}"" ""{executable}"" {argsString}
";
        File.WriteAllText(launchScript, scriptContent);

        using var chmod = Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/chmod",
            Arguments = $"+x \"{launchScript}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        chmod?.WaitForExit();

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(launchScript);

        Logger.Info("Game", $"Launch script: {launchScript}");
        return startInfo;
    }

    /// <summary>
    /// Builds GPU environment variable lines for the Unix launch script.
    /// Returns a string with export lines to be placed before 'exec env'.
    /// Detects the GPU vendor and applies appropriate environment variables
    /// </summary>
    private string BuildGpuEnvLines()
    {
        var gpuPref = _config.GpuPreference?.ToLowerInvariant() ?? "dedicated";
        if (gpuPref == "auto") return "# GPU preference: auto (system decides)\n\n";

        if (gpuPref == "dedicated")
        {
            var sb = new StringBuilder();
            sb.AppendLine("# GPU preference: dedicated (discrete GPU)");

            // Detect the vendor of the dedicated GPU
            var adapters = _gpuProvider.GetAdapters();
            var dedicatedGpu = adapters.FirstOrDefault(a => a.Type == "dedicated");

            if (dedicatedGpu != null && !string.IsNullOrEmpty(dedicatedGpu.PciId))
            {
                // Use explicit PCI ID for DRI_PRIME if available for more precise selection
                Logger.Info("Game", $"Using dedicated GPU PCI ID for DRI_PRIME: {dedicatedGpu.PciId}");
                sb.AppendLine($"export DRI_PRIME=pci:{dedicatedGpu.PciId}");
            }
            else
            {
                // Fallback to DRI_PRIME=1 if PCI ID detection failed or not applicable
                Logger.Info("Game", "Using generic DRI_PRIME=1 for dedicated GPU");
                sb.AppendLine("export DRI_PRIME=1");
            }

            var vendor = dedicatedGpu?.Vendor?.ToUpperInvariant() ?? "";

            if (vendor == "NVIDIA")
            {
                Logger.Info("Game", "GPU preference: dedicated (NVIDIA env vars in launch script)");
                sb.AppendLine("export __NV_PRIME_RENDER_OFFLOAD=1");
                sb.AppendLine("export __GLX_VENDOR_LIBRARY_NAME=nvidia");

                var nvidiaEglVendorJson = TryGetLinuxNvidiaEglVendorJsonPath();
                if (!string.IsNullOrWhiteSpace(nvidiaEglVendorJson))
                {
                    sb.AppendLine($"export __EGL_VENDOR_LIBRARY_FILENAMES=\"{nvidiaEglVendorJson}\"");
                    Logger.Info("Game", $"Applied NVIDIA EGL vendor override: {nvidiaEglVendorJson}");
                }
            }
            else if (vendor == "AMD")
            {
                Logger.Info("Game", "GPU preference: dedicated (AMD env vars in launch script)");
            }
            else
            {
                // Apply both NVIDIA and AMD variables when the vendor is unknown
                Logger.Info("Game", "GPU preference: dedicated (generic env vars, unknown vendor)");
                sb.AppendLine("export __NV_PRIME_RENDER_OFFLOAD=1");
                sb.AppendLine("export __GLX_VENDOR_LIBRARY_NAME=nvidia");

                var nvidiaEglVendorJson = TryGetLinuxNvidiaEglVendorJsonPath();
                if (!string.IsNullOrWhiteSpace(nvidiaEglVendorJson))
                {
                    sb.AppendLine($"export __EGL_VENDOR_LIBRARY_FILENAMES=\"{nvidiaEglVendorJson}\"");
                }
            }

            sb.AppendLine();
            return sb.ToString();
        }

        if (gpuPref == "integrated")
        {
            Logger.Info("Game", "GPU preference: integrated");
            return @"# GPU preference: integrated
export DRI_PRIME=0
export __NV_PRIME_RENDER_OFFLOAD=0

";
        }

        return "";
    }

    private static string? TryGetLinuxNvidiaEglVendorJsonPath()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return null;

        const string glvndDir = "/usr/share/glvnd/egl_vendor.d";
        if (!Directory.Exists(glvndDir))
            return null;

        var preferred = new[]
        {
            Path.Combine(glvndDir, "10_nvidia.json"),
            Path.Combine(glvndDir, "15_nvidia_gbm.json"),
            Path.Combine(glvndDir, "20_nvidia.json")
        };

        foreach (var candidate in preferred)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        try
        {
            foreach (var candidate in Directory.GetFiles(glvndDir, "*nvidia*.json", SearchOption.TopDirectoryOnly))
            {
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        catch
        {
        }

        return null;
    }

    /// <summary>
    /// Builds custom environment variable lines for the Unix launch script.
    /// Parses KEY=VALUE pairs from config and adds them to ENV_ARGS
    /// </summary>
    private string BuildCustomEnvLines()
    {
        var customEnv = _config.GameEnvironmentVariables?.Trim();
        if (string.IsNullOrWhiteSpace(customEnv))
            return "# No custom environment variables\n\n";

        var sb = new StringBuilder();
        sb.AppendLine("# Custom environment variables from Settings");

        var lines = customEnv.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var validCount = 0;

        // Regex for parsing space-separated KEY=VALUE pairs (supports quotes)
        // Matches: KEY="VALUE" OR KEY='VALUE' OR KEY=VALUE
        var envVarRegex = new Regex(@"(?<key>[A-Za-z_][A-Za-z0-9_]*)=(?<value>""[^""]*""|'[^']*'|[^""'\s]+)", RegexOptions.Compiled);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            // Skip comments and empty lines
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                continue;

            // Check if line contains multiple assignments (heuristic: "KEY=" appearing after whitespace)
            // If so, use regex parsing to robustly extract multiple variables from one line
            bool isMultiVarLine = Regex.IsMatch(trimmed, @"\s+[A-Za-z_][A-Za-z0-9_]*=");

            if (isMultiVarLine)
            {
                var matches = envVarRegex.Matches(trimmed);
                foreach (Match match in matches)
                {
                    var key = match.Groups["key"].Value;
                    var val = match.Groups["value"].Value;

                    // Remove surrounding quotes if present
                    if ((val.StartsWith('"') && val.EndsWith('"')) || (val.StartsWith('\'') && val.EndsWith('\'')))
                    {
                        if (val.Length >= 2) val = val.Substring(1, val.Length - 2);
                    }

                    var escaped = EscapeForBashDoubleQuoted(val);
                    sb.AppendLine($"ENV_ARGS+=({key}=\"{escaped}\")");
                    validCount++;
                }
            }
            else
            {
                // Classic parsing: treat entire remainder of line as value
                // Validate KEY=VALUE format
                var eqIndex = trimmed.IndexOf('=');
                if (eqIndex <= 0) continue;

                var key = trimmed[..eqIndex].Trim();
                var value = trimmed[(eqIndex + 1)..].Trim();

                // Validate key is a valid env var name (alphanumeric + underscore, starts with letter/underscore)
                if (!Regex.IsMatch(key, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                    continue;

                // Escape value for bash
                var escapedValue = EscapeForBashDoubleQuoted(value);
                sb.AppendLine($"ENV_ARGS+=({key}=\"{escapedValue}\")");
                validCount++;
            }
        }

        if (validCount > 0)
        {
            Logger.Info("Game", $"Applied {validCount} custom environment variable(s) from settings");
        }

        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// Builds DualAuth environment variable lines for the Unix launch script.
    /// Returns a string with variable assignments to be placed before 'exec env'.
    /// Each variable is quoted individually to handle paths with spaces.
    /// </summary>
    private string BuildDualAuthEnvLines()
    {
        if (string.IsNullOrEmpty(_dualAuthAgentPath) || IsOfficialServerMode())
            return "# No DualAuth for official profiles\nDUALAUTH_JAVA_TOOL_OPTIONS=\"\"\nDUALAUTH_AUTH_DOMAIN=\"\"\nDUALAUTH_TRUST_ALL=\"\"\nDUALAUTH_TRUST_OFFICIAL=\"\"\n\n";

        string authDomain = GetDualAuthDomain();

        Logger.Info("Game", $"DualAuth env lines for Unix script: {authDomain}");

        // Store DualAuth values in separate shell variables, then compose the
        // JAVA_TOOL_OPTIONS=KEY=VALUE pair when building ENV_ARGS.
        // This avoids nested quoting issues where paths with spaces (e.g.
        // "Application Support") broke the javaagent argument.
        // The JAVA_TOOL_OPTIONS value includes literal double quotes so Java's
        // tokenizer treats the entire -javaagent:... as one token even when
        // the path contains spaces.
        return $@"# DualAuth Agent Configuration
DUALAUTH_JAVA_TOOL_OPTIONS=""\""-javaagent:{_dualAuthAgentPath}\""""
DUALAUTH_AUTH_DOMAIN=""{authDomain}""
DUALAUTH_TRUST_ALL=""true""
DUALAUTH_TRUST_OFFICIAL=""true""

";
    }

    private string BuildUserJavaEnvLines()
        => JvmArgumentBuilder.BuildEnvLine(_config.JavaArguments);

    private static string EscapeForBashDoubleQuoted(string value)
        => JvmArgumentBuilder.EscapeForBash(value);

    private async Task StartAndMonitorProcessAsync(
        ProcessStartInfo startInfo,
        string sessionUuid,
        string profileName)
    {

        Process? process = null;
        try
        {
            _progress.ReportDownloadProgress("launching", 80, "launch.detail.starting_process", null, 0, 0);

            process = new Process { StartInfo = startInfo };
            var interfaceLoadedTcs = new TaskCompletionSource<bool>();

            var sysInfoBuffer = new List<string>();
            bool capturingSysInfo = false;
            bool capturingAudio = false;

            process.OutputDataReceived += (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                string line = e.Data;
                bool isNewLogEntry = Regex.IsMatch(line, @"^\d{4}-\d{2}-\d{2}");

                if (line.StartsWith("Set log path to")) { Logger.Info("Game", line); return; }

                if (line.Trim() == "System informations" || line.Contains("|System informations"))
                { capturingSysInfo = true; return; }

                if (capturingSysInfo)
                {
                    if (isNewLogEntry) { capturingSysInfo = false; }
                    else
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("OpenGL") || trimmed.StartsWith("GPU"))
                        { sysInfoBuffer.Add(trimmed); return; }
                    }
                }

                if (line.Contains("|Audio:")) { capturingAudio = true; return; }

                if (capturingAudio)
                {
                    if (isNewLogEntry)
                    {
                        capturingAudio = false;
                        Logger.Info("Game", "Got system info");
                        foreach (var sysLine in sysInfoBuffer) Logger.Info("Game", $"\t{sysLine}");
                        sysInfoBuffer.Clear();
                    }
                    else
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("OpenAL") || trimmed.StartsWith("Renderer") ||
                            trimmed.StartsWith("Vendor") || trimmed.StartsWith("Using device"))
                        { sysInfoBuffer.Add(trimmed); }
                        return;
                    }
                }

                if (line.Contains("|INFO|HytaleClient.Application.AppStartup|Interface loaded.") ||
                    line.Contains("Interface loaded."))
                {
                    Logger.Success("Game", "Started successfully");
                    interfaceLoadedTcs.TrySetResult(true);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                Logger.Warning("Game", $"stderr: {e.Data}");
            };

            if (!process.Start())
            {
                Logger.Error("Game", "Process.Start returned false - game failed to launch");
                _progress.ReportError("launch", "Failed to start game", "Process.Start returned false");
                throw new Exception("Failed to start game process");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!_config.OnlineMode && !IsOfficialServerMode())
                await _localNode.AttachGameProcessAsync(process.Id);

            // Transfer ownership to GameProcessTracker (it will handle disposal and notify subscribers)
            _launchedProfileUuid = sessionUuid;
            _gameProcess.SetGameProcess(process);
            Logger.Success("Game", $"Game started with PID: {process.Id}");

            _discord.SetPresence(PresenceState.Playing, $"Playing as {profileName}");
            _progress.ReportGameStateChanged("started", process.Id);
            _progress.ReportDownloadProgress("launching", 100, "launch.detail.waiting_for_window", null, 0, 0);

            // Wait for interface loaded signal or timeout (60s)
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(60));
            var completedTask = await Task.WhenAny(interfaceLoadedTcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                Logger.Warning("Game", "Timed out waiting for interface load signal (or game output is silent)");
            }

            _progress.ReportDownloadProgress("complete", 100, "launch.detail.done", null, 0, 0);
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Failed to start game process: {ex.Message}");

            // Cleanup process if failed before transferring to GameProcessTracker
            if (process != null && _gameProcess.GetGameProcess() != process)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
                try { process.Dispose(); } catch { }
            }

            if (!_config.OnlineMode
                && !IsOfficialServerMode()
                && (process is null || _gameProcess.GetGameProcess() != process))
            {
                try
                {
                    await _localNode.StopAsync();
                }
                catch (Exception stopException)
                {
                    Logger.Warning("LocalNode", $"Could not stop after failed game launch: {stopException.Message}");
                }
            }

            _progress.ReportError("launch", "Failed to start game", ex.Message);
            throw new Exception($"Failed to start game: {ex.Message}");
        }
    }
}
