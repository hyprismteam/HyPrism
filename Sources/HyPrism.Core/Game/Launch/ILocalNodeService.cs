// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using HyPrism.Core.Game.Authentication;

namespace HyPrism.Core.Game.Launch;

/// <summary>
/// Controls the loopback authentication and account service used by autonomous profiles
/// </summary>
public interface ILocalNodeService : IDisposable
{
    /// <summary>
    /// Gets the host and port written into patched Hytale service URLs
    /// </summary>
    string EndpointDomain { get; }

    /// <summary>
    /// Gets the canonical issuer written into local OmniAuth tokens
    /// </summary>
    string Issuer { get; }

    /// <summary>
    /// Starts the Local Node, selects the installed cosmetics catalog, and verifies its health endpoint
    /// </summary>
    /// <param name="gameDirectory">Root directory of the game instance being launched</param>
    /// <param name="cancellationToken">Cancels startup and the readiness request</param>
    /// <returns>A task that completes after the Local Node is ready</returns>
    Task EnsureReadyAsync(
        string? gameDirectory = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or renews the active OmniAuth session for a local profile
    /// </summary>
    /// <param name="playerUuid">Stable UUID of the selected local profile</param>
    /// <param name="playerName">Display name of the selected local profile</param>
    /// <param name="cancellationToken">Cancels the session request</param>
    /// <returns>The signed identity and session tokens</returns>
    Task<OmniAuthSession> CreateSessionAsync(
        string playerUuid,
        string playerName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transfers Local Node lifetime ownership to the launched game process
    /// </summary>
    /// <param name="gameProcessId">Operating system process ID of the launched game</param>
    /// <param name="cancellationToken">Cancels the lifecycle request</param>
    /// <returns>A task that completes after the Local Node accepts the game process</returns>
    Task AttachGameProcessAsync(
        int gameProcessId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops an unattached Local Node after a failed game launch
    /// </summary>
    /// <param name="cancellationToken">Cancels the shutdown request</param>
    /// <returns>A task that completes after the Local Node has stopped</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies process-scoped certificate trust required to call the Local Node
    /// </summary>
    /// <param name="startInfo">Game process configuration that receives trust variables</param>
    void ApplyClientTrust(ProcessStartInfo startInfo);
}
