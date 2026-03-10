using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Game.Instance;

namespace HyPrism.Services.User;

/// <summary>
/// Manages user identities (UUID and username mappings).
/// Handles UUID generation, username switching, and orphaned skin recovery.
/// Delegates profile storage to <see cref="IProfileService"/>.
/// </summary>
public class UserIdentityService : IUserIdentityService
{
    private readonly IConfigService _configService;
    private readonly ISkinService _skinService;
    private readonly IInstanceService _instanceService;
    private readonly IProfileService _profileService;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserIdentityService"/> class.
    /// </summary>
    /// <param name="configService">The configuration service.</param>
    /// <param name="skinService">The skin management service.</param>
    /// <param name="instanceService">The game instance service.</param>
    /// <param name="profileService">The profile service for UUID/name lookups.</param>
    public UserIdentityService(
        IConfigService configService,
        ISkinService skinService,
        IInstanceService instanceService,
        IProfileService profileService)
    {
        _configService = configService;
        _skinService = skinService;
        _instanceService = instanceService;
        _profileService = profileService;
    }

    /// <inheritdoc/>
    public string GetUuidForUser(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return _profileService.GetCurrentUuid();

        // Look up UUID from profiles (case-insensitive)
        var existingProfile = _profileService.GetProfiles()
            .FirstOrDefault(p => p.Name.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (existingProfile != null)
            return existingProfile.UUID;

        // Check current active profile
        if (_profileService.GetNick().Equals(username, StringComparison.OrdinalIgnoreCase))
            return _profileService.GetCurrentUuid();

        // Before creating a new UUID, check if there are orphaned skin files we should adopt
        var orphanedUuid = _skinService.FindOrphanedSkinUuid();
        if (!string.IsNullOrEmpty(orphanedUuid))
        {
            Logger.Info("UUID", $"Recovered orphaned skin UUID for user '{username}': {orphanedUuid}");
            _profileService.SetUUID(orphanedUuid);
            return orphanedUuid;
        }

        // No orphaned skins found — create a new UUID
        var newUuid = Guid.NewGuid().ToString();
        _profileService.SetUUID(newUuid);
        Logger.Info("UUID", $"Created new UUID for user '{username}': {newUuid}");

        return newUuid;
    }

    /// <inheritdoc/>
    public string GetCurrentUuid() => _profileService.GetCurrentUuid();

    /// <inheritdoc/>
    public List<UuidMapping> GetAllUuidMappings()
    {
        var currentNick = _profileService.GetNick();

        return _profileService.GetProfiles()
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

        // If it's the current active profile, update through IProfileService
        if (username.Equals(_profileService.GetNick(), StringComparison.OrdinalIgnoreCase))
        {
            _profileService.SetUUID(parsed.ToString());
            Logger.Info("UUID", $"Set UUID for current user '{username}': {parsed}");
            return true;
        }

        Logger.Warning("UUID", $"Cannot set UUID for non-active user '{username}' — use ProfileManagementService.UpdateProfile instead");
        return false;
    }

    /// <inheritdoc/>
    public bool DeleteUuidForUser(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;

        // Don't allow deleting current user's UUID
        if (username.Equals(_profileService.GetNick(), StringComparison.OrdinalIgnoreCase))
        {
            Logger.Warning("UUID", $"Cannot delete UUID for current user '{username}'");
            return false;
        }

        var profile = _profileService.GetProfiles()
            .FirstOrDefault(p => p.Name.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (profile == null) return false;

        var deleted = _profileService.DeleteProfile(profile.Id);
        if (deleted)
            Logger.Info("UUID", $"Deleted profile for user '{username}'");

        return deleted;
    }

    /// <inheritdoc/>
    public string ResetCurrentUserUuid()
    {
        var newUuid = Guid.NewGuid().ToString();
        _profileService.SetUUID(newUuid);
        Logger.Info("UUID", $"Reset UUID for current user '{_profileService.GetNick()}': {newUuid}");
        return newUuid;
    }

    /// <inheritdoc/>
    public string? SwitchToUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;

        var existingProfile = _profileService.GetProfiles()
            .FirstOrDefault(p => p.Name.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (existingProfile != null)
        {
            _profileService.SwitchProfile(existingProfile.Id);
            Logger.Info("UUID", $"Switched to existing user '{existingProfile.Name}' with UUID {existingProfile.UUID}");
            return existingProfile.UUID;
        }

        // Username doesn't exist — create new UUID and set as current
        var newUuid = Guid.NewGuid().ToString();
        _profileService.SetNick(username);
        _profileService.SetUUID(newUuid);
        Logger.Info("UUID", $"Created new user '{username}' with UUID {newUuid}");
        return newUuid;
    }

    /// <inheritdoc/>
    public bool RecoverOrphanedSkinData()
    {
        try
        {
            var currentUuid = _profileService.GetCurrentUuid();
            var orphanedUuid = _skinService.FindOrphanedSkinUuid();

            if (string.IsNullOrEmpty(orphanedUuid))
            {
                Logger.Info("UUID", "No orphaned skin data found to recover");
                return false;
            }

            // Resolve instance path for skin cache
            string? versionPath = null;
            var selected = _instanceService.GetSelectedInstance();
            if (selected != null)
                versionPath = _instanceService.GetInstancePathById(selected.Id);

            if (string.IsNullOrWhiteSpace(versionPath))
                versionPath = _instanceService.GetInstalledInstances().FirstOrDefault()?.Path;

            if (string.IsNullOrWhiteSpace(versionPath))
            {
                Logger.Info("UUID", "No existing instance found, skipping orphaned skin recovery copy");
                return false;
            }

            var userDataPath = _instanceService.GetInstanceUserDataPath(versionPath);
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

            _skinService.BackupProfileSkinData(currentUuid);

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("UUID", $"Failed to recover orphaned skin data: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public string? GetOrphanedSkinUuid() => _skinService.FindOrphanedSkinUuid();
}
