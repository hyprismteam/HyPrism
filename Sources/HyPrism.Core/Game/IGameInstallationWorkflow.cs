// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Models;
using HyPrism.Core.Accounts;

namespace HyPrism.Core.Game;

/// <summary>
/// Orchestrates the download/update/launch workflow.
/// Coordinates between IPatchManager, IGameLauncher, and other services
/// </summary>
public interface IGameInstallationWorkflow : IDisposable
{
    /// <summary>
    /// Downloads/updates the game and launches it upon completion
    /// </summary>
    /// <param name="authorizationUriPresenter">Optional host callback used when an official account requires interactive authorization</param>
    /// <returns>A <see cref="DownloadProgress"/> object for tracking download state and progress</returns>
    Task<DownloadProgress> DownloadAndLaunchAsync(
        AuthUriPresenter? authorizationUriPresenter = null);

    /// <summary>
    /// Downloads or updates a specific instance and launches it upon completion
    /// </summary>
    /// <param name="instanceId">Stable identifier of the target instance</param>
    /// <param name="authorizationUriPresenter">Optional host callback used when an official account requires interactive authorization</param>
    /// <returns>A <see cref="DownloadProgress"/> object for tracking download state and progress</returns>
    Task<DownloadProgress> DownloadAndLaunchInstanceAsync(
        string instanceId,
        AuthUriPresenter? authorizationUriPresenter = null);

    /// <summary>
    /// Cancels the ongoing operation for one instance
    /// </summary>
    /// <param name="instanceId">Stable identifier of the instance whose operation should be cancelled</param>
    void CancelDownload(string instanceId);
}
