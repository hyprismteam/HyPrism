using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Game;
using HyPrism.Services.Game.Instance;
using HyPrism.Services.Game.Version;
using static HyPrism.Services.Core.App.LauncherPackageExtractor;

namespace HyPrism.Services.Core.App;

/// <summary>
/// Manages HyPrism launcher updates via GitHub Releases.
/// Supports release (stable) and beta (pre-release) update channels.
/// </summary>
/// <remarks>
/// Checks GitHub releases API for new versions and handles the download,
/// extraction, and restart process for self-updating.
/// </remarks>
public class UpdateService : IUpdateService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/hyprismteam/HyPrism/releases";
    
    private static readonly Lazy<string> _launcherVersion = new(() =>
    {
        var assembly = Assembly.GetExecutingAssembly();
        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        
        if (!string.IsNullOrEmpty(infoVersion))
        {
            // Remove build metadata (e.g., "+abc123" suffix) if present
            var plusIndex = infoVersion.IndexOf('+');
            return plusIndex > 0 ? infoVersion[..plusIndex] : infoVersion;
        }
        
        var version = assembly.GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
    });
    
    private readonly HttpClient _httpClient;
    private readonly IConfigService _configService;
    private readonly IVersionService _versionService;
    private readonly IInstanceService _instanceService;
    private readonly IProgressNotificationService _progressNotificationService;

    /// <summary>
    /// Raised when a launcher update is available.
    /// </summary>
    public event Action<object>? LauncherUpdateAvailable;

    /// <summary>
    /// Raised during launcher update download/install to report progress.
    /// </summary>
    public event Action<object>? LauncherUpdateProgress;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client for API requests.</param>
    /// <param name="configService">The configuration service.</param>
    /// <param name="versionService">The version service for version checks.</param>
    /// <param name="instanceService">The instance service for path management.</param>
    /// <param name="progressNotificationService">The progress notification service.</param>
    public UpdateService(
        HttpClient httpClient,
        IConfigService configService,
        IVersionService versionService,
        IInstanceService instanceService,
        IProgressNotificationService progressNotificationService)
    {
        _httpClient = httpClient;
        _configService = configService;
        _versionService = versionService;
        _instanceService = instanceService;
        _progressNotificationService = progressNotificationService;

        // GitHub API requires a User-Agent; keep this safe even if DI didn't configure it.
        try
        {
            if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("HyPrismLauncher/1.0");
            }
        }
        catch
        {
            // Non-fatal
        }
    }

    private void EmitLauncherUpdateProgress(
        string stage,
        double progress,
        string message,
        long downloadedBytes = 0,
        long totalBytes = 0,
        string? downloadedFilePath = null,
        bool? hasDownloadedFile = null)
    {
        try
        {
            LauncherUpdateProgress?.Invoke(new
            {
                stage,
                progress = Math.Clamp(progress, 0, 100),
                message,
                downloadedBytes,
                totalBytes,
                downloadedFilePath,
                hasDownloadedFile
            });
        }
        catch
        {
            // Progress events must never break update flow.
        }
    }

    private Config _config => _configService.Configuration;

    private static string? TryGetDownloadsDirectory()
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home)) return null;
            var downloads = Path.Combine(home, "Downloads");
            return Directory.Exists(downloads) ? downloads : null;
        }
        catch
        {
            return null;
        }
    }

    private string GetInstalledLauncherBranchOrInit(string desiredBranch)
    {
        var installed = _config.InstalledLauncherBranch;
        if (!string.IsNullOrWhiteSpace(installed))
            return installed;

        // First run (or old config): assume the currently running launcher matches the user's desired channel.
        installed = string.IsNullOrWhiteSpace(desiredBranch) ? "release" : desiredBranch;
        _config.InstalledLauncherBranch = installed;
        try { _configService.SaveConfig(); } catch { /* ignore */ }
        return installed;
    }

    /// <summary>
    /// Gets the path to the latest game instance for the current branch.
    /// </summary>
    /// <returns>The path to the latest instance directory.</returns>
    private string GetLatestInstancePath()
    {
        #pragma warning disable CS0618 // Backward compatibility: VersionType kept for migration
        var branch = UtilityService.NormalizeVersionType(_config.VersionType);
        #pragma warning restore CS0618
        var info = _instanceService.LoadLatestInfo(branch);
        if (info != null)
        {
            return _instanceService.ResolveInstancePath(branch, info.Version, true);
        }
        return _instanceService.ResolveInstancePath(branch, 0, true);
    }

    #region Public API

    /// <summary>
    /// Returns the current launcher version string.
    /// </summary>
    public string GetLauncherVersion() => _launcherVersion.Value;

    /// <summary>
    /// Gets the current launcher version (static accessor).
    /// </summary>
    public static string GetCurrentVersion() => _launcherVersion.Value;

    /// <summary>
    /// Returns the active update channel for the launcher (<c>"release"</c> or <c>"beta"</c>).
    /// Falls back to <c>"release"</c> when no channel is configured.
    /// </summary>
    public string GetLauncherBranch() => 
        string.IsNullOrWhiteSpace(_config.LauncherBranch) ? "release" : _config.LauncherBranch;

    /// <summary>
    /// Checks GitHub for a newer launcher release and raises <c>LauncherUpdateAvailable</c>
    /// if one is found. Respects the configured update channel (release vs. beta).
    /// </summary>
    public async Task CheckForLauncherUpdatesAsync()
    {
        try
        {
            var launcherBranch = GetLauncherBranch();
            var isBetaChannel = launcherBranch == "beta";
            var installedBranch = GetInstalledLauncherBranchOrInit(launcherBranch);
            var isChannelSwitch = !string.Equals(installedBranch, launcherBranch, StringComparison.OrdinalIgnoreCase);
            
            // Get all releases (not just latest) to support beta channel
            var apiUrl = $"{GitHubApiUrl}?per_page=50";
            var json = await _httpClient.GetStringAsync(apiUrl);
            using var doc = JsonDocument.Parse(json);

            var currentVersion = GetLauncherVersion();
            string? bestVersion = null;
            JsonElement? bestRelease = null;

            foreach (var release in doc.RootElement.EnumerateArray())
            {
                var tagName = release.GetProperty("tag_name").GetString();
                if (string.IsNullOrWhiteSpace(tagName)) continue;

                // Check GitHub's native prerelease flag
                var isPrerelease = release.TryGetProperty("prerelease", out var prereleaseVal) && prereleaseVal.GetBoolean();

                // Match channel: beta channel gets prereleases, stable gets stable releases
                if (isBetaChannel && !isPrerelease) continue;
                if (!isBetaChannel && isPrerelease) continue;

                // Parse version from tag
                var version = ParseVersionFromTag(tagName);
                if (string.IsNullOrWhiteSpace(version)) continue;

                if (isChannelSwitch)
                {
                    // Channel switch: always offer the newest release in the selected channel,
                    // even if it is the same version or a downgrade.
                    bestVersion = version;
                    bestRelease = release;
                    break;
                }

                // Normal update flow: only newer versions
                if (IsNewerVersion(version, currentVersion))
                {
                    if (bestVersion == null || IsNewerVersion(version, bestVersion))
                    {
                        bestVersion = version;
                        bestRelease = release;
                    }
                }
            }

            if (bestRelease.HasValue && !string.IsNullOrWhiteSpace(bestVersion))
            {
                var release = bestRelease.Value;
                var reason = isChannelSwitch ? $"channel switch {installedBranch} -> {launcherBranch}" : "version update";
                Logger.Info("Update", $"Update available: {currentVersion} -> {bestVersion} ({reason})");
                
                // Pick the right asset for this platform
                string? downloadUrl = null;
                string? assetName = null;
                TryPickBestAssetForCurrentPlatform(release, out downloadUrl, out assetName);

                if (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(assetName))
                {
                    Logger.Warning("Update", $"Update found ({bestVersion}) but no compatible asset was found for this platform; skipping notification");
                    return;
                }

                var changelog = release.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;

                var updateInfo = new
                {
                    // Back-compat: keep both `version` and `latestVersion`
                    version = bestVersion,
                    latestVersion = bestVersion,
                    currentVersion = currentVersion,
                    changelog = changelog ?? string.Empty,
                    downloadUrl = downloadUrl,
                    assetName = assetName,
                    releaseUrl = release.GetProperty("html_url").GetString() ?? "",
                    isBeta = launcherBranch == "beta"
                };
                    
                LauncherUpdateAvailable?.Invoke(updateInfo);
            }
            else
            {
                Logger.Info("Update", $"Launcher is up to date: {currentVersion} (channel: {launcherBranch})");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Update", $"Error checking for updates: {ex.Message}");
        }
    }
    public async Task<bool> UpdateAsync(JsonElement[]? args)
    {
        string? downloadedUpdatePath = null;
        bool downloadCompleted = false;
        try
        {
            var launcherBranch = GetLauncherBranch();
            var isBetaChannel = launcherBranch == "beta";
            var currentVersion = GetLauncherVersion();
            var installedBranch = GetInstalledLauncherBranchOrInit(launcherBranch);
            var isChannelSwitch = !string.Equals(installedBranch, launcherBranch, StringComparison.OrdinalIgnoreCase);
            
            // Get all releases to find the best match for user's channel
            var apiUrl = $"{GitHubApiUrl}?per_page=50";
            var json = await _httpClient.GetStringAsync(apiUrl);
            using var doc = JsonDocument.Parse(json);
            
            // Find the best release for the user's channel
            JsonElement? targetRelease = null;
            string? targetVersion = null;
            
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                var isPrerelease = release.TryGetProperty("prerelease", out var prereleaseVal) && prereleaseVal.GetBoolean();
                
                // Match channel
                if (isBetaChannel && !isPrerelease) continue; // Beta wants prereleases
                if (!isBetaChannel && isPrerelease) continue; // Stable wants releases
                
                var tagName = release.GetProperty("tag_name").GetString();
                if (string.IsNullOrWhiteSpace(tagName)) continue;
                
                var version = ParseVersionFromTag(tagName);
                if (string.IsNullOrWhiteSpace(version)) continue;
                
                // Take the first matching release (they're sorted newest first)
                if (targetRelease == null)
                {
                    targetRelease = release;
                    targetVersion = version;
                    break;
                }
            }
            
            if (!targetRelease.HasValue || string.IsNullOrWhiteSpace(targetVersion))
            {
                Logger.Error("Update", $"No suitable {(isBetaChannel ? "pre-release" : "release")} found");
                return false;
            }
            
            Logger.Info("Update", $"Downloading {(isBetaChannel ? "pre-release" : "release")} {targetVersion} (current: {currentVersion})");

            if (!isChannelSwitch && !IsNewerVersion(targetVersion, currentVersion))
            {
                Logger.Info("Update", $"No update needed (current: {currentVersion}, latest: {targetVersion})");
                return false;
            }
            
            string? downloadUrl = null;
            string? assetName = null;
            TryPickBestAssetForCurrentPlatform(targetRelease.Value, out downloadUrl, out assetName);

            if (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(assetName))
            {
                Logger.Error("Update", "Could not find matching asset in latest release; opening releases page");
                return false;
            }

            var updateDir = TryGetDownloadsDirectory() ?? Path.Combine(Path.GetTempPath(), "HyPrism", "launcher-update");

            Directory.CreateDirectory(updateDir);
            var targetPath = Path.Combine(updateDir, assetName);
            downloadedUpdatePath = targetPath;

            Logger.Info("Update", $"Downloading latest launcher to {targetPath}");
            EmitLauncherUpdateProgress("download", 0, $"Downloading {assetName}...", 0, 0);
            using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0;
                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var file = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[8192];
                int read;
                long downloaded = 0;
                var lastReport = Stopwatch.StartNew();
                while ((read = await stream.ReadAsync(buffer)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read));

                    downloaded += read;
                    if (lastReport.ElapsedMilliseconds >= 150)
                    {
                        var pct = total > 0 ? (downloaded / (double)total) * 80.0 : 0.0;
                        EmitLauncherUpdateProgress("download", pct, "Downloading...", downloaded, total);
                        lastReport.Restart();
                    }
                }

                EmitLauncherUpdateProgress("download", 80, "Download complete", downloaded, total);
            }

            downloadCompleted = File.Exists(targetPath);

            // Platform-specific installation

            // Persist the installed channel before we hand off to the replacement script.
            // (The current process usually exits right after starting the updater.)
            try
            {
                _config.InstalledLauncherBranch = launcherBranch;
                _configService.SaveConfig();
            }
            catch
            {
                // Non-fatal
            }

            EmitLauncherUpdateProgress("install", 85, "Installing...", 0, 0);
            await InstallUpdateAsync(targetPath);

            EmitLauncherUpdateProgress("install", 100, "Restarting launcher...", 0, 0);
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Update", $"Update failed: {ex.Message}");
            var hasFile = downloadCompleted && !string.IsNullOrWhiteSpace(downloadedUpdatePath) && File.Exists(downloadedUpdatePath);
            EmitLauncherUpdateProgress(
                "error",
                0,
                ex.Message,
                0,
                0,
                downloadedUpdatePath,
                hasFile);
            return false;
        }
    }

    /// <summary>
    /// Forces a reset of the stored latest instance version for the given branch,
    /// triggering a game update check on next launch.
    /// </summary>
    /// <param name="branch">The branch whose latest version entry should be reset (e.g. <c>"release"</c>).</param>
    /// <returns><c>true</c> if the reset succeeded; <c>false</c> if no versions are available for the branch.</returns>
    public async Task<bool> ForceUpdateLatestAsync(string branch)
    {
        try
        {
            var normalizedBranch = UtilityService.NormalizeVersionType(branch);
            var versions = await _versionService.GetVersionListAsync(normalizedBranch);
            if (versions.Count == 0) return false;

            var info = _instanceService.LoadLatestInfo(normalizedBranch);
            
            if (info == null)
            {
                // No version info, assume version 1 to force full update path
                _instanceService.SaveLatestInfo(normalizedBranch, 1);
                Logger.Info("Update", $"No version info found, set to v1 to force update");
            }
            else
            {
                // Set installed version to one less than latest to trigger update
                int latestVersion = versions[0];
                if (info.Version < latestVersion)
                {
                    // Already behind, just return true
                    Logger.Info("Update", $"Already needs update: v{info.Version} -> v{latestVersion}");
                    return true;
                }
                // If somehow at or ahead of latest, force update by going back one version
                int forcedVersion = Math.Max(1, latestVersion - 1);
                _instanceService.SaveLatestInfo(normalizedBranch, forcedVersion);
                Logger.Info("Update", $"Forced version to v{forcedVersion} to trigger update to v{latestVersion}");
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Update", $"Failed to force update: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Duplicates the current latest instance as a versioned instance.
    /// Creates a copy with the current version number.
    /// </summary>
    public async Task<bool> DuplicateLatestAsync(string branch)
    {
        try
        {
            var normalizedBranch = UtilityService.NormalizeVersionType(branch);
            var info = _instanceService.LoadLatestInfo(normalizedBranch);
            
            if (info == null)
            {
                Logger.Warning("Update", "Cannot duplicate latest: no version info found");
                return false;
            }
            
            var currentVersion = info.Version;
            var latestPath = GetLatestInstancePath();
            
            if (!_instanceService.IsClientPresent(latestPath))
            {
                Logger.Warning("Update", "Cannot duplicate latest: instance not found");
                return false;
            }
            
            // Get versioned instance path
            var versionedPath = _instanceService.ResolveInstancePath(normalizedBranch, currentVersion, true);
            
            // Check if this version already exists
            if (_instanceService.IsClientPresent(versionedPath))
            {
                Logger.Warning("Update", $"Version {currentVersion} already exists, skipping duplicate");
                return false;
            }
            
            // Copy the entire latest instance folder to versioned folder
            Logger.Info("Update", $"Duplicating latest (v{currentVersion}) to versioned instance...");
            UtilityService.CopyDirectory(latestPath, versionedPath);
            
            // Save version info for the duplicated instance
            var versionInfoPath = Path.Combine(versionedPath, "version.json");
            var versionInfo = new { Version = currentVersion, Branch = normalizedBranch };
            File.WriteAllText(versionInfoPath, System.Text.Json.JsonSerializer.Serialize(versionInfo));
            
            Logger.Success("Update", $"Duplicated latest to versioned instance v{currentVersion}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Update", $"Failed to duplicate latest: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Platform-Specific Installation

    private async Task InstallUpdateAsync(string targetPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (targetPath.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
                await InstallMacOSUpdateAsync(targetPath);
            else if (targetPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                await InstallMacOSZipUpdateAsync(targetPath);
            else
                await InstallMacOSBinaryUpdateAsync(targetPath);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (targetPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                EmitLauncherUpdateProgress("install", 90, "Extracting update...", 0, 0);
                var extractedExe = ExtractZipAndFindWindowsExe(targetPath);
                if (string.IsNullOrWhiteSpace(extractedExe))
                    throw new Exception("Could not find .exe inside update .zip");
                var sourceDir = Path.GetDirectoryName(extractedExe);
                InstallWindowsUpdate(extractedExe, sourceDir);
            }
            else
            {
                InstallWindowsUpdate(targetPath, Path.GetDirectoryName(targetPath));
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            InstallLinuxUpdate(targetPath);
        }
    }

    private async Task InstallMacOSBinaryUpdateAsync(string newBinaryPath)
    {
        try
        {
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe))
                throw new Exception("Could not determine current executable path");

            // Replace just the executable and restart the app. This is intended for update assets
            // that are direct binaries (not DMG installers).
            var updateScript = Path.Combine(Path.GetTempPath(), "hyprism_update.sh");
            var scriptContent = $@"#!/bin/bash
sleep 2
chmod +x ""{newBinaryPath}"" 2>/dev/null || true
rm -f ""{currentExe}""
cp -f ""{newBinaryPath}"" ""{currentExe}""
chmod +x ""{currentExe}"" 2>/dev/null || true
""{currentExe}"" &
rm -f ""$0""
";

            File.WriteAllText(updateScript, scriptContent);
            Process.Start("chmod", $"+x \"{updateScript}\"")?.WaitForExit();

            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"\"{updateScript}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });

            Logger.Info("Update", "Update script started");
        }
        catch (Exception ex)
        {
            Logger.Error("Update", $"Auto-update (binary) failed: {ex.Message}");
            throw;
        }
    }

    private async Task InstallMacOSZipUpdateAsync(string zipPath)
    {
        EmitLauncherUpdateProgress("install", 90, "Extracting update...", 0, 0);
        var extractDir = Path.Combine(Path.GetTempPath(), "HyPrism", "launcher-update", "extract", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractDir);
        ZipFile.ExtractToDirectory(zipPath, extractDir, true);

        // Prefer .app bundles if present
        var appCandidate = Directory.GetDirectories(extractDir, "*.app", SearchOption.AllDirectories).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(appCandidate) && Directory.Exists(appCandidate))
        {
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe))
                throw new Exception("Could not determine current executable path");

            var currentAppPath = currentExe;
            for (int i = 0; i < 3; i++)
            {
                currentAppPath = Path.GetDirectoryName(currentAppPath);
                if (string.IsNullOrEmpty(currentAppPath)) break;
            }

            if (string.IsNullOrEmpty(currentAppPath) || !currentAppPath.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                throw new Exception($"Could not determine .app path from: {currentExe}");

            var updateScript = Path.Combine(Path.GetTempPath(), "hyprism_update.sh");
            var scriptContent = $@"#!/bin/bash
sleep 2
rm -rf ""{currentAppPath}""
cp -R ""{appCandidate}"" ""{currentAppPath}""
open ""{currentAppPath}""
rm -f ""$0""
";
            File.WriteAllText(updateScript, scriptContent);
            Process.Start("chmod", $"+x \"{updateScript}\"")?.WaitForExit();
            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"\"{updateScript}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            Logger.Info("Update", "Update script started");
            return;
        }

        // Fallback: raw executable inside zip
        var raw = FindExecutableFile(extractDir);
        if (string.IsNullOrWhiteSpace(raw))
            throw new Exception("Could not find executable inside update .zip");

        await InstallMacOSBinaryUpdateAsync(raw);
    }

    private static void TryPickBestAssetForCurrentPlatform(JsonElement release, out string? downloadUrl, out string? assetName)
    {
        downloadUrl = null;
        assetName = null;

        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return;

        static int ScoreAsset(string name)
        {
            // Higher is better.
            var lower = name.ToLowerInvariant();

            // Avoid installer-ish Windows assets when we can.
            var looksLikeInstaller = lower.Contains("setup") || lower.Contains("installer") || lower.Contains("install");

            // Prefer matching current architecture if present in the name.
            var arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                _ => ""
            };
            var hasArchHint = !string.IsNullOrWhiteSpace(arch) && lower.Contains(arch);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (lower.EndsWith(".dmg")) return 300 + (hasArchHint ? 10 : 0);
                if (lower.EndsWith(".zip")) return 250 + (hasArchHint ? 10 : 0);
                if (lower.EndsWith(".tar.gz")) return 200 + (hasArchHint ? 10 : 0);
                return 0;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Prefer portable archives first.
                if (lower.EndsWith(".zip")) return 300 + (hasArchHint ? 10 : 0);
                if (lower.EndsWith(".exe")) return looksLikeInstaller ? 0 : (200 + (hasArchHint ? 10 : 0));
                return 0;
            }

            // Linux
            if (lower.EndsWith(".appimage")) return 300 + (hasArchHint ? 10 : 0);
            if (lower.EndsWith(".tar.gz")) return 250 + (hasArchHint ? 10 : 0);
            if (lower.EndsWith(".zip")) return 200 + (hasArchHint ? 10 : 0);
            return 0;
        }

        (string name, string url, int score)? best = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!asset.TryGetProperty("browser_download_url", out var urlEl)) continue;
            var url = urlEl.GetString();
            if (string.IsNullOrWhiteSpace(url)) continue;

            var score = ScoreAsset(name);
            if (score <= 0) continue;

            if (best == null || score > best.Value.score)
                best = (name, url, score);
        }

        if (best != null)
        {
            assetName = best.Value.name;
            downloadUrl = best.Value.url;
            return;
        }

        // 2) Fallback: first asset with a download URL
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var urlEl) ? urlEl.GetString() : null;
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var lower = name.ToLowerInvariant();
                    var looksLikeInstaller = lower.Contains("setup") || lower.Contains("installer") || lower.Contains("install");
                    if (looksLikeInstaller && lower.EndsWith(".exe"))
                        continue;
                }
                assetName = name;
                downloadUrl = url;
                return;
            }
        }
    }

    private async Task InstallMacOSUpdateAsync(string dmgPath)
    {
        try
        {
            Logger.Info("Update", "Mounting DMG and installing...");
            
            // Mount the DMG
            var mountProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "hdiutil",
                Arguments = $"attach \"{dmgPath}\" -nobrowse -readonly",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            
            if (mountProcess == null)
            {
                throw new Exception("Failed to mount DMG");
            }
            
            await mountProcess.WaitForExitAsync();
            var mountOutput = await mountProcess.StandardOutput.ReadToEndAsync();
            
            // Parse mount point from hdiutil output (last line, last column)
            var mountPoint = mountOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()?
                .Split('\t', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()?
                .Trim();
            
            if (string.IsNullOrWhiteSpace(mountPoint) || !Directory.Exists(mountPoint))
            {
                throw new Exception($"Could not find mount point. Output: {mountOutput}");
            }

            EmitLauncherUpdateProgress("install", 90, "Extracting update...", 0, 0);
            Logger.Info("Update", $"DMG mounted at: {mountPoint}");
            
            // Find the .app in the mounted DMG
            var appInDmg = Directory.GetDirectories(mountPoint, "*.app").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(appInDmg) || !Directory.Exists(appInDmg))
            {
                Process.Start("hdiutil", $"detach \"{mountPoint}\" -force");
                throw new Exception("No .app found in DMG");
            }

            
            // Get current app path
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe))
            {
                Process.Start("hdiutil", $"detach \"{mountPoint}\" -force");
                throw new Exception("Could not determine current executable path");
            }
            
            // Navigate up to get the .app bundle path
            var currentAppPath = currentExe;
            for (int i = 0; i < 3; i++) // Go up 3 levels to get to .app
            {
                currentAppPath = Path.GetDirectoryName(currentAppPath);
                if (string.IsNullOrEmpty(currentAppPath)) break;
            }
            
            if (string.IsNullOrEmpty(currentAppPath) || !currentAppPath.EndsWith(".app"))
            {
                Process.Start("hdiutil", $"detach \"{mountPoint}\" -force");
                throw new Exception($"Could not determine .app path from: {currentExe}");
            }
            
            Logger.Info("Update", $"Current app: {currentAppPath}");
            Logger.Info("Update", $"New app: {appInDmg}");
            
            // Create update script to replace app and restart
            var updateScript = Path.Combine(Path.GetTempPath(), "hyprism_update.sh");
            var scriptContent = $@"#!/bin/bash
