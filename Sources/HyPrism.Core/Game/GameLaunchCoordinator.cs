// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Application.Progress;
using HyPrism.Core.Game.Instances;
using HyPrism.Core.Game.Launch;
using HyPrism.Core.Accounts;
using HyPrism.Core.Infrastructure;

namespace HyPrism.Core.Game;

/// <summary>
/// UI-neutral entry point for the complete game launch workflow.
/// </summary>
public sealed class GameLaunchCoordinator(
    IGameInstallationWorkflow gameSession,
    IGameProcessTracker processService,
    IInstanceRepository instances,
    IProgressReporter progress) : IGameLaunchCoordinator
{
    private readonly HashSet<string> _launchingInstanceIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _launchLock = new();
    private const int ExitSuccess = 0;
    private const int ErrorGenericLaunch = 1;
    private const int ErrorNotInstalled = 11;
    private const int ErrorDownloadFailed = 12;
    private const int ErrorLaunchFailed = 13;
    private const int ErrorMirrorUnreachable = 14;

    /// <inheritdoc/>
    public async Task LaunchAsync(
        string? instanceId = null,
        AuthUriPresenter? authorizationUriPresenter = null)
    {
        // Install and update remain separate UI actions, so this operation launches
        // only an instance whose selected version is already installed.
        var selectedInstance = string.IsNullOrWhiteSpace(instanceId)
            ? instances.GetSelectedInstance()
            : instances.FindInstanceById(instanceId);
        if (selectedInstance != null)
        {
            if (processService.IsInstanceRunning(selectedInstance.Id))
            {
                Logger.Warning("Game", $"Game launch request ignored - instance {selectedInstance.Id} is already running");
                return;
            }

            var versionPath = instances.GetInstancePathById(selectedInstance.Id);
            if (!string.IsNullOrEmpty(versionPath) && !instances.IsClientPresent(versionPath))
            {
                Logger.Warning("Game", $"Instance {selectedInstance.Id} has no game client installed");
                progress.ReportError(
                    "launch",
                    "Game not installed",
                    $"Instance '{selectedInstance.Name}' has no game installed. Click UPDATE to install.");
                return;
            }

            lock (_launchLock)
            {
                if (!_launchingInstanceIds.Add(selectedInstance.Id))
                {
                    Logger.Warning("Game", $"Game launch request ignored - instance {selectedInstance.Id} is already being prepared");
                    return;
                }
            }
        }

        Logger.Info("Game", "Game launch requested");

        try
        {
            var result = string.IsNullOrWhiteSpace(instanceId)
                ? await gameSession.DownloadAndLaunchAsync(authorizationUriPresenter)
                : await gameSession.DownloadAndLaunchInstanceAsync(instanceId, authorizationUriPresenter);

            if (result.Cancelled || string.Equals(result.Error, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                progress.ReportGameStateChanged("stopped", ExitSuccess);
                return;
            }

            if (!result.Success)
            {
                var exitCode = DetermineExitCodeForError(result.Error);
                progress.ReportError(
                    "download",
                    "Failed to install game",
                    result.Error ?? "Unknown error");
                progress.ReportGameStateChanged("stopped", exitCode);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Game launch failed: {ex.Message}");
            progress.ReportError("download", "Failed to install game", ex.ToString());
            progress.ReportGameStateChanged("stopped", ErrorLaunchFailed);
        }
        finally
        {
            if (selectedInstance is not null)
            {
                lock (_launchLock)
                    _launchingInstanceIds.Remove(selectedInstance.Id);
            }
        }
    }

    private static int DetermineExitCodeForError(string? error)
    {
        if (string.IsNullOrEmpty(error))
            return ErrorDownloadFailed;

        var normalized = error.ToLowerInvariant();

        if (normalized.Contains("not installed") || normalized.Contains("no game client"))
            return ErrorNotInstalled;

        if (normalized.Contains("mirror") &&
            (normalized.Contains("unreachable") || normalized.Contains("failed")))
            return ErrorMirrorUnreachable;

        if (normalized.Contains("launch"))
            return ErrorLaunchFailed;

        if (normalized.Contains("download"))
            return ErrorDownloadFailed;

        return ErrorGenericLaunch;
    }
}
