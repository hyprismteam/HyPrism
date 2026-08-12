// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Application.Progress;
using HyPrism.Core.Infrastructure;
using HyPrism.Core.Game.Instances;
using HyPrism.Core.Game.Launch;
using HyPrism.Core.Accounts;

namespace HyPrism.Core.Game;

/// <summary>
/// UI-neutral entry point for the complete game launch workflow.
/// </summary>
public sealed class GameLaunchCoordinator(
    IGameInstallationWorkflow gameSession,
    IGameProcessTracker processService,
    IInstanceRepository instances,
    IConfigStore configStore,
    IProgressReporter progress) : IGameLaunchCoordinator
{
    private const int ExitSuccess = 0;
    private const int ErrorGenericLaunch = 1;
    private const int ErrorNotInstalled = 11;
    private const int ErrorDownloadFailed = 12;
    private const int ErrorLaunchFailed = 13;
    private const int ErrorMirrorUnreachable = 14;

    /// <inheritdoc/>
    public async Task LaunchAsync(
        string? instanceId = null,
        bool? launchAfterDownload = null,
        AuthUriPresenter? authorizationUriPresenter = null)
    {
        if (processService.IsGameRunning())
        {
            Logger.Warning("Game", "Game launch request ignored - game already running");
            return;
        }

        var shouldLaunchAfterDownload =
            launchAfterDownload ?? configStore.Configuration.LaunchAfterDownload;

        if (!string.IsNullOrWhiteSpace(instanceId))
            instances.SetSelectedInstance(instanceId);

        // Install and update remain separate UI actions, so this operation launches
        // only an instance whose selected version is already installed.
        var selectedInstance = instances.GetSelectedInstance();
        if (selectedInstance != null)
        {
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
        }

        Logger.Info("Game", "Game launch requested");

        try
        {
            var result = await gameSession.DownloadAndLaunchAsync(
                () => shouldLaunchAfterDownload,
                authorizationUriPresenter);

            if (result.Cancelled || string.Equals(result.Error, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                progress.ReportGameStateChanged("stopped", ExitSuccess);
                return;
            }

            if (result.Success && !shouldLaunchAfterDownload)
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
