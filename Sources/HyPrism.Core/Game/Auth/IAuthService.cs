// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Models;

namespace HyPrism.Services.Game.Auth;

/// <summary>
/// Handles authentication with the custom Hytale auth server
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Creates a game session and retrieves authentication tokens
    /// </summary>
    /// <param name="uuid">Player UUID used for the session</param>
    /// <param name="playerName">Player name sent to the authentication service</param>
    /// <returns>The token response returned by the authentication service</returns>
    /// <exception cref="HttpRequestException">Thrown when the authentication request fails</exception>
    Task<AuthTokenResult> GetGameSessionTokenAsync(string uuid, string playerName);

    /// <summary>
    /// Requests an offline mode token from the auth server.
    /// Used when the game client requires HYTALE_OFFLINE_TOKEN for offline/singleplayer mode
    /// </summary>
    /// <param name="uuid">Player UUID used for the token</param>
    /// <param name="playerName">Player name used for the token</param>
    /// <param name="ct">Token that cancels the request</param>
    /// <returns>The offline token, or <see langword="null"/> when the server does not provide one</returns>
    /// <exception cref="HttpRequestException">Thrown when the authentication request fails</exception>
    Task<string?> GetOfflineTokenAsync(string uuid, string playerName, CancellationToken ct = default);

    /// <summary>
    /// Validates an existing token is still valid
    /// </summary>
    /// <param name="token">Token to validate</param>
    /// <returns><see langword="true"/> when the server accepts the token</returns>
    /// <exception cref="HttpRequestException">Thrown when validation cannot reach the authentication service</exception>
    Task<bool> ValidateTokenAsync(string token);
}
