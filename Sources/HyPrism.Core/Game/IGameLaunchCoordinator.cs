// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Services.Game;

/// <summary>
/// Coordinates a user-initiated game launch independently from any UI transport.
/// </summary>
public interface IGameLaunchCoordinator
{
    /// <summary>
    /// Starts the selected instance, optionally overriding the selected instance and
    /// the launch-after-download preference for this operation.
    /// </summary>
    Task LaunchAsync(string? instanceId = null, bool? launchAfterDownload = null);
}
