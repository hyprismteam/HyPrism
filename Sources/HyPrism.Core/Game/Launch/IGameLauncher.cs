// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Accounts;

namespace HyPrism.Core.Game.Launch;

/// <summary>
/// Handles launching the game process, including client patching,
/// authentication, and process lifecycle management
/// </summary>
public interface IGameLauncher
{
    /// <summary>
    /// Launches the game from the specified version directory
    /// </summary>
    /// <param name="versionPath">The path to the game version directory containing the client</param>
    /// <param name="branch">The game branch ("release" or "pre-release")</param>
    /// <param name="authorizationUriPresenter">Optional host callback used when an official account requires interactive authorization</param>
    /// <param name="instanceId">Optional identifier of the instance being launched</param>
    /// <param name="ct">Token to cancel the launch operation</param>
    /// <returns>A task that completes after the game process starts</returns>
    /// <exception cref="InvalidOperationException">Thrown if the game is already running</exception>
    /// <exception cref="FileNotFoundException">Thrown if the client executable is not found</exception>
    Task LaunchGameAsync(
        string versionPath,
        string branch,
        AuthUriPresenter? authorizationUriPresenter = null,
        string? instanceId = null,
        CancellationToken ct = default);
}
