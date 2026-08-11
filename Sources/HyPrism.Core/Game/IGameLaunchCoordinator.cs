// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Accounts;

namespace HyPrism.Core.Game;

/// <summary>
/// Coordinates a user-initiated game launch independently from any UI transport
/// </summary>
public interface IGameLaunchCoordinator
{
    /// <summary>
    /// Starts the selected instance and optionally overrides launch preferences for this operation
    /// </summary>
    /// <param name="instanceId">Stable instance identifier, or <see langword="null"/> to use the current selection</param>
    /// <param name="launchAfterDownload">Per-operation launch preference, or <see langword="null"/> to use the saved setting</param>
    /// <param name="authorizationUriPresenter">Optional host callback used when an official account requires interactive authorization</param>
    /// <returns>A task that completes when launch coordination finishes</returns>
    /// <exception cref="InvalidOperationException">Thrown when no usable instance can be selected</exception>
    Task LaunchAsync(
        string? instanceId = null,
        bool? launchAfterDownload = null,
        AuthUriPresenter? authorizationUriPresenter = null);
}