sleep 2
rm -rf ""{currentAppPath}""
cp -R ""{appInDmg}"" ""{currentAppPath}""
hdiutil detach ""{mountPoint}"" -force
open ""{currentAppPath}""
rm -f ""$0""
";
            
            File.WriteAllText(updateScript, scriptContent);
            Process.Start("chmod", $"+x \"{updateScript}\"")?.WaitForExit();
            
            // Start the update script and exit
            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"\"{updateScript}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            
            Logger.Info("Update", "Update script started");
        }
        catch (Exception ex)
        {
            Logger.Error("Update", $"Auto-update failed: {ex.Message}");
            try { Process.Start("open", dmgPath); } catch { }
            throw new Exception($"Please install the update manually from Downloads. {ex.Message}");
        }
    }

    private void InstallWindowsUpdate(string exePath, string? sourceDir)
    {
        try
        {
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe))
            {
                Logger.Error("Update", "Could not determine current executable path");
                Process.Start("explorer.exe", $"/select,\"{exePath}\"");
                return;
            }

            var targetDir = Path.GetDirectoryName(currentExe);
            if (string.IsNullOrWhiteSpace(targetDir))
            {
                Logger.Error("Update", "Could not determine current install directory");
                Process.Start("explorer.exe", $"/select,\"{exePath}\"");
                return;
            }
            var safeSourceDir = string.IsNullOrWhiteSpace(sourceDir) ? Path.GetDirectoryName(exePath) : sourceDir;
            if (string.IsNullOrWhiteSpace(safeSourceDir) || !Directory.Exists(safeSourceDir))
                safeSourceDir = null;

            // Create a batch script to replace the exe and restart
            var batchPath = Path.Combine(Path.GetTempPath(), "hyprism_update.bat");
                        var safeSourceDirValue = safeSourceDir ?? string.Empty;
                        var batchContent = $$"""
@echo off
timeout /t 2 /nobreak >nul
set "DST={{targetDir}}"
set "SRC={{safeSourceDirValue}}"

if not "%SRC%"=="" (
    xcopy /E /I /Y /Q "%SRC%\*" "%DST%" >nul
)

del "{{currentExe}}" 2>nul
copy /y "{{exePath}}" "{{currentExe}}" >nul

rem Ensure ffmpeg.dll is present next to the launcher exe (some packages keep it deeper).
if not "%SRC%"=="" (
    setlocal EnableDelayedExpansion
    set "FF="
    for /r "%SRC%" %%F in (ffmpeg.dll) do (
        set "FF=%%F"
        goto :_fffound
    )
    :_fffound
    if not "!FF!"=="" copy /y "!FF!" "%DST%\ffmpeg.dll" >nul
    endlocal
)

start "" "{{currentExe}}"
del "%~f0"
""";
            File.WriteAllText(batchPath, batchContent);

            // Start the batch script and exit
            var psi = new ProcessStartInfo
            {
                FileName = batchPath,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);

            Logger.Info("Update", "Starting update script");
        }
        catch (Exception ex)
        {
            Logger.Warning("Update", $"Auto-update failed, opening Explorer: {ex.Message}");
            Process.Start("explorer.exe", $"/select,\"{exePath}\"");
        }
    }

    private void InstallLinuxUpdate(string targetPath)
    {
        try
        {
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe))
            {
                throw new Exception("Could not determine current executable path");
            }

            // Accept archives by extracting and then installing the extracted payload.
            if (targetPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var extractDir = Path.Combine(Path.GetTempPath(), "HyPrism", "launcher-update", "extract", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(extractDir);
                ZipFile.ExtractToDirectory(targetPath, extractDir, true);
                var candidate = Directory.GetFiles(extractDir, "*.AppImage", SearchOption.AllDirectories).FirstOrDefault() ?? FindExecutableFile(extractDir);
                if (string.IsNullOrWhiteSpace(candidate))
                    throw new Exception("Could not find executable inside update archive");
                InstallLinuxUpdate(candidate);
                return;
            }

            if (targetPath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                var extractDir = Path.Combine(Path.GetTempPath(), "HyPrism", "launcher-update", "extract", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(extractDir);
                ExtractTarGz(targetPath, extractDir).GetAwaiter().GetResult();
                var candidate = Directory.GetFiles(extractDir, "*.AppImage", SearchOption.AllDirectories).FirstOrDefault() ?? FindExecutableFile(extractDir);
                if (string.IsNullOrWhiteSpace(candidate))
                    throw new Exception("Could not find executable inside update archive");
                InstallLinuxUpdate(candidate);
                return;
            }
            
            // For AppImage, just replace the file
            if (targetPath.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
            {
                // Make the new AppImage executable
                Process.Start("chmod", $"+x \"{targetPath}\"")?.WaitForExit();
                
                // Create update script
                var updateScript = Path.Combine(Path.GetTempPath(), "hyprism_update.sh");
                var scriptContent = $@"#!/bin/bash
sleep 2
rm -f ""{currentExe}""
cp -f ""{targetPath}"" ""{currentExe}""
chmod +x ""{currentExe}""
""{currentExe}"" &
rm -f ""$0""
";
                File.WriteAllText(updateScript, scriptContent);
                Process.Start("chmod", $"+x \"{updateScript}\"")?.WaitForExit();
                
                // Start the update script and exit
                Process.Start(new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"\"{updateScript}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                
                Logger.Info("Update", "Update script started");
            }
            else
            {
                // Treat as a raw executable: replace and restart.
                Process.Start("chmod", $"+x \"{targetPath}\"")?.WaitForExit();

                var updateScript = Path.Combine(Path.GetTempPath(), "hyprism_update.sh");
                var scriptContent = $@"#!/bin/bash
sleep 2
rm -f ""{currentExe}""
cp -f ""{targetPath}"" ""{currentExe}""
chmod +x ""{currentExe}"" 2>/dev/null || true
""{currentExe}"" &
rm -f ""$0""
";
                File.WriteAllText(updateScript, scriptContent);
                Process.Start("chmod", $"+x \"{updateScript}\"")?.WaitForExit();
                Process.Start(new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"\"{updateScript}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                Logger.Info("Update", "Update script started");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Update", $"Auto-update failed: {ex.Message}");
            try { Process.Start("xdg-open", targetPath); } catch { }
            throw new Exception($"Please install the update manually from Downloads. {ex.Message}");
        }
    }

    #endregion

    #region Version Parsing

    private static string? ParseVersionFromTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        
        var tagLower = tag.ToLowerInvariant();
        
        // Handle beta format: "beta3-3.0.0" or "beta-3.0.0"
        if (tagLower.StartsWith("beta"))
        {
            var dashIndex = tag.IndexOf('-');
            if (dashIndex >= 0 && dashIndex < tag.Length - 1)
            {
                var versionPart = tag.Substring(dashIndex + 1).TrimStart('v', 'V');
                return versionPart;
            }
            // beta without dash, try to extract version after "beta" text
            var afterBeta = tag.Substring(4).TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').TrimStart('-', '_').TrimStart('v', 'V');
            return string.IsNullOrWhiteSpace(afterBeta) ? null : afterBeta;
        }
        
        // Handle standard format: "v2.0.1" or "2.0.1"
        return tag.TrimStart('v', 'V');
    }

    private static bool IsNewerVersion(string remote, string current)
    {
        // Parse versions like "2.0.1" into comparable parts
        var remoteParts = remote.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var currentParts = current.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();

        for (int i = 0; i < Math.Max(remoteParts.Length, currentParts.Length); i++)
        {
            var r = i < remoteParts.Length ? remoteParts[i] : 0;
            var c = i < currentParts.Length ? currentParts[i] : 0;
            if (r > c) return true;
            if (r < c) return false;
        }
        return false;
    }

    #endregion

    #region Wrapper Mode

    /// <summary>
    /// Wrapper Mode: Get status of the installed HyPrism binary and check for updates.
    /// Returns: { installed: bool, version: string, needsUpdate: bool, latestVersion: string }
    /// </summary>
    public async Task<Dictionary<string, object>> WrapperGetStatus()
    {
        var result = new Dictionary<string, object>
        {
            ["installed"] = false,
            ["version"] = "",
            ["needsUpdate"] = false,
            ["latestVersion"] = ""
        };

        try
        {
            var wrapperDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HyPrism", "wrapper");
            var binaryPath = Path.Combine(wrapperDir, "HyPrism");
            var versionFile = Path.Combine(wrapperDir, "version.txt");

            if (!File.Exists(binaryPath))
            {
                return result;
            }

            result["installed"] = true;

            if (File.Exists(versionFile))
            {
                result["version"] = (await File.ReadAllTextAsync(versionFile)).Trim();
            }

            // Check GitHub for latest release
            var latestVersion = await GetLatestLauncherVersionFromGitHub();
            if (!string.IsNullOrEmpty(latestVersion))
            {
                result["latestVersion"] = latestVersion;
                result["needsUpdate"] = result["version"].ToString() != latestVersion;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WrapperGetStatus error: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Wrapper Mode: Install or update the latest HyPrism binary from GitHub releases.
    /// Downloads the appropriate release for the current OS and extracts it to wrapper directory.
    /// </summary>
    public async Task<bool> WrapperInstallLatest()
    {
        try
        {
            var wrapperDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HyPrism", "wrapper");
            Directory.CreateDirectory(wrapperDir);

            // Get latest release from GitHub
            var latestVersion = await GetLatestLauncherVersionFromGitHub();
            if (string.IsNullOrEmpty(latestVersion))
            {
                Console.WriteLine("Failed to get latest version from GitHub");
                return false;
            }

            // Determine the asset name based on OS
            string assetName;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                assetName = $"HyPrism-{latestVersion}-linux-x64.tar.gz";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                assetName = $"HyPrism-{latestVersion}-win-x64.zip";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                assetName = $"HyPrism-{latestVersion}-osx-x64.tar.gz";
            }
            else
            {
                Console.WriteLine($"Unsupported platform: {RuntimeInformation.OSDescription}");
                return false;
            }

            var downloadUrl = $"https://github.com/yyyumeniku/HyPrism/releases/download/{latestVersion}/{assetName}";
            var archivePath = Path.Combine(wrapperDir, assetName);

            // Download archive
            _progressNotificationService.ReportDownloadProgress("wrapper-install", 0, "Downloading HyPrism...", null, 0, 100);
            
            var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to download: {response.StatusCode}");
                return false;
            }

            await using (var contentStream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = File.Create(archivePath))
            {
                await contentStream.CopyToAsync(fileStream);
            }

            _progressNotificationService.ReportDownloadProgress("wrapper-install", 50, "Extracting...", null, 50, 100);

            // Extract archive
            if (assetName.EndsWith(".tar.gz"))
            {
                await ExtractTarGz(archivePath, wrapperDir);
            }
            else if (assetName.EndsWith(".zip"))
            {
                ZipFile.ExtractToDirectory(archivePath, wrapperDir, true);
            }

            // Set executable permission on Linux/Mac
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var binaryPath = Path.Combine(wrapperDir, "HyPrism");
                if (File.Exists(binaryPath))
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "chmod",
                            Arguments = $"+x \"{binaryPath}\"",
                            UseShellExecute = false
                        }
                    };
                    process.Start();
                    await process.WaitForExitAsync();
                }
            }

            // Save version
            await File.WriteAllTextAsync(Path.Combine(wrapperDir, "version.txt"), latestVersion);

            // Cleanup archive
            File.Delete(archivePath);

            _progressNotificationService.ReportDownloadProgress("wrapper-install", 100, "Installation complete", null, 100, 100);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WrapperInstallLatest error: {ex.Message}");
            _progressNotificationService.ReportError("Wrapper Installation Error", ex.Message, null);
            return false;
        }
    }

    /// <summary>
    /// Wrapper Mode: Launch the installed HyPrism binary.
    /// </summary>
    public async Task<bool> WrapperLaunch()
    {
        try
        {
            var wrapperDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HyPrism", "wrapper");
            var binaryPath = Path.Combine(wrapperDir, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "HyPrism.exe" : "HyPrism");

            if (!File.Exists(binaryPath))
            {
                Console.WriteLine("HyPrism binary not found");
                return false;
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = binaryPath,
                    UseShellExecute = true,
                    WorkingDirectory = wrapperDir
                }
            };

            process.Start();
            await Task.CompletedTask;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WrapperLaunch error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Helper: Get latest launcher version from GitHub releases API.
    /// </summary>
    private async Task<string> GetLatestLauncherVersionFromGitHub()
    {
        try
        {
            var response = await _httpClient.GetStringAsync("https://api.github.com/repos/yyyumeniku/HyPrism/releases/latest");
            var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("tag_name", out var tagName))
            {
                return tagName.GetString() ?? "";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get latest version: {ex.Message}");
        }
        return "";
    }

    #endregion
}
