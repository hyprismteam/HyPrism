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
    /// Raised when the tracked game process has exited
    /// </summary>
    event EventHandler? ProcessExited;

    /// <summary>
    /// Raised with the affected process when any tracked game process exits
    /// </summary>
    event EventHandler<GameProcessExitedEventArgs>? GameProcessExited;

    /// <summary>
    /// Sets the current game process reference
    /// </summary>
    /// <param name="p">The game process, or <c>null</c> to clear the reference</param>
    void SetGameProcess(Process? p);

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
    /// Gets the current game process reference
    /// </summary>
    /// <returns>The current game process, or <c>null</c> if no game is tracked</returns>
    Process? GetGameProcess();

    /// <summary>
    /// Gets all game processes restored from disk or started by this launcher instance
    /// <returns>All known running game processes</returns>
    /// </summary>
    IReadOnlyCollection<GameProcessInfo> GetRunningProcesses();

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
    /// Scans the system for any running Hytale game processes
    /// </summary>
    /// <returns><c>true</c> if a Hytale process is found running; otherwise, <c>false</c></returns>
    bool CheckForRunningGame();

    /// <summary>
    /// Terminates the current game process if it is running
    /// </summary>
    /// <returns><c>true</c> if the game was successfully terminated; otherwise, <c>false</c></returns>
    bool ExitGame();

    /// <summary>
    /// Terminates the game process belonging to a specific instance
    /// <param name="instanceId">Stable instance identifier</param>
    /// <returns><see langword="true"/> when a running process was terminated</returns>
    /// </summary>
    bool ExitGame(string instanceId);
}
