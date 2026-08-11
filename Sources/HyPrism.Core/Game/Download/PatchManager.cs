// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Infrastructure;
using HyPrism.Core.Application.Progress;
using HyPrism.Core.Game.Patching;
using HyPrism.Core.Game.Instances;
using HyPrism.Core.Game.Versions;

namespace HyPrism.Core.Game.Download;

/// <summary>
/// Manages differential game updates by downloading and applying Butler PWR patches.
/// Handles the patch sequence calculation and applies patches incrementally.
/// </summary>
/// <remarks>
/// Extracted from the former monolithic GameInstallationWorkflow for better separation of concerns.
/// Works with the Butler tool to apply binary patches efficiently.
/// </remarks>
public class PatchManager : IPatchManager
{
    private readonly IGameVersionCatalog _versions;
    private readonly IButlerClient _butler;
    private readonly IFileDownloader _downloader;
    private readonly IInstanceRepository _instances;
    private readonly IProgressReporter _progress;
    private readonly HttpClient _httpClient;
    private readonly string _appDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchManager"/> class.
    /// </summary>
    /// <param name="versions">Service for version management and patch sequence calculation.</param>
    /// <param name="butler">Service for Butler patch tool operations.</param>
    /// <param name="downloader">Service for downloading patch files.</param>
    /// <param name="instances">Service for managing game instances.</param>
    /// <param name="progress">Service for reporting progress notifications.</param>
    /// <param name="httpClient">HTTP client for network operations.</param>
    /// <param name="appPath">Application path configuration.</param>
    public PatchManager(
        IGameVersionCatalog versions,
        IButlerClient butler,
        IFileDownloader downloader,
        IInstanceRepository instances,
        IProgressReporter progress,
        HttpClient httpClient,
        AppPathConfiguration appPath)
    {
        _versions = versions;
        _butler = butler;
        _downloader = downloader;
        _instances = instances;
        _progress = progress;
        _httpClient = httpClient;
        _appDir = appPath.AppDir;
    }

    /// <inheritdoc/>
    public async Task ApplyDifferentialUpdateAsync(
        string versionPath,
        string branch,
        int installedVersion,
        int latestVersion,
        CancellationToken ct = default)
    {
        bool officialDown = _versions.IsOfficialServerDown(branch);
        var normalizedBranch = LauncherUtilities.NormalizeVersionType(branch);
        var os = LauncherUtilities.GetOS();
        var arch = LauncherUtilities.GetArch();

        Logger.Info("Download", $"Differential update: v{installedVersion} -> v{latestVersion} (official={!officialDown})");
        _progress.ReportDownloadProgress("update", 0, $"Updating game from v{installedVersion} to v{latestVersion}...", null, 0, 0);

        // Ensure Butler is available
        await _butler.EnsureButlerInstalledAsync((_, _) => { });

        // Mirror + release: use a single full standalone game copy.
        // Differential release updates are intentionally not used here.
        if (officialDown && !_versions.IsDiffBasedBranch(normalizedBranch))
        {
            Logger.Info("Download", $"Mirror release: downloading full copy v{latestVersion}");
            await DownloadAndApplyMirrorFullCopyAsync(versionPath, normalizedBranch, os, arch, latestVersion, ct);
            return;
        }

        // Official server or mirror pre-release: apply patches sequentially
        var patchesToApply = _versions.GetPatchSequence(installedVersion, latestVersion);
        Logger.Info("Download", $"Patches to apply: {string.Join(" -> ", patchesToApply)}");

        for (int i = 0; i < patchesToApply.Count; i++)
        {
            int patchVersion = patchesToApply[i];
            int prevVersion = patchVersion - 1; // Sequence is always contiguous
            ct.ThrowIfCancellationRequested();

            int baseProgress = (i * 90) / patchesToApply.Count;
            int progressPerPatch = 90 / patchesToApply.Count;

            _progress.ReportDownloadProgress("update", baseProgress,
                $"Downloading patch {i + 1}/{patchesToApply.Count} (v{patchVersion})...", null, 0, 0);

            string patchPwrPath = Path.Combine(_appDir, "Cache", $"{branch}_patch_{patchVersion}.pwr");
            Directory.CreateDirectory(Path.GetDirectoryName(patchPwrPath)!);

            if (officialDown)
            {
                // Mirror pre-release: download diff v{prev}~{version} directly
                await DownloadMirrorDiffAsync(os, arch, normalizedBranch, prevVersion, patchVersion,
                    patchPwrPath, i, patchesToApply.Count, baseProgress, progressPerPatch, ct);
            }
            else
            {
                // Get URL from cache (will refresh if needed)
                string patchUrl;
                try
                {
                    patchUrl = await _versions.RefreshAndGetDownloadUrlAsync(normalizedBranch, patchVersion, ct);
                }
                catch (Exception ex)
                {
                    Logger.Warning("Download", $"Failed to get official URL for patch v{patchVersion}: {ex.Message}");
                    // Fall back to mirror diff
                    await DownloadMirrorDiffAsync(os, arch, normalizedBranch, prevVersion, patchVersion,
                        patchPwrPath, i, patchesToApply.Count, baseProgress, progressPerPatch, ct);
                    goto applyPatch;
                }
                
                Logger.Info("Download", $"Downloading patch: {patchUrl}");
                await DownloadPatchWithFallbackAsync(patchUrl, patchPwrPath, os, arch, normalizedBranch,
                    prevVersion, patchVersion, i, patchesToApply.Count, baseProgress, progressPerPatch, ct);
            }

            applyPatch:
            // Apply the downloaded patch with Butler
            ct.ThrowIfCancellationRequested();
            int applyBaseProgress = baseProgress + (progressPerPatch / 2);
            _progress.ReportDownloadProgress("update", applyBaseProgress,
                $"Applying patch {i + 1}/{patchesToApply.Count}...", null, 0, 0);

            await _butler.ApplyPwrAsync(patchPwrPath, versionPath, (progress, message) =>
            {
                int mappedProgress = applyBaseProgress + (int)(progress * 0.5 * progressPerPatch / 100);
                _progress.ReportDownloadProgress("update", mappedProgress, message, null, 0, 0);
            }, ct);

            if (File.Exists(patchPwrPath))
                try { File.Delete(patchPwrPath); } catch { }

            _instances.SaveLatestInfo(branch, patchVersion);
            Logger.Success("Download", $"Patch v{patchVersion} applied successfully");
        }

        Logger.Success("Download", $"Differential update complete: now at v{latestVersion}");
    }

