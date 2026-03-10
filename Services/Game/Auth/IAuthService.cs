using HyPrism.Models;

namespace HyPrism.Services.Game.Auth;

/// <summary>
/// Handles authentication with the custom Hytale auth server.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Creates a game session and retrieves authentication tokens.
    /// </summary>
    Task<AuthTokenResult> GetGameSessionTokenAsync(string uuid, string playerName);

    /// <summary>
    /// Requests an offline mode token from the auth server.
    /// Used when the game client requires HYTALE_OFFLINE_TOKEN for offline/singleplayer mode.
    /// </summary>
    Task<string?> GetOfflineTokenAsync(string uuid, string playerName, CancellationToken ct = default);

    /// <summary>
    /// Validates an existing token is still valid.
    /// </summary>
    Task<bool> ValidateTokenAsync(string token);
}
