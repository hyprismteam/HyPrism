// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Services.Core.App;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Game.Instance;
using HyPrism.Services.Game.Launch;
using HyPrism.Services.User;

namespace HyPrism.Services.Game;

/// <summary>
/// UI-neutral entry point for the complete game launch workflow.
/// </summary>
public sealed class GameLaunchCoordinator(
    IGameSessionService gameSession,
    IGameProcessService processService,
    IInstanceService instanceService,
    IConfigService configService,
    IProgressNotificationService progressService) : IGameLaunchCoordinator
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
            launchAfterDownload ?? configService.Configuration.LaunchAfterDownload;

        if (!string.IsNullOrWhiteSpace(instanceId))
            instanceService.SetSelectedInstance(instanceId);

        // Preserve the current launch behavior while moving orchestration out of
        // the Electron IPC adapter. Install/update remains a separate UI action.
        var selectedInstance = instanceService.GetSelectedInstance();
        if (selectedInstance != null)
        {
            var versionPath = instanceService.GetInstancePathById(selectedInstance.Id);
            if (!string.IsNullOrEmpty(versionPath) && !instanceService.IsClientPresent(versionPath))
            {
                Logger.Warning("Game", $"Instance {selectedInstance.Id} has no game client installed");
                progressService.ReportError(
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
                progressService.ReportGameStateChanged("stopped", ExitSuccess);
                return;
            }

            if (result.Success && !shouldLaunchAfterDownload)
            {
                progressService.ReportGameStateChanged("stopped", ExitSuccess);
                return;
            }

            if (!result.Success)
            {
                var exitCode = DetermineExitCodeForError(result.Error);
                progressService.ReportError(
                    "download",
                    "Failed to install game",
                    result.Error ?? "Unknown error");
                progressService.ReportGameStateChanged("stopped", exitCode);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Game launch failed: {ex.Message}");
            progressService.ReportError("download", "Failed to install game", ex.ToString());
            progressService.ReportGameStateChanged("stopped", ErrorLaunchFailed);
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
