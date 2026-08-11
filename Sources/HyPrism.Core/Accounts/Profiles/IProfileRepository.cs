// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Models;

namespace HyPrism.Core.Accounts;

/// <summary>
/// Provides functionality for managing user profiles including creation, deletion, switching, and data persistence
/// </summary>
public interface IProfileRepository
{
    /// <summary>
    /// Gets all available user profiles
    /// </summary>
    /// <returns>A list of all user profiles with valid names and UUIDs</returns>
    List<Profile> GetProfiles();

    /// <summary>
    /// Gets the index of the currently active profile
    /// </summary>
    /// <returns>The zero-based index of the active profile, or -1 if no profile is selected</returns>
    int GetActiveProfileIndex();

    /// <summary>
    /// Gets the ID of the currently active profile
    /// </summary>
    /// <returns>The profile ID string, or empty if no profile is selected</returns>
    string GetSelectedProfileId();

    /// <summary>
    /// Gets the currently active profile object
    /// </summary>
    /// <returns>The active <see cref="Profile"/>, or null if none is selected</returns>
    Profile? GetSelectedProfile();

    /// <summary>
    /// Creates a new profile with the specified name, UUID, and account type
    /// </summary>
    /// <param name="name">The profile name (1-16 characters)</param>
    /// <param name="uuid">The UUID for the profile</param>
    /// <param name="isOfficial">Whether the profile is linked to an official Hytale account</param>
    /// <returns>The created profile, or null if creation failed</returns>
    Profile? CreateProfile(string name, string uuid, bool isOfficial = false);

    /// <summary>
    /// Deletes a profile by its unique identifier
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to delete</param>
    /// <returns>True if the profile was successfully deleted; otherwise, false</returns>
    bool DeleteProfile(string profileId);

    /// <summary>
    /// Switches to a profile at the specified index
    /// </summary>
    /// <param name="index">The zero-based index of the profile to switch to</param>
    /// <returns>True if the switch was successful; otherwise, false</returns>
    bool SwitchProfile(int index);

    /// <summary>
    /// Switches to a profile by its unique ID
    /// </summary>
    /// <param name="profileId">The profile ID to switch to</param>
    /// <returns>True if the switch was successful; otherwise, false</returns>
    bool SwitchProfile(string profileId);

    /// <summary>
    /// Updates an existing profile with new name and/or UUID
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to update</param>
    /// <param name="newName">The new name for the profile, or null to keep existing</param>
    /// <param name="newUuid">The new UUID for the profile, or null to keep existing</param>
    /// <returns>True if the update was successful; otherwise, false</returns>
    bool UpdateProfile(string profileId, string? newName, string? newUuid);

    /// <summary>
    /// Saves the current UUID and nickname as a new profile
    /// </summary>
    /// <returns>The created or updated profile, or null if save failed</returns>
    Profile? SaveCurrentAsProfile();

    /// <summary>
    /// Duplicates an existing profile including all user data (mods, UserData folder)
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to duplicate</param>
    /// <returns>The newly created profile, or null if duplication failed</returns>
    Profile? DuplicateProfile(string profileId);

    /// <summary>
    /// Duplicates an existing profile without copying user data (only profile settings)
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to duplicate</param>
    /// <returns>The newly created profile, or null if duplication failed</returns>
    Profile? DuplicateProfileWithoutData(string profileId);

    /// <summary>
    /// Resolves the current profile folder and creates its metadata when needed
    /// </summary>
    /// <returns>The absolute profile folder path, or <see langword="null"/> when no profile is available</returns>
    string? GetCurrentProfileFolder();

    /// <summary>
    /// Initializes compatibility handling for profile mods storage.
    /// Ensures instance-local <c>UserData/Mods</c> exists and migrates legacy profile links when detected
    /// </summary>
    void InitializeProfileModsSymlink();

    /// <summary>
    /// Gets the path to the profiles root folder
    /// </summary>
    /// <returns>The absolute path to the profiles folder</returns>
    string GetProfilesFolder();
}