    /// <summary>
    /// Mirror release shortcut: download a single full copy and apply it.
    /// On the mirror, release files contain the complete game, not diffs.
    /// </summary>
    private async Task DownloadAndApplyMirrorFullCopyAsync(
        string versionPath, string branch, string os, string arch,
        int version, CancellationToken ct)
    {
        var mirrorUrl = await _versions.GetMirrorDownloadUrlAsync(os, arch, branch, version, ct);
        if (mirrorUrl == null)
            throw new Exception($"Mirror does not have release v{version} for {os}/{arch}");

        string pwrPath = Path.Combine(_appDir, "Cache", $"{branch}_mirror_full_{version}.pwr");
        Directory.CreateDirectory(Path.GetDirectoryName(pwrPath)!);

        Logger.Info("Download", $"Downloading full copy from mirror: {mirrorUrl}");
        _progress.ReportDownloadProgress("update", 5, "launch.detail.downloading_mirror", null, 0, 0);

        await _downloader.DownloadFileAsync(mirrorUrl, pwrPath, (progress, dl, total) =>
        {
            int mappedProgress = 5 + (int)(progress * 0.45);
            _progress.ReportDownloadProgress("update", mappedProgress, "launch.detail.downloading_mirror", [progress], dl, total);
        }, ct);

        Logger.Success("Download", $"Full copy v{version} downloaded from mirror");

        _progress.ReportDownloadProgress("update", 55, "launch.detail.installing_butler_pwr", null, 0, 0);

        await _butler.ApplyPwrAsync(pwrPath, versionPath, (progress, message) =>
        {
            int mappedProgress = 55 + (int)(progress * 0.35);
            _progress.ReportDownloadProgress("update", mappedProgress, message, null, 0, 0);
        }, ct);

        if (File.Exists(pwrPath))
            try { File.Delete(pwrPath); } catch { }

        _instances.SaveLatestInfo(branch, version);
        Logger.Success("Download", $"Mirror release update complete: now at v{version}");
    }

