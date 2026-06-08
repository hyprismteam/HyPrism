using HyPrism.Models;

namespace HyPrism.Services.User;

/// <summary>
/// Manages user identities including UUID-to-username mappings,
/// identity switching, and orphaned skin recovery.
/// </summary>
public interface IUserIdentityService
{
    /// <summary>
    /// Gets the UUID for a specific username, creating a new one if it doesn't exist.
    /// Attempts to recover orphaned skin data when creating a new UUID.
    /// </summary>
    /// <param name="username">The username to get the UUID for.</param>
    /// <returns>The UUID associated with the username.</returns>
    string GetUuidForUser(string username);

    /// <summary>
    /// Gets the UUID for the current user based on the current nickname.
    /// </summary>
    /// <returns>The current user's UUID.</returns>
    string GetCurrentUuid();

    /// <summary>
    /// Gets all username-to-UUID mappings with information about which is current.
    /// </summary>
    /// <returns>A list of UUID mappings with username, UUID, and current status.</returns>
    List<UuidMapping> GetAllUuidMappings();

    /// <summary>
    /// Sets a custom UUID for a specific username.
    /// </summary>
    /// <param name="username">The username to set the UUID for.</param>
    /// <param name="uuid">The UUID to assign to the username.</param>
    /// <returns>True if the UUID was set successfully; otherwise, false.</returns>
    bool SetUuidForUser(string username, string uuid);

    /// <summary>
    /// Deletes the UUID mapping for a specific username.
    /// Cannot delete the UUID for the current user.
    /// </summary>
    /// <param name="username">The username whose UUID mapping should be deleted.</param>
    /// <returns>True if the mapping was deleted successfully; otherwise, false.</returns>
    bool DeleteUuidForUser(string username);

    /// <summary>
    /// Generates a new random UUID for the current user.
    /// Warning: This will change the player's identity and they will lose their skin.
    /// </summary>
    /// <returns>The newly generated UUID.</returns>
    string ResetCurrentUserUuid();

    /// <summary>
    /// Switches to an existing username and its associated UUID.
    /// Creates a new UUID if the username doesn't exist.
    /// </summary>
    /// <param name="username">The username to switch to.</param>
    /// <returns>The UUID for the username, or null if the switch failed.</returns>
    string? SwitchToUsername(string username);

    /// <summary>
    /// Attempts to recover orphaned skin data and associate it with the current user.
    /// Useful when a user's config was reset but skin data still exists.
    /// </summary>
    /// <returns>True if skin data was recovered; otherwise, false.</returns>
    bool RecoverOrphanedSkinData();

    /// <summary>
    /// Gets the UUID of any orphaned skin found in the game cache.
    /// </summary>
    /// <returns>The orphaned skin's UUID, or null if none found.</returns>
    string? GetOrphanedSkinUuid();
}
