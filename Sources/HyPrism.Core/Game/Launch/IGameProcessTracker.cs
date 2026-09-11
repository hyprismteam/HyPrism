// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;

namespace HyPrism.Core.Game.Launch;

/// <summary>
/// Manages the game process lifecycle, including tracking, monitoring, and termination
/// </summary>
public interface IGameProcessTracker
{
    /// <summary>
    /// Raised with the affected process when a game process starts being tracked.
    /// Not raised for processes restored from the registry at startup, since no
    /// subscriber exists yet; query <see cref="IsGameRunning"/> for initial state
    /// </summary>
    event EventHandler<GameProcessStartedEventArgs>? GameProcessStarted;

    /// <summary>
    /// Raised with the affected process when any tracked game process exits
    /// </summary>
    event EventHandler<GameProcessExitedEventArgs>? GameProcessExited;

    /// <summary>
    /// Tracks a launched game process without replacing other active instances
    /// </summary>
    /// <param name="process">The newly launched game process.</param>
    /// <param name="instanceId">Stable instance identifier.</param>
    /// <param name="profileId">Stable launcher profile identifier.</param>
    /// <param name="officialAccountId">Official Hytale account owner identifier, when applicable.</param>
    void TrackGameProcess(
        Process process,
        string instanceId,
        string profileId,
        string? officialAccountId = null);

    /// <summary>
    /// Gets all game processes restored from disk or started by this launcher instance
    /// </summary>
    /// <returns>All known running game processes</returns>
    IReadOnlyCollection<GameProcessInfo> GetRunningProcesses();

    /// <summary>
    /// Returns tracked processes that ended while the launcher was not running
    /// Each record is returned at most once so startup cleanup can reconcile persistent state
    /// </summary>
    /// <returns>Processes requiring one-time startup reconciliation</returns>
    IReadOnlyCollection<GameProcessInfo> TakeProcessesExitedWhileUnavailable();

    /// <summary>
    /// Checks whether a specific game instance is currently running
    /// <param name="instanceId">Stable instance identifier</param>
    /// <returns><see langword="true"/> when the instance is running</returns>
    /// </summary>
    bool IsInstanceRunning(string instanceId);

    /// <summary>
    /// Checks if the tracked game process is currently running
    /// </summary>
    /// <returns><c>true</c> if the game process is running; otherwise, <c>false</c></returns>
    bool IsGameRunning();

    /// <summary>
    /// Terminates the game process belonging to a specific instance
    /// <param name="instanceId">Stable instance identifier</param>
    /// <returns><see langword="true"/> when a running process was terminated</returns>
    /// </summary>
    bool ExitGame(string instanceId);
}