    /// <summary>
    /// Downloads a diff patch directly from the mirror (pre-release when official is down).
    /// </summary>
    private async Task DownloadMirrorDiffAsync(
        string os, string arch, string branch,
        int fromVersion, int toVersion,
        string destPath,
        int patchIndex, int totalPatches,
        int baseProgress, int progressPerPatch,
        CancellationToken ct)
    {
        var mirrorUrl = await _versions.GetMirrorDiffUrlAsync(os, arch, branch, fromVersion, toVersion, ct);
        if (mirrorUrl == null)
            throw new Exception($"Mirror does not have diff v{fromVersion}~{toVersion} for {os}/{arch}/{branch}");

        Logger.Info("Download", $"Downloading diff v{fromVersion}~{toVersion} from mirror: {mirrorUrl}");
        _progress.ReportDownloadProgress("update", baseProgress,
            $"Downloading patch {patchIndex + 1}/{totalPatches} from mirror (v{fromVersion}→v{toVersion})...", null, 0, 0);

        await _downloader.DownloadFileAsync(mirrorUrl, destPath, (progress, dl, total) =>
        {
            int mappedProgress = baseProgress + (int)(progress * 0.5 * progressPerPatch / 100);
            _progress.ReportDownloadProgress("update", mappedProgress,
                $"Downloading patch {patchIndex + 1}/{totalPatches} (mirror)... {progress}%", null, dl, total);
        }, ct);

        Logger.Success("Download", $"Diff v{fromVersion}~{toVersion} downloaded from mirror");
    }

    /// <summary>
    /// Downloads a patch from the official server with mirror fallback.
    /// Used when the official server is available but may fail.
    /// </summary>
    private async Task DownloadPatchWithFallbackAsync(
        string officialUrl, string destPath,
        string os, string arch, string branch,
        int prevVersion, int patchVersion,
        int patchIndex, int totalPatches,
        int baseProgress, int progressPerPatch,
        CancellationToken ct)
    {
        bool downloaded = false;

        // Try official URL first
        try
        {
            await _downloader.DownloadFileAsync(officialUrl, destPath, (progress, dl, total) =>
            {
                int mappedProgress = baseProgress + (int)(progress * 0.5 * progressPerPatch / 100);
                _progress.ReportDownloadProgress("update", mappedProgress,
                    $"Downloading patch {patchIndex + 1}/{totalPatches}... {progress}%", null, dl, total);
            }, ct);
            downloaded = true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.Warning("Download", $"Official patch download failed: {ex.Message}");
            if (File.Exists(destPath)) try { File.Delete(destPath); } catch { }
        }

        // Fallback to mirror
        if (!downloaded)
        {
            // Try the correct mirror method based on branch type
            string? mirrorUrl;
            if (_versions.IsDiffBasedBranch(branch))
                mirrorUrl = await _versions.GetMirrorDiffUrlAsync(os, arch, branch, prevVersion, patchVersion, ct);
            else
                mirrorUrl = await _versions.GetMirrorDownloadUrlAsync(os, arch, branch, patchVersion, ct);

            if (mirrorUrl != null)
            {
                try
                {
                    Logger.Info("Download", $"Retrying patch from mirror: {mirrorUrl}");
                    _progress.ReportDownloadProgress("update", baseProgress,
                        $"Downloading patch {patchIndex + 1}/{totalPatches} from mirror...", null, 0, 0);

                    await _downloader.DownloadFileAsync(mirrorUrl, destPath, (progress, dl, total) =>
                    {
                        int mappedProgress = baseProgress + (int)(progress * 0.5 * progressPerPatch / 100);
                        _progress.ReportDownloadProgress("update", mappedProgress,
                            $"Downloading patch {patchIndex + 1}/{totalPatches} (mirror)... {progress}%", null, dl, total);
                    }, ct);
                    downloaded = true;
                    Logger.Success("Download", $"Patch v{patchVersion} downloaded from mirror");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception mirrorEx)
                {
                    Logger.Error("Download", $"Mirror patch download also failed: {mirrorEx.Message}");
                }
            }
            else
            {
                Logger.Warning("Download", $"No mirror URL available for patch v{patchVersion}");
            }
        }

        if (!downloaded)
            throw new Exception($"Failed to download patch v{patchVersion} from both official server and mirror");
    }
}
