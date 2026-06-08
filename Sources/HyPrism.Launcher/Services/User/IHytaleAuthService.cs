using HyPrism.Models;

namespace HyPrism.Services.User;

/// <summary>
/// Interface for Hytale OAuth 2.0 authentication service.
/// Handles login, logout, session management and token refresh.
/// </summary>
public interface IHytaleAuthService
{
    /// <summary>
    /// Current authenticated session, if any.
    /// </summary>
    HytaleAuthSession? CurrentSession { get; }

    /// <summary>
    /// Initiates OAuth 2.0 login flow with PKCE.
    /// Opens browser for user authentication.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Authenticated session, or null if login failed/cancelled.</returns>
    Task<HytaleAuthSession?> LoginAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs out the current user and clears session data.
    /// </summary>
    void Logout();

    /// <summary>
    /// Gets a valid session, refreshing tokens if needed.
    /// </summary>
    /// <returns>Valid session, or null if not authenticated.</returns>
    Task<HytaleAuthSession?> GetValidSessionAsync();

    /// <summary>
    /// Forces a token refresh regardless of expiration.
    /// </summary>
    /// <returns>True if refresh succeeded.</returns>
    Task<bool> ForceRefreshAsync();

    /// <summary>
    /// Ensures a fresh session is available for game launch.
    /// Automatically refreshes tokens if close to expiration.
    /// </summary>
    /// <returns>Fresh session, or null if authentication failed.</returns>
    Task<HytaleAuthSession?> EnsureFreshSessionForLaunchAsync();

    /// <summary>
    /// Reloads session data when switching profiles.
    /// </summary>
    void ReloadSessionForCurrentProfile();

    /// <summary>
    /// Gets a valid session from any official profile (not just the active one).
    /// Used for fetching version info when the current profile may not be official.
    /// </summary>
    /// <returns>Valid session from any official profile, or null if none available.</returns>
    Task<HytaleAuthSession?> GetValidOfficialSessionAsync();

    /// <summary>
    /// Saves the current session to the active profile's folder.
    /// Use this after LoginAsync() when re-authenticating within an existing official profile.
    /// </summary>
    void SaveCurrentSession();

    /// <summary>
    /// Saves the current session to a specific profile's folder.
    /// Used when creating a new official profile to enable Hytale source access.
    /// </summary>
    /// <param name="profile">The profile to save the session to.</param>
    /// <returns>True if the session was saved successfully.</returns>
    bool SaveSessionToProfile(Profile profile);
}
