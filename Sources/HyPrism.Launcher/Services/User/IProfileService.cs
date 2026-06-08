using HyPrism.Models;

namespace HyPrism.Services.User;

/// <summary>
/// Provides basic profile management operations including nickname/UUID handling, avatar management, and profile CRUD operations.
/// </summary>
public interface IProfileService
{
    /// <summary>
    /// Gets the current user's nickname.
    /// </summary>
    /// <returns>The current nickname.</returns>
    string GetNick();

    /// <summary>
    /// Sets the current user's nickname.
    /// </summary>
    /// <param name="nick">The new nickname (max 16 characters).</param>
    /// <returns>True if the nickname was set successfully; otherwise, false.</returns>
    bool SetNick(string nick);

    /// <summary>
    /// Gets the current user's UUID.
    /// </summary>
    /// <returns>The current UUID.</returns>
    string GetUUID();

    /// <summary>
    /// Sets the current user's UUID.
    /// </summary>
    /// <param name="uuid">The new UUID.</param>
    /// <returns>True if the UUID was set successfully; otherwise, false.</returns>
    bool SetUUID(string uuid);

    /// <summary>
    /// Gets the UUID for the current user, generating a new one if none exists.
    /// </summary>
    /// <returns>The current UUID.</returns>
    string GetCurrentUuid();

    /// <summary>
    /// Generates a new random UUID.
    /// </summary>
    /// <returns>A newly generated UUID string.</returns>
    string GenerateNewUuid();

    /// <summary>
    /// Gets the path to the current user's avatar preview image.
    /// </summary>
    /// <returns>The file path to the avatar image, or null if no avatar exists.</returns>
    string? GetAvatarPreview();

    /// <summary>
    /// Gets the path to the avatar preview image for a specific UUID.
    /// </summary>
    /// <param name="uuid">The UUID to get the avatar for.</param>
    /// <returns>The file path to the avatar image, or null if no avatar exists.</returns>
    string? GetAvatarPreviewForUUID(string uuid);

    /// <summary>
    /// Clears all cached avatar images.
    /// </summary>
    /// <returns>True if the cache was cleared successfully; otherwise, false.</returns>
    bool ClearAvatarCache();

    /// <summary>
    /// Gets the directory path for storing avatar images for the current user.
    /// </summary>
    /// <returns>The absolute path to the avatar directory.</returns>
    string GetAvatarDirectory();

    /// <summary>
    /// Opens the avatar directory in the system file explorer.
    /// </summary>
    /// <returns>True if the directory was opened successfully; otherwise, false.</returns>
    bool OpenAvatarDirectory();

    /// <summary>
    /// Gets all available user profiles.
    /// </summary>
    /// <returns>A list of all user profiles.</returns>
    List<Profile> GetProfiles();

    /// <summary>
    /// Creates a new profile with the specified name.
    /// </summary>
    /// <param name="name">The profile name.</param>
    /// <param name="uuid">Optional UUID for the profile; a new one is generated if null.</param>
    /// <returns>True if the profile was created successfully; otherwise, false.</returns>
    bool CreateProfile(string name, string? uuid = null);

    /// <summary>
    /// Deletes a profile by its unique identifier.
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to delete.</param>
    /// <returns>True if the profile was deleted successfully; otherwise, false.</returns>
    bool DeleteProfile(string profileId);

    /// <summary>
    /// Switches to a profile by its unique identifier.
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to switch to.</param>
    /// <returns>True if the switch was successful; otherwise, false.</returns>
    bool SwitchProfile(string profileId);

    /// <summary>
    /// Saves the current nickname and UUID as a new profile.
    /// </summary>
    /// <returns>True if the profile was saved successfully; otherwise, false.</returns>
    bool SaveCurrentAsProfile();

    /// <summary>
    /// Gets the file system path for a specific profile.
    /// </summary>
    /// <param name="profile">The profile to get the path for.</param>
    /// <returns>The absolute path to the profile's directory.</returns>
    string GetProfilePath(Profile profile);
}
