// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Models;
using HyPrism.Core.Infrastructure;
using HyPrism.Core.Game.Instances;

namespace HyPrism.Core.Accounts;

/// <summary>
/// Manages user identities (UUID and username mappings).
/// Handles profile identity lookup, profile switching, and orphaned skin recovery.
/// Delegates profile storage to <see cref="IProfileManager"/>
/// </summary>
public class UserIdentityProvider : IUserIdentityProvider
{
    private readonly ISkinRepository _skins;
    private readonly IInstanceRepository _instances;
    private readonly IProfileManager _profiles;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserIdentityProvider"/> class
    /// </summary>
    /// <param name="skins">The skin management service</param>
    /// <param name="instances">The game instance service</param>
    /// <param name="profiles">The profile service for UUID/name lookups</param>
    public UserIdentityProvider(
        ISkinRepository skins,
        IInstanceRepository instances,
        IProfileManager profiles)
    {
        _skins = skins;
        _instances = instances;
        _profiles = profiles;
    }

    /// <inheritdoc/>
    public string GetUuidForUser(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return _profiles.GetCurrentUuid();

        // Look up UUID from profiles (case-insensitive)
        var existingProfile = _profiles.GetProfiles()
            .FirstOrDefault(p => p.Name.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (existingProfile != null)
            return existingProfile.UUID;

        return string.Empty;
    }

    /// <inheritdoc/>
    public string GetCurrentUuid() => _profiles.GetCurrentUuid();

    /// <inheritdoc/>
    public List<UuidMapping> GetAllUuidMappings()
    {
        var currentNick = _profiles.GetNick();

        return _profiles.GetProfiles()
            .Select(p => new UuidMapping
            {
                Username = p.Name,
                Uuid = p.UUID,
                IsCurrent = p.Name.Equals(currentNick, StringComparison.OrdinalIgnoreCase)
            })
            .ToList();
    }

    /// <inheritdoc/>
    public bool SetUuidForUser(string username, string uuid)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        if (!Guid.TryParse(uuid.Trim(), out var parsed)) return false;

        // If it's the current active profile, update through IProfileManager
        if (username.Equals(_profiles.GetNick(), StringComparison.OrdinalIgnoreCase))
        {
            var updated = _profiles.SetUUID(parsed.ToString());
            if (updated)
                Logger.Info("UUID", $"Set UUID for current user '{username}': {parsed}");
            return updated;
        }

        Logger.Warning("UUID", $"Cannot set UUID for non-active user '{username}'. Use JsonProfileRepository.UpdateProfile instead");
        return false;
    }

    /// <inheritdoc/>
    public bool DeleteUuidForUser(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;

        // Don't allow deleting current user's UUID
        if (username.Equals(_profiles.GetNick(), StringComparison.OrdinalIgnoreCase))
        {
            Logger.Warning("UUID", $"Cannot delete UUID for current user '{username}'");
            return false;
        }

        var profile = _profiles.GetProfiles()
            .FirstOrDefault(p => p.Name.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (profile == null) return false;

        var deleted = _profiles.DeleteProfile(profile.Id);
        if (deleted)
            Logger.Info("UUID", $"Deleted profile for user '{username}'");

        return deleted;
    }

    /// <inheritdoc/>
    public string ResetCurrentUserUuid()
    {
        if (string.IsNullOrWhiteSpace(_profiles.GetCurrentUuid()))
            return string.Empty;

        var newUuid = Guid.NewGuid().ToString();
        if (!_profiles.SetUUID(newUuid))
            return string.Empty;
        Logger.Info("UUID", $"Reset UUID for current user '{_profiles.GetNick()}': {newUuid}");
        return newUuid;
    }

    /// <inheritdoc/>
    public string? SwitchToUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;

        var existingProfile = _profiles.GetProfiles()
            .FirstOrDefault(p => p.Name.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (existingProfile != null)
        {
            _profiles.SwitchProfile(existingProfile.Id);
            Logger.Info("UUID", $"Switched to existing user '{existingProfile.Name}' with UUID {existingProfile.UUID}");
            return existingProfile.UUID;
        }

        // Create a complete profile when the username does not exist
        var newUuid = Guid.NewGuid().ToString();
        if (!_profiles.CreateProfile(username, newUuid))
            return null;

        var createdProfile = _profiles.GetProfiles()
            .FirstOrDefault(profile => profile.UUID == newUuid);
        if (createdProfile is null || !_profiles.SwitchProfile(createdProfile.Id))
            return null;

        Logger.Info("UUID", $"Created new user '{username}' with UUID {newUuid}");
        return newUuid;
    }

    /// <inheritdoc/>
    public bool RecoverOrphanedSkinData()
    {
        try
        {
            var currentUuid = _profiles.GetCurrentUuid();
            var orphanedUuid = _skins.FindOrphanedSkinUuid();

            if (string.IsNullOrEmpty(orphanedUuid))
            {
                Logger.Info("UUID", "No orphaned skin data found to recover");
                return false;
            }

            // Resolve instance path for skin cache
            string? versionPath = null;
            var selected = _instances.GetSelectedInstance();
            if (selected != null)
                versionPath = _instances.GetInstancePathById(selected.Id);

            if (string.IsNullOrWhiteSpace(versionPath))
                versionPath = _instances.GetInstalledInstances().FirstOrDefault()?.Path;

            if (string.IsNullOrWhiteSpace(versionPath))
            {
                Logger.Info("UUID", "No existing instance found, skipping orphaned skin recovery copy");
                return false;
            }

            var userDataPath = _instances.GetInstanceUserDataPath(versionPath);
            var skinCacheDir = Path.Combine(userDataPath, "CachedPlayerSkins");
            var avatarCacheDir = Path.Combine(userDataPath, "CachedAvatarPreviews");

            var currentSkinPath = Path.Combine(skinCacheDir, $"{currentUuid}.json");

            // If current user already has a skin, don't overwrite
            if (File.Exists(currentSkinPath))
            {
                Logger.Info("UUID", $"Current user already has skin data. Use SetUuidForUser to switch to the orphaned UUID: {orphanedUuid}");
                return false;
            }

            // Copy orphaned skin to current UUID
            var orphanSkinPath = Path.Combine(skinCacheDir, $"{orphanedUuid}.json");
            if (File.Exists(orphanSkinPath))
            {
                Directory.CreateDirectory(skinCacheDir);
                File.Copy(orphanSkinPath, currentSkinPath, true);
                Logger.Success("UUID", $"Copied orphaned skin from {orphanedUuid} to {currentUuid}");
            }

            // Copy orphaned avatar to current UUID
            var orphanAvatarPath = Path.Combine(avatarCacheDir, $"{orphanedUuid}.png");
            var currentAvatarPath = Path.Combine(avatarCacheDir, $"{currentUuid}.png");
            if (File.Exists(orphanAvatarPath))
            {
                Directory.CreateDirectory(avatarCacheDir);
                File.Copy(orphanAvatarPath, currentAvatarPath, true);
                Logger.Success("UUID", $"Copied orphaned avatar from {orphanedUuid} to {currentUuid}");
            }

            _skins.BackupProfileSkinData(currentUuid);

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("UUID", $"Failed to recover orphaned skin data: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public string? GetOrphanedSkinUuid() => _skins.FindOrphanedSkinUuid();
}
