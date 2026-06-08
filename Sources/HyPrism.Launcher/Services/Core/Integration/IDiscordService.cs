namespace HyPrism.Services.Core.Integration;

/// <summary>
/// Manages Discord Rich Presence integration for displaying game/launcher status.
/// Implements <see cref="IDisposable"/> to properly cleanup Discord RPC connection.
/// </summary>
public interface IDiscordService : IDisposable
{
    /// <summary>
    /// Initializes the Discord RPC client and establishes connection to Discord.
    /// Should be called once during application startup.
    /// </summary>
    void Initialize();
    
    /// <summary>
    /// Updates the Discord Rich Presence with the specified state and details.
    /// </summary>
    /// <param name="state">The current presence state (e.g., InLauncher, Downloading, Playing).</param>
    /// <param name="details">Optional additional details to display in the presence.</param>
    /// <param name="progress">Optional progress percentage (0-100) for download/update operations.</param>
    void SetPresence(DiscordService.PresenceState state, string? details = null, int? progress = null);
    
    /// <summary>
    /// Clears the current Discord Rich Presence, removing HyPrism from the user's status.
    /// </summary>
    void ClearPresence();
}
