// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Core.Game.Launch;

/// <summary>
/// Describes a game process known to the launcher.
/// </summary>
public sealed record GameProcessInfo(
    int ProcessId,
    DateTime ProcessStartedAtUtc,
    string InstanceId,
    string ProfileId,
    string? OfficialAccountId,
    DateTime RegisteredAtUtc);

/// <summary>
/// Provides details when a tracked game process starts.
/// </summary>
public sealed class GameProcessStartedEventArgs(GameProcessInfo process) : EventArgs
{
    /// <summary>
    /// Gets the process that started.
    /// </summary>
    public GameProcessInfo Process { get; } = process;
}

/// <summary>
/// Provides details when a tracked game process exits.
/// </summary>
public sealed class GameProcessExitedEventArgs(GameProcessInfo process, int exitCode) : EventArgs
{
    /// <summary>
    /// Gets the process that exited.
    /// </summary>
    public GameProcessInfo Process { get; } = process;

    /// <summary>
    /// Gets the exit code reported by the process, or 0 when it could not be read.
    /// </summary>
    public int ExitCode { get; } = exitCode;
}
