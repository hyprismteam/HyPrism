using System.Runtime.InteropServices;
using System.Net;
using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Core.App;
using HyPrism.Services.Game.Butler;
using HyPrism.Services.Game.Download;
using HyPrism.Services.Game.Instance;
using HyPrism.Services.Game.Launch;
using HyPrism.Services.Game.Version;

namespace HyPrism.Services.Game;

/// <summary>
/// Orchestrates the complete game download, update, and launch workflow.
/// Acts as the primary coordinator between version checking, patching, and game launching.
/// </summary>
/// <remarks>
/// This service was refactored from a ~1000 line monolithic class into a coordinator
/// that delegates to specialized services like IPatchManager and IGameLauncher.
/// </remarks>
public class GameSessionService : IGameSessionService
{
    private const long MinValidPwrBytes = 1_048_576; // 1 MB

    private readonly IConfigService _configService;
    private readonly IInstanceService _instanceService;
    private readonly IVersionService _versionService;
    private readonly ILaunchService _launchService;
    private readonly IButlerService _butlerService;
    private readonly IDownloadService _downloadService;
    private readonly IProgressNotificationService _progressService;
    private readonly IPatchManager _patchManager;
    private readonly IGameLauncher _gameLauncher;
    private readonly HttpClient _httpClient;
    private readonly string _appDir;
    
    private volatile bool _cancelRequested;
    private CancellationTokenSource? _downloadCts;
    private readonly object _ctsLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="GameSessionService"/> class.
    /// </summary>
    /// <param name="configService">Service for accessing configuration.</param>
    /// <param name="instanceService">Service for managing game instances.</param>
    /// <param name="versionService">Service for version checking.</param>
    /// <param name="launchService">Service for launch prerequisites (JRE, VC++).</param>
    /// <param name="butlerService">Service for Butler patch tool.</param>
    /// <param name="downloadService">Service for file downloads.</param>
    /// <param name="progressService">Service for progress notifications.</param>
    /// <param name="patchManager">Manager for differential updates.</param>
    /// <param name="gameLauncher">Launcher for the game process.</param>
    /// <param name="httpClient">HTTP client for network requests.</param>
    /// <param name="appPath">Application path configuration.</param>
    public GameSessionService(
        IConfigService configService,
        IInstanceService instanceService,
        IVersionService versionService,
        ILaunchService launchService,
        IButlerService butlerService,
        IDownloadService downloadService,
        IProgressNotificationService progressService,
        IPatchManager patchManager,
        IGameLauncher gameLauncher,
        HttpClient httpClient,
        AppPathConfiguration appPath)
    {
        _configService = configService;
        _instanceService = instanceService;
        _versionService = versionService;
        _launchService = launchService;
        _butlerService = butlerService;
        _downloadService = downloadService;
        _progressService = progressService;
        _patchManager = patchManager;
        _gameLauncher = gameLauncher;
        _httpClient = httpClient;
        _appDir = appPath.AppDir;
    }

    private Config _config => _configService.Configuration;

