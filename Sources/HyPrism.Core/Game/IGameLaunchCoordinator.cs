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
    /// Raised when a launch attempt ends without a running game process, either
    /// because it failed or because it was cancelled. A successful attempt raises
    /// no event here; the started process is announced by the game process tracker
    /// </summary>
    event EventHandler<LaunchFailedEventArgs>? LaunchFailed;

    /// <summary>
    /// Starts the selected or explicitly addressed instance
    /// </summary>
    /// <param name="instanceId">Stable instance identifier that does not change the current selection, or <see langword="null"/> to use the current selection</param>
    /// <param name="authorizationUriPresenter">Optional host callback used when an official account requires interactive authorization</param>
    /// <returns>A task that completes when launch coordination finishes</returns>
    /// <exception cref="InvalidOperationException">Thrown when no usable instance can be selected</exception>
    Task LaunchAsync(
        string? instanceId = null,
        AuthUriPresenter? authorizationUriPresenter = null);
}

/// <summary>
/// Provides details when a launch attempt ends without a running game process
/// </summary>
public sealed class LaunchFailedEventArgs(string? instanceId, int exitCode) : EventArgs
{
    /// <summary>
    /// Gets the stable identifier of the instance the attempt targeted, or
    /// <see langword="null"/> when no instance could be resolved
    /// </summary>
    public string? InstanceId { get; } = instanceId;

    /// <summary>
    /// Gets the stable exit code describing the outcome; 0 means the attempt was cancelled
    /// </summary>
    public int ExitCode { get; } = exitCode;
}
