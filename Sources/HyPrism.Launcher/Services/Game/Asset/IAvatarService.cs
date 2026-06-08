namespace HyPrism.Services.Game.Asset;

/// <summary>
/// Manages user avatar cache and preview images for game instances.
/// Handles persistent avatar backup and cache cleanup across all installed instances.
/// </summary>
public interface IAvatarService
{
    /// <summary>
    /// Event raised when the user's avatar image has been updated.
    /// The string parameter is the full path to the updated avatar file.
    /// </summary>
    event Action<string>? AvatarUpdated;

    /// <summary>
    /// Gets the path to the persistent avatar backup file for the specified UUID.
    /// </summary>
    /// <param name="uuid">The player UUID whose backup path should be resolved.</param>
    /// <returns>The absolute path to the backup PNG file (may not exist yet).</returns>
    string GetAvatarBackupPath(string uuid);

    /// <summary>
    /// Copies the latest avatar from the game's <c>CachedAvatarPreviews</c> to persistent backup.
    /// Searches all installed instances and picks the most recently written avatar file.
    /// Should be called after the game exits to capture the most recent avatar.
    /// </summary>
    /// <param name="uuid">The player UUID to back up.</param>
    /// <returns><see langword="true"/> if the avatar was found and backed up; otherwise <see langword="false"/>.</returns>
    bool BackupAvatar(string uuid);

    /// <summary>
    /// Clears the avatar cache for the specified UUID.
    /// Removes the avatar from both persistent backup and all game instance caches.
    /// </summary>
    /// <param name="uuid">The player UUID whose avatar cache should be cleared.</param>
    /// <returns><see langword="true"/> if the cache was cleared successfully; otherwise <see langword="false"/>.</returns>
    bool ClearAvatarCache(string uuid);
}
