// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Core.Application.Ports;

/// <summary>
/// Describes the launcher activity exposed to presence integrations
/// </summary>
public enum PresenceState
{
    /// <summary>The user is idle in the launcher</summary>
    Idle,

    /// <summary>Game files are being downloaded</summary>
    Downloading,

    /// <summary>Game files are being installed or extracted</summary>
    Installing,

    /// <summary>The user is playing the game</summary>
    Playing
}

/// <summary>
/// Manages Discord Rich Presence integration for displaying game/launcher status.
/// Implements <see cref="IDisposable"/> to properly cleanup Discord RPC connection
/// </summary>
public interface IDiscordPresence : IDisposable
{
    /// <summary>
    /// Initializes the Discord RPC client and establishes connection to Discord.
    /// Should be called once during application startup
    /// </summary>
    void Initialize();

    /// <summary>
    /// Updates the Discord Rich Presence with the specified state and details
    /// </summary>
    /// <param name="state">The current presence state (e.g., InLauncher, Downloading, Playing)</param>
    /// <param name="details">Optional additional details to display in the presence</param>
    /// <param name="progress">Optional progress percentage (0-100) for download/update operations</param>
    void SetPresence(PresenceState state, string? details = null, int? progress = null);

    /// <summary>
    /// Clears the current Discord Rich Presence, removing HyPrism from the user's status
    /// </summary>
    void ClearPresence();
}