    /// <inheritdoc/>
    public async Task<DownloadProgress> DownloadAndLaunchAsync(Func<bool>? launchAfterDownloadProvider = null)
    {
        CancellationTokenSource cts;
        lock (_ctsLock)
        {
            if (_cancelRequested)
            {
                _cancelRequested = false;
                return new DownloadProgress { Cancelled = true };
            }
            cts = new CancellationTokenSource();
            _downloadCts = cts;
        }

        try
        {
            _progressService.ReportDownloadProgress("preparing", 0, "launch.detail.preparing_session", null, 0, 0);

            var selectedInstance = _instanceService.GetSelectedInstance();
            if (selectedInstance == null)
            {
                Logger.Error("Download", "No instance selected — cannot launch. Select an instance first.");
                return new DownloadProgress { Error = "No instance selected" };
            }

            var branch = UtilityService.NormalizeVersionType(selectedInstance.Branch);
            var isLatestInstance = selectedInstance.Version == 0;
            var targetVersion = selectedInstance.Version;

            // Resolve instance path strictly by ID. Create the directory if it does not exist yet
            // (first-time launch before any files have been downloaded).
            var versionPath = _instanceService.GetInstancePathById(selectedInstance.Id)
                ?? _instanceService.CreateInstanceDirectory(branch, selectedInstance.Id);

            Logger.Info("Download", $"Using instance path: {selectedInstance.Id} -> {versionPath}", false);

            Directory.CreateDirectory(versionPath);

            bool gameIsInstalled = _instanceService.IsClientPresent(versionPath);

            // OPTIMIZATION: If game is already installed and this is NOT a "latest" instance,
            // skip version fetching entirely — no network calls needed, just launch.
            if (gameIsInstalled && !isLatestInstance && targetVersion > 0)
            {
                Logger.Success("Download", $"Fast path: Game already installed at v{targetVersion}, skipping version check");
                return await HandleInstalledGameFastAsync(versionPath, branch, cts.Token);
            }

            // For "latest" instances or fresh installs, we need to fetch version list
            _progressService.ReportDownloadProgress("preparing", 1, "launch.detail.checking_versions", null, 0, 0);
            var versions = await _versionService.GetVersionListAsync(branch, cts.Token);
            cts.Token.ThrowIfCancellationRequested();

            if (versions.Count == 0)
                return new DownloadProgress { Error = "No versions available for this branch" };

            // Resolve targetVersion from versions list
            if (targetVersion <= 0 || !versions.Contains(targetVersion))
                targetVersion = versions[0];

            Logger.Info("Download", $"=== INSTALL CHECK ===", false);
            Logger.Info("Download", $"Version path: {versionPath}", false);
            Logger.Info("Download", $"Is latest instance: {isLatestInstance}", false);
            Logger.Info("Download", $"Target version: {targetVersion}", false);
            Logger.Info("Download", $"Client exists (game installed): {gameIsInstalled}", false);

            // Check for interrupted install/patch: if PendingVersion is set,
            // a previous download or patch was interrupted and needs to be resumed.
            var instanceMeta = _instanceService.GetInstanceMeta(versionPath);
            if (instanceMeta != null && instanceMeta.PendingVersion > 0)
            {
                Logger.Warning("Download", $"Detected interrupted install: PendingVersion={instanceMeta.PendingVersion}, InstalledVersion={instanceMeta.InstalledVersion}");

                if (gameIsInstalled && instanceMeta.InstalledVersion > 0 && instanceMeta.InstalledVersion < instanceMeta.PendingVersion)
                {
                    // Game is partially patched — resume differential update
                    Logger.Info("Download", $"Resuming differential update from v{instanceMeta.InstalledVersion} to v{instanceMeta.PendingVersion}");
                    try
                    {
                        await _patchManager.ApplyDifferentialUpdateAsync(
                            versionPath, branch, instanceMeta.InstalledVersion, instanceMeta.PendingVersion, cts.Token);
                        return await CompleteInstallAsync(versionPath, branch, isLatestInstance, instanceMeta.PendingVersion, launchAfterDownloadProvider, cts.Token);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        Logger.Warning("Download", $"Resume patching failed: {ex.Message}, falling through to normal flow");
                    }
                }
                else if (!gameIsInstalled)
                {
                    // Client missing — full re-install needed, PendingVersion carries forward
                    Logger.Info("Download", "Client not present despite PendingVersion, will re-install");
                }
            }

            // Set PendingVersion before starting install/patch
            if (instanceMeta != null)
            {
                instanceMeta.PendingVersion = targetVersion;
                _instanceService.SaveInstanceMeta(versionPath, instanceMeta);
            }

            if (gameIsInstalled)
            {
                return await HandleInstalledGameAsync(versionPath, branch, isLatestInstance, versions, cts.Token);
            }

            return await HandleFreshInstallAsync(versionPath, branch, isLatestInstance, targetVersion, launchAfterDownloadProvider, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Logger.Warning("Download", "Operation cancelled");
            return new DownloadProgress { Error = "Cancelled" };
        }
        catch (Exception ex)
        {
            Logger.Error("Download", $"Fatal error: {ex.Message}");
            Logger.Error("Download", ex.ToString());
            _progressService.ReportError("fatal", "Fatal error", ex.ToString());
            return new DownloadProgress { Error = $"Fatal error: {ex.Message}" };
        }
        finally
        {
            lock (_ctsLock)
            {
                _downloadCts = null;
                _cancelRequested = false;
            }
            cts.Dispose();
        }
    }

    public void CancelDownload()
    {
        _cancelRequested = true;
        lock (_ctsLock)
        {
            _downloadCts?.Cancel();
        }
    }

    public void Dispose()
    {
        lock (_ctsLock)
        {
            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    /// <summary>
    /// Fast path for launching an already-installed game with a specific version (not "latest").
    /// Skips version list fetching entirely — no network calls needed.
    /// </summary>
    private async Task<DownloadProgress> HandleInstalledGameFastAsync(
        string versionPath, string branch, CancellationToken ct)
    {
        Logger.Success("Download", "Fast path: Game is already installed, skipping version check");

        await EnsureRuntimeDependenciesAsync(ct);

        _progressService.ReportDownloadProgress("complete", 100, "launch.detail.launching_game", null, 0, 0);
        try
        {
            await _gameLauncher.LaunchGameAsync(versionPath, branch, ct);
            return new DownloadProgress { Success = true, Progress = 100 };
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Launch failed: {ex.Message}");
            _progressService.ReportError("launch", "Failed to launch game", ex.ToString());
            return new DownloadProgress { Error = $"Failed to launch game: {ex.Message}" };
        }
    }

    private async Task<DownloadProgress> HandleInstalledGameAsync(
        string versionPath, string branch, bool isLatestInstance,
        List<int> versions, CancellationToken ct)
    {
        Logger.Success("Download", "Game is already installed");

        // Check for differential updates (only for latest instance)
        if (isLatestInstance)
        {
            await TryApplyDifferentialUpdateAsync(versionPath, branch, versions, ct);
        }

        await EnsureRuntimeDependenciesAsync(ct);

        _progressService.ReportDownloadProgress("complete", 100, "launch.detail.launching_game", null, 0, 0);
        try
        {
            await _gameLauncher.LaunchGameAsync(versionPath, branch, ct);
            return new DownloadProgress { Success = true, Progress = 100 };
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Launch failed: {ex.Message}");
            _progressService.ReportError("launch", "Failed to launch game", ex.ToString());
            return new DownloadProgress { Error = $"Failed to launch game: {ex.Message}" };
        }
    }

    private async Task TryApplyDifferentialUpdateAsync(
        string versionPath, string branch, List<int> versions, CancellationToken ct)
    {
        var info = _instanceService.LoadLatestInfo(branch);
        int installedVersion = info?.Version ?? 0;
        int latestVersion = versions[0];

        // Detect installed version from cache if no latest.json
        if (installedVersion == 0)
        {
            installedVersion = DetectInstalledVersion(versionPath, branch);
        }

        Logger.Info("Download", $"Installed version: {installedVersion}, Latest version: {latestVersion}", false);

        if (installedVersion > 0 && installedVersion < latestVersion)
        {
            // Set PendingVersion so interrupted updates can be resumed
            var meta = _instanceService.GetInstanceMeta(versionPath);
            if (meta != null)
            {
                meta.PendingVersion = latestVersion;
                _instanceService.SaveInstanceMeta(versionPath, meta);
            }

            try
            {
                await _patchManager.ApplyDifferentialUpdateAsync(versionPath, branch, installedVersion, latestVersion, ct);

                // Update completed: clear PendingVersion, set InstalledVersion
                if (meta != null)
                {
                    meta.InstalledVersion = latestVersion;
                    meta.PendingVersion = 0;
                    _instanceService.SaveInstanceMeta(versionPath, meta);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Logger.Error("Download", $"Differential update failed: {ex.Message}");
                Logger.Warning("Download", "Keeping current version, user can try UPDATE again later");
            }
        }
        else if (installedVersion >= latestVersion)
        {
            Logger.Info("Download", "Already at latest version, no update needed", false);
            _instanceService.SaveLatestInfo(branch, latestVersion);
        }
    }

    private int DetectInstalledVersion(string versionPath, string branch)
    {
        var receiptPath = Path.Combine(versionPath, ".itch", "receipt.json.gz");
        if (!File.Exists(receiptPath)) return 0;

        var cacheDir = Path.Combine(_appDir, "Cache");
        if (!Directory.Exists(cacheDir)) return 0;

        var pwrFiles = Directory.GetFiles(cacheDir, $"{branch}_patch_*.pwr")
            .Concat(Directory.GetFiles(cacheDir, $"{branch}_*.pwr"))
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .SelectMany(n =>
            {
                var parts = n.Split('_');
                var vs = new List<int>();
                foreach (var part in parts)
                {
                    if (int.TryParse(part, out var v) && v > 0)
                        vs.Add(v);
                }
                return vs;
            })
            .OrderByDescending(v => v)
            .ToList();

        if (pwrFiles.Count > 0)
        {
            int detected = pwrFiles[0];
            Logger.Info("Download", $"Detected installed version from cache: v{detected}", false);
            _instanceService.SaveLatestInfo(branch, detected);
            return detected;
        }

        Logger.Info("Download", "Butler receipt exists but no version info, launching as-is", false);
        return 0;
    }

    private async Task<DownloadProgress> HandleFreshInstallAsync(
        string versionPath, string branch, bool isLatestInstance,
        int targetVersion, Func<bool>? launchAfterDownloadProvider, CancellationToken ct)
    {
        Logger.Info("Download", "Game not installed, starting download...");
        _progressService.ReportDownloadProgress("download", 1, "launch.detail.preparing_download", null, 0, 0);

        try
        {
            _progressService.ReportDownloadProgress("download", 2, "launch.detail.installing_butler", null, 0, 0);
            await _butlerService.EnsureButlerInstalledAsync((progress, message) =>
            {
                int mappedProgress = 2 + (int)(progress * 0.03);
                _progressService.ReportDownloadProgress("download", mappedProgress, message, null, 0, 0);
            });
        }
        catch (Exception ex)
        {
            Logger.Error("Download", $"Butler install failed: {ex.Message}");
            return new DownloadProgress { Error = $"Failed to install Butler: {ex.Message}" };
        }

        ct.ThrowIfCancellationRequested();

        bool officialDown = _versionService.IsOfficialServerDown(branch);
        string osName = UtilityService.GetOS();
        string arch = UtilityService.GetArch();
        string apiVersionType = UtilityService.NormalizeVersionType(branch);

        // Mirror + pre-release: diff-based branch requires applying the entire patch chain
        // from version 0 (empty) up to the target version sequentially.
        if (officialDown && _versionService.IsDiffBasedBranch(apiVersionType))
        {
            Logger.Info("Download", $"Mirror pre-release: installing via diff chain v0 -> v{targetVersion}");
            _progressService.ReportDownloadProgress("download", 5, "launch.detail.downloading_mirror", null, 0, 0);

            try
            {
                await _patchManager.ApplyDifferentialUpdateAsync(versionPath, branch, 0, targetVersion, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Logger.Error("Download", $"Mirror diff chain install failed: {ex.Message}");
                return new DownloadProgress { Error = $"Failed to install game from mirror: {ex.Message}" };
            }

            return await CompleteInstallAsync(versionPath, branch, isLatestInstance, targetVersion, launchAfterDownloadProvider, ct);
        }
        else
        {
            // Official server or mirror release: download a single full PWR and apply.
            // Get the download URL (will refresh cache if needed)
            string downloadUrl;
            CachedVersionEntry versionEntry;
            try
            {
                versionEntry = await _versionService.RefreshAndGetVersionEntryAsync(apiVersionType, targetVersion, ct);
                downloadUrl = versionEntry.PwrUrl;
            }
            catch (Exception ex)
            {
                Logger.Error("Download", $"Failed to get download URL: {ex.Message}");
                return new DownloadProgress { Error = $"Failed to get download URL for v{targetVersion}: {ex.Message}" };
            }
            
            bool hasOfficialUrl = !string.IsNullOrEmpty(versionEntry.PwrUrl) 
                && versionEntry.PwrUrl.Contains("game-patches.hytale.com") 
                && versionEntry.PwrUrl.Contains("verify=");
            
            string pwrPath = Path.Combine(_appDir, "Cache", $"{branch}_{(isLatestInstance ? "latest" : "version")}_{targetVersion}.pwr");

            Directory.CreateDirectory(Path.GetDirectoryName(pwrPath)!);

            // Determine if we should skip official and go straight to mirror
            bool skipOfficial = officialDown || !hasOfficialUrl;

            try
            {
                await DownloadPwrWithCachingAsync(downloadUrl, pwrPath, osName, arch, apiVersionType, targetVersion, skipOfficial, hasOfficialUrl, ct);
            }
            catch (MirrorDiffRequiredException)
            {
                // Pre-release official download failed, mirror requires diff-based approach
                Logger.Info("Download", $"Switching to mirror diff chain for pre-release v{targetVersion}");
                _progressService.ReportDownloadProgress("download", 5, "launch.detail.downloading_mirror", null, 0, 0);
                
                try
                {
                    await _patchManager.ApplyDifferentialUpdateAsync(versionPath, branch, 0, targetVersion, ct);
                    return await CompleteInstallAsync(versionPath, branch, isLatestInstance, targetVersion, launchAfterDownloadProvider, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Logger.Error("Download", $"Mirror diff chain install failed: {ex.Message}");
                    return new DownloadProgress { Error = $"Failed to install game from mirror: {ex.Message}" };
                }
            }
            catch (MirrorBootstrapRequiredException ex)
            {
                // Full pre-release file is unavailable/corrupted (often 0-byte placeholder).
                // Try previous full build + patch to target.
                if (!apiVersionType.Equals("pre-release", StringComparison.OrdinalIgnoreCase))
                {
                    return new DownloadProgress { Error = ex.Message };
                }

                Logger.Warning("Download", $"Mirror full pre-release v{targetVersion} unavailable ({ex.Message}). Trying previous full build + patch...");

                var installed = await TryInstallPreReleaseFromPreviousFullAsync(
                    versionPath,
                    branch,
                    apiVersionType,
                    osName,
                    arch,
                    targetVersion,
                    ct);

                if (!installed)
                {
                    return new DownloadProgress { Error = $"Failed to install pre-release v{targetVersion}: no valid base build + patch path found" };
                }

                return await CompleteInstallAsync(versionPath, branch, isLatestInstance, targetVersion, launchAfterDownloadProvider, ct);
            }

            // Extract PWR with Butler
            _progressService.ReportDownloadProgress("install", 65, "launch.detail.installing_butler_pwr", null, 0, 0);

            try
            {
                await _butlerService.ApplyPwrAsync(pwrPath, versionPath, (progress, message) =>
                {
                    int mappedProgress = 65 + (int)(progress * 0.20);
                    _progressService.ReportDownloadProgress("install", mappedProgress, message, null, 0, 0);
                }, ct);

                ct.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Logger.Error("Download", $"PWR extraction failed: {ex.Message}");
                return new DownloadProgress { Error = $"Failed to install game: {ex.Message}" };
            }
        }

        return await CompleteInstallAsync(versionPath, branch, isLatestInstance, targetVersion, launchAfterDownloadProvider, ct);
    }

    private async Task<DownloadProgress> CompleteInstallAsync(
        string versionPath,
        string branch,
        bool isLatestInstance,
        int targetVersion,
        Func<bool>? launchAfterDownloadProvider,
        CancellationToken ct)
    {
        if (isLatestInstance)
            _instanceService.SaveLatestInfo(branch, targetVersion);

        // Update instance meta: clear PendingVersion, set InstalledVersion
        var meta = _instanceService.GetInstanceMeta(versionPath);
        if (meta != null)
        {
            meta.InstalledVersion = targetVersion;
            meta.PendingVersion = 0;
            _instanceService.SaveInstanceMeta(versionPath, meta);
        }

        _progressService.ReportDownloadProgress("complete", 95, "launch.detail.download_complete", null, 0, 0);

        await EnsureRuntimeDependenciesAsync(ct);

        ct.ThrowIfCancellationRequested();

        var shouldLaunchAfterDownload = launchAfterDownloadProvider?.Invoke() ?? true;
        if (!shouldLaunchAfterDownload)
        {
            _progressService.ReportDownloadProgress("complete", 100, "launch.detail.done", null, 0, 0);
            return new DownloadProgress { Success = true, Progress = 100 };
        }

        _progressService.ReportDownloadProgress("complete", 100, "launch.detail.launching_game", null, 0, 0);

        try
        {
            await _gameLauncher.LaunchGameAsync(versionPath, branch, ct);

            var cacheDir = Path.Combine(_appDir, "Cache");
            if (Directory.Exists(cacheDir))
            {
                foreach (var file in Directory.GetFiles(cacheDir, $"{branch}_*.pwr"))
                    try { File.Delete(file); } catch { }
            }

            return new DownloadProgress { Success = true, Progress = 100 };
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Launch failed: {ex.Message}");
            _progressService.ReportError("launch", "Failed to launch game", ex.ToString());
            return new DownloadProgress { Error = $"Failed to launch game: {ex.Message}" };
        }
    }

    private async Task<bool> TryInstallPreReleaseFromPreviousFullAsync(
        string versionPath,
        string branch,
        string apiBranch,
        string os,
        string arch,
        int targetVersion,
        CancellationToken ct)
    {
        if (targetVersion <= 1)
            return false;

        for (int baseVersion = targetVersion - 1; baseVersion >= 1; baseVersion--)
        {
            var bootstrapPath = Path.Combine(_appDir, "Cache", $"{branch}_bootstrap_{baseVersion}.pwr");
            Directory.CreateDirectory(Path.GetDirectoryName(bootstrapPath)!);

            try
            {
                Logger.Info("Download", $"Trying fallback base v{baseVersion} for target v{targetVersion}");

                await DownloadPwrWithCachingAsync(
                    downloadUrl: string.Empty,
                    pwrPath: bootstrapPath,
                    os: os,
                    arch: arch,
                    branch: apiBranch,
                    version: baseVersion,
                    skipOfficial: true,
                    hasOfficialUrl: false,
                    ct: ct);

                _progressService.ReportDownloadProgress("install", 65, "launch.detail.installing_butler_pwr", null, 0, 0);

                await _butlerService.ApplyPwrAsync(bootstrapPath, versionPath, (progress, message) =>
                {
                    int mappedProgress = 65 + (int)(progress * 0.15);
                    _progressService.ReportDownloadProgress("install", mappedProgress, message, null, 0, 0);
                }, ct);

                await _patchManager.ApplyDifferentialUpdateAsync(versionPath, branch, baseVersion, targetVersion, ct);
                Logger.Success("Download", $"Installed pre-release via fallback path: full v{baseVersion} + patches to v{targetVersion}");
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (MirrorBootstrapRequiredException ex)
            {
                Logger.Warning("Download", $"Fallback base v{baseVersion} invalid: {ex.Message}");
            }
            catch (Exception ex)
            {
                Logger.Warning("Download", $"Fallback path failed for base v{baseVersion}: {ex.Message}");
            }
        }

        return false;
    }

    private async Task DownloadPwrWithCachingAsync(
        string downloadUrl, string pwrPath,
        string os, string arch, string branch, int version,
        bool skipOfficial, bool hasOfficialUrl, CancellationToken ct)
    {
        bool needDownload = true;
        long remoteSize = -1;

        // Only check remote size from official if we have a valid URL
        if (!skipOfficial && hasOfficialUrl)
        {
            try { remoteSize = await _downloadService.GetFileSizeAsync(downloadUrl, ct); }
            catch { /* Proceed to download anyway */ }
        }

        if (File.Exists(pwrPath))
        {
            if (remoteSize > 0)
            {
                long localSize = new FileInfo(pwrPath).Length;
                if (localSize == remoteSize && localSize >= MinValidPwrBytes)
                {
                    Logger.Info("Download", "Using cached PWR file.");
                    needDownload = false;
                }
                else
                {
                    Logger.Warning("Download", $"Cached file size mismatch ({localSize} vs {remoteSize}). Deleting.");
                    try { File.Delete(pwrPath); } catch { }
                }
            }
            else
            {
                long localSize = new FileInfo(pwrPath).Length;
                if (localSize >= MinValidPwrBytes)
                {
                    Logger.Info("Download", "Cannot verify remote size, using valid local cache entry.");
                    needDownload = false;
                }
                else
                {
                    Logger.Warning("Download", $"Cached PWR is too small ({localSize} bytes). Deleting and redownloading.");
                    try { File.Delete(pwrPath); } catch { }
                }
            }
        }

        if (needDownload)
        {
            string partPath = pwrPath + ".part";
            bool downloaded = false;

            // Try official URL first (skip if server is known to be down or no valid URL)
            if (!skipOfficial && hasOfficialUrl)
            {
                try
                {
                    Logger.Info("Download", $"Downloading from official: {downloadUrl}");
                    _progressService.ReportDownloadProgress("download", 5, "launch.detail.downloading_official", null, 0, 0);
                    await _downloadService.DownloadFileAsync(downloadUrl, partPath, (progress, downloaded, total) =>
                    {
                        int mappedProgress = 5 + (int)(progress * 0.60);
                        _progressService.ReportDownloadProgress("download", mappedProgress, "launch.detail.downloading_official", [progress], downloaded, total);
                    }, ct);
                    downloaded = true;
                    Logger.Success("Download", "Downloaded from official successfully");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Logger.Warning("Download", $"Official download failed: {ex.Message}");
                    // Clean up partial file before mirror attempt
                    if (File.Exists(partPath)) try { File.Delete(partPath); } catch { }

                    // 403 from official CDN usually means expired/invalid signed verify token.
                    // Force-refresh version cache and retry official once with a new signed URL.
                    if (IsHttpForbidden(ex))
                    {
                        try
                        {
                            Logger.Warning("Download", "Official URL returned 403. Forcing version cache refresh and retrying official download once...");
                            await _versionService.ForceRefreshCacheAsync(branch, ct);

                            var refreshedEntry = _versionService.GetVersionEntry(branch, version);
                            var refreshedOfficialUrl = refreshedEntry?.PwrUrl;
                            var hasRefreshedOfficialUrl =
                                !string.IsNullOrEmpty(refreshedOfficialUrl)
                                && refreshedOfficialUrl.Contains("game-patches.hytale.com")
                                && refreshedOfficialUrl.Contains("verify=");

                            if (hasRefreshedOfficialUrl)
                            {
                                Logger.Info("Download", $"Retrying official download after cache refresh: {refreshedOfficialUrl}");
                                _progressService.ReportDownloadProgress("download", 5, "launch.detail.downloading_official", null, 0, 0);

                                await _downloadService.DownloadFileAsync(refreshedOfficialUrl!, partPath, (progress, dl, total) =>
                                {
                                    int mappedProgress = 5 + (int)(progress * 0.60);
                                    _progressService.ReportDownloadProgress("download", mappedProgress, "launch.detail.downloading_official", [progress], dl, total);
                                }, ct);

                                downloaded = true;
                                Logger.Success("Download", "Downloaded from official successfully after token refresh");
                            }
                            else
                            {
                                Logger.Warning("Download", "No refreshed official signed URL found after cache refresh");
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception refreshRetryEx)
                        {
                            Logger.Warning("Download", $"Official retry after cache refresh failed: {refreshRetryEx.Message}");
                            if (File.Exists(partPath)) try { File.Delete(partPath); } catch { }
                        }
                    }
                }
            }
            else if (!skipOfficial)
            {
                Logger.Info("Download", "No signed official URL available, skipping to mirror...");
            }
            else
            {
                Logger.Info("Download", "Official server is down, skipping to mirror...");
            }

            // Fallback to mirror (or primary if official is known down)
            if (!downloaded)
            {
                var mirrorUrl = await _versionService.GetMirrorDownloadUrlAsync(os, arch, branch, version, ct);
                if (mirrorUrl != null)
                {
                    try
                    {
                        try
                        {
                            var mirrorSize = await _downloadService.GetFileSizeAsync(mirrorUrl, ct);
                            if (mirrorSize >= 0 && mirrorSize < MinValidPwrBytes)
                            {
                                throw new MirrorBootstrapRequiredException(version, $"Mirror returned tiny full build ({mirrorSize} bytes) for v{version}");
                            }
                        }
                        catch (MirrorBootstrapRequiredException) { throw; }
                        catch
                        {
                            // Ignore HEAD/size-check failures and try real download.
                        }

                        Logger.Info("Download", $"Retrying from mirror: {mirrorUrl}");
                        _progressService.ReportDownloadProgress("download", 5, "launch.detail.downloading_mirror", null, 0, 0);

                        await _downloadService.DownloadFileAsync(mirrorUrl, partPath, (progress, dl, total) =>
                        {
                            int mappedProgress = 5 + (int)(progress * 0.60);
                            _progressService.ReportDownloadProgress("download", mappedProgress, "launch.detail.downloading_mirror", [progress], dl, total);
                        }, ct);

                        long downloadedSize = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
                        if (downloadedSize < MinValidPwrBytes)
                        {
                            throw new MirrorBootstrapRequiredException(version, $"Downloaded mirror full build is too small ({downloadedSize} bytes) for v{version}");
                        }

                        downloaded = true;
                        Logger.Success("Download", "Downloaded from mirror successfully");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (MirrorBootstrapRequiredException) { throw; }
                    catch (Exception mirrorEx)
                    {
                        Logger.Error("Download", $"Mirror download also failed: {mirrorEx.Message}");
                        
                        // If mirror returned 404, invalidate this version from cache
                        // to prevent showing unavailable versions to users
                        if (IsHttpNotFound(mirrorEx))
                        {
                            Logger.Warning("Download", $"Version v{version} not found on mirror, invalidating cache entry");
                            _versionService.InvalidateVersionFromCache(branch, version);
                        }
                    }
                }
                else if (_versionService.IsDiffBasedBranch(branch))
                {
                    // Pre-release uses diff patches on the mirror; signal caller to use diff chain
                    Logger.Info("Download", "Pre-release branch detected - falling back to diff-based mirror download");
                    throw new MirrorDiffRequiredException(version);
                }
            }

            if (!downloaded)
            {
                throw new Exception("Download failed from both official server and mirror. Please try again later.");
            }

            if (File.Exists(partPath))
                File.Move(partPath, pwrPath, true);
        }
        else
        {
            _progressService.ReportDownloadProgress("download", 65, "launch.detail.using_cached_installer", null, 0, 0);
        }
    }

    private static bool IsHttpForbidden(Exception ex)
    {
        if (ex is HttpRequestException hre && hre.StatusCode == HttpStatusCode.Forbidden)
        {
            return true;
        }

        var message = ex.Message ?? string.Empty;
        return message.Contains("HTTP 403", StringComparison.OrdinalIgnoreCase)
            || message.Contains("403 Forbidden", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHttpNotFound(Exception ex)
    {
        if (ex is HttpRequestException hre && hre.StatusCode == HttpStatusCode.NotFound)
        {
            return true;
        }

        var message = ex.Message ?? string.Empty;
        return message.Contains("HTTP 404", StringComparison.OrdinalIgnoreCase)
            || message.Contains("404 NotFound", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    private async Task EnsureRuntimeDependenciesAsync(CancellationToken ct)
    {
        // VC++ Redist check (Windows only)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _progressService.ReportDownloadProgress("install", 94, "launch.detail.vc_redist", null, 0, 0);
            try
            {
                await _launchService.EnsureVCRedistInstalledAsync((progress, message) =>
                {
                    int mappedProgress = 94 + (int)(progress * 0.02);
                    _progressService.ReportDownloadProgress("install", mappedProgress, message, null, 0, 0);
                });
            }
            catch (Exception ex)
            {
                Logger.Warning("VCRedist", $"VC++ install warning: {ex.Message}");
            }
        }

        // JRE check
        string jrePath = _launchService.GetJavaPath();
        if (!File.Exists(jrePath))
        {
            Logger.Info("Download", "JRE missing, installing...");
            _progressService.ReportDownloadProgress("install", 96, "launch.detail.java_install", null, 0, 0);
            await _launchService.EnsureJREInstalledAsync((progress, message) =>
            {
                int mappedProgress = 96 + (int)(progress * 0.03);
                _progressService.ReportDownloadProgress("install", mappedProgress, message, null, 0, 0);
            });
        }
    }
}

/// <summary>
/// Thrown when a pre-release download fails from official and the mirror requires diff-based download.
/// </summary>
internal class MirrorDiffRequiredException : Exception
{
    public int TargetVersion { get; }
    public MirrorDiffRequiredException(int targetVersion) : base("Mirror requires diff-based download for pre-release")
    {
        TargetVersion = targetVersion;
    }
}

internal class MirrorBootstrapRequiredException : Exception
{
    public int TargetVersion { get; }

    public MirrorBootstrapRequiredException(int targetVersion, string message) : base(message)
    {
        TargetVersion = targetVersion;
    }
}
