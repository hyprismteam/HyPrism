using HyPrism.Models;

namespace HyPrism.Services.User;

/// <summary>
/// Provides skin management functionality including protection from game overwrites,
/// backup/restore operations, and orphaned skin recovery.
/// </summary>
public interface ISkinService : IDisposable
{
    /// <summary>
    /// Starts protection of the skin file from being overwritten by the game.
    /// Sets the file as read-only and monitors for changes.
    /// </summary>
    /// <param name="profile">The profile whose skin is being protected.</param>
    /// <param name="skinCachePath">The path to the skin cache file to protect.</param>
    void StartSkinProtection(Profile profile, string skinCachePath);

    /// <summary>
    /// Stops the skin file protection and removes the read-only flag.
    /// </summary>
    void StopSkinProtection();

    /// <summary>
    /// Attempts to recover orphaned skin data on application startup.
    /// Handles the scenario where config was reset but skin files still exist.
    /// </summary>
    void TryRecoverOrphanedSkinOnStartup();

    /// <summary>
    /// Searches for orphaned skin UUIDs in the game cache that are not mapped to any user.
    /// </summary>
    /// <returns>The UUID of an orphaned skin, or null if none found.</returns>
    string? FindOrphanedSkinUuid();

    /// <summary>
    /// Recovers orphaned skin data by copying it to the specified UUID.
    /// </summary>
    /// <param name="currentUuid">The current user's UUID to copy the skin data to.</param>
    /// <returns>True if skin data was recovered successfully; otherwise, false.</returns>
    bool RecoverOrphanedSkinData(string currentUuid);

    /// <summary>
    /// Creates a backup of skin data for the specified UUID in the profile directory.
    /// </summary>
    /// <param name="uuid">The UUID whose skin data should be backed up.</param>
    void BackupProfileSkinData(string uuid);

    /// <summary>
    /// Restores previously backed up skin data for a profile.
    /// </summary>
    /// <param name="profile">The profile whose skin data should be restored.</param>
    void RestoreProfileSkinData(Profile profile);

    /// <summary>
    /// Copies skin data from a UUID to a profile directory.
    /// </summary>
    /// <param name="uuid">The source UUID.</param>
    /// <param name="profileDir">The destination profile directory.</param>
    void CopyProfileSkinData(string uuid, string profileDir);
}
