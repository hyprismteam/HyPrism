using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Game;
using HyPrism.Services.Game.Instance;
namespace HyPrism.Services.User;

/// <summary>
/// Manages profile operations: creation, deletion, switching, and profile folder/symlink management.
/// </summary>
public class ProfileManagementService : IProfileManagementService
{
    #region Fields and Constructor
    private readonly string _appDir;
    private readonly IConfigService _configService;
    private readonly ISkinService _skinService;
    private readonly IInstanceService _instanceService;
    private readonly IUserIdentityService _userIdentityService;
    private bool _profileFolderMigrationAttempted;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileManagementService"/> class.
    /// </summary>
    /// <param name="appPath">The application path configuration.</param>
    /// <param name="configService">The configuration service.</param>
    /// <param name="skinService">The skin management service.</param>
    /// <param name="instanceService">The game instance service.</param>
    /// <param name="userIdentityService">The user identity service.</param>
    public ProfileManagementService(
        AppPathConfiguration appPath,
        IConfigService configService,
        ISkinService skinService,
        IInstanceService instanceService,
        IUserIdentityService userIdentityService)
    {
        _appDir = appPath.AppDir;
        _configService = configService;
        _skinService = skinService;
        _instanceService = instanceService;
        _userIdentityService = userIdentityService;

        EnsureProfileStorageUpgraded();
    }

    #endregion

    #region Profile cache (profiles.json)

    private static readonly JsonSerializerOptions _profileJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    #endregion

    /// <summary>Returns the path to the profile cache file inside the profiles folder.</summary>
    private string GetProfileCachePath() => Path.Combine(GetProfilesFolder(), "profiles.json");

    /// <summary>
    /// Loads the profile list from profiles.json.
    /// On first run migrates from the deprecated config.Profiles field.
    /// </summary>
    private List<Profile> LoadProfilesFromCache()
    {
        var path = GetProfileCachePath();
        if (File.Exists(path))
        {
            try
            {
                return JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(path), _profileJsonOpts) ?? new();
            }
            catch (Exception ex)
            {
                Logger.Warning("Profile", $"Failed to read profiles.json: {ex.Message}");
            }
        }

        // Migration: seed from deprecated config.Profiles if present
        #pragma warning disable CS0618
        var config = _configService.Configuration;
        if (config.Profiles?.Count > 0)
        {
            Logger.Info("Profile", $"Migrating {config.Profiles.Count} profiles from config to profiles.json");
            var migrated = config.Profiles.ToList();
            SaveProfilesToCache(migrated);
            config.Profiles = null;

            // Migrate ActiveProfileIndex → SelectedProfileId
            if (config.ActiveProfileIndex >= 0 && config.ActiveProfileIndex < migrated.Count
                && string.IsNullOrEmpty(config.SelectedProfileId))
            {
                config.SelectedProfileId = migrated[config.ActiveProfileIndex].Id;
            }
            _configService.SaveConfig();
            return migrated;
        }
        #pragma warning restore CS0618

        return new List<Profile>();
    }

    /// <summary>Saves the profile list to profiles.json.</summary>
    private void SaveProfilesToCache(IEnumerable<Profile> profiles)
    {
        try
        {
            var list = profiles.ToList();
            var dir = GetProfilesFolder();
            Directory.CreateDirectory(dir);
            File.WriteAllText(GetProfileCachePath(), JsonSerializer.Serialize(list, _profileJsonOpts));
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to save profiles.json: {ex.Message}");
        }
    }

    /// <summary>Gets the ID of the currently active profile.</summary>
    public string GetSelectedProfileId() => _configService.Configuration.SelectedProfileId ?? "";

    /// <summary>Gets the currently active profile object, or null if none is selected.</summary>
    public Profile? GetSelectedProfile()
    {
        var id = GetSelectedProfileId();
        if (string.IsNullOrEmpty(id)) return null;
        return LoadProfilesFromCache().FirstOrDefault(p => p.Id == id);
    }


    /// <inheritdoc/>
    /// <remarks>Filters out any profiles with null/empty names or UUIDs.</remarks>
    public List<Profile> GetProfiles()
    {
        EnsureProfileStorageUpgraded();

        var profiles = LoadProfilesFromCache();

        // Clean up any null/empty profiles
        var valid = profiles
            .Where(p => !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.UUID))
            .ToList();

        if (valid.Count != profiles.Count)
        {
            Logger.Info("Profile", $"Cleaned up {profiles.Count - valid.Count} invalid profiles");
            SaveProfilesToCache(valid);
        }

        Logger.Info("Profile", $"GetProfiles returning {valid.Count} profiles");
        return valid;
    }

    private void EnsureProfileStorageUpgraded()
    {
        if (_profileFolderMigrationAttempted)
            return;

        _profileFolderMigrationAttempted = true;

        try
        {
            var profiles = LoadProfilesFromCache(); // also triggers config migration

            bool changed = false;
            foreach (var profile in profiles)
            {
                if (string.IsNullOrWhiteSpace(profile.Id) || !Guid.TryParse(profile.Id, out _))
                {
                    profile.Id = Guid.NewGuid().ToString();
                    changed = true;
                    Logger.Info("Profile", $"Assigned missing profile ID for '{profile.Name}': {profile.Id}");
                }
                UtilityService.GetProfileFolderPath(_appDir, profile, createIfMissing: false, migrateLegacyByName: true);
            }

            ProfileMigrationService.MigrateUnresolvedFolders(GetProfilesFolder(), profiles, _appDir);

            if (ProfileMigrationService.MigrateOrphanedFolders(GetProfilesFolder(), profiles))
                changed = true;

            if (changed)
                SaveProfilesToCache(profiles);
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Profile storage migration check failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public int GetActiveProfileIndex()
    {
        var id = GetSelectedProfileId();
        if (string.IsNullOrEmpty(id)) return -1;
        return LoadProfilesFromCache().FindIndex(p => p.Id == id);
    }

    /// <inheritdoc/>
    /// <remarks>Validates name length (1-16 characters) and UUID format before creation.</remarks>
    public Profile? CreateProfile(string name, string uuid)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(uuid))
            {
                Logger.Warning("Profile", $"Cannot create profile with empty name or UUID");
                return null;
            }
            
            // Validate name length (1-16 characters)
            var trimmedName = name.Trim();
            if (trimmedName.Length < 1 || trimmedName.Length > 16)
            {
                Logger.Warning("Profile", $"Invalid name length: {trimmedName.Length} (must be 1-16 chars)");
                return null;
            }
            
            // Validate UUID format
            if (!Guid.TryParse(uuid.Trim(), out var parsedUuid))
            {
                Logger.Warning("Profile", $"Invalid UUID format: {uuid}");
                return null;
            }
            
            var profile = new Profile
            {
                Id = Guid.NewGuid().ToString(),
                UUID = parsedUuid.ToString(),
                Name = trimmedName,
                CreatedAt = DateTime.UtcNow
            };
            
            var profiles = LoadProfilesFromCache();
            profiles.Add(profile);

            // Auto-activate the first profile created, or if none is selected yet
            var config = _configService.Configuration;
            if (profiles.Count == 1 || string.IsNullOrEmpty(config.SelectedProfileId))
            {
                config.SelectedProfileId = profile.Id;
                #pragma warning disable CS0618
                config.UUID = profile.UUID;
                config.Nick = profile.Name;
                #pragma warning restore CS0618
                Logger.Info("Profile", $"Auto-activated new profile '{profile.Name}'");
            }

            SaveProfilesToCache(profiles);
            _configService.SaveConfig();
            Logger.Info("Profile", $"Profile added to list. Total profiles: {profiles.Count}");
            Logger.Info("Profile", $"Config saved to disk");

            // Save profile to disk folder
            SaveProfileToDisk(profile);

            Logger.Success("Profile", $"Created profile '{trimmedName}' with UUID {parsedUuid}");
            return profile;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to create profile: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Updates SelectedProfileId if the deleted profile was active.</remarks>
    public bool DeleteProfile(string profileId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId))
                return false;

            var profiles = LoadProfilesFromCache();
            var profile = profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
                return false;

            profiles.Remove(profile);
            SaveProfilesToCache(profiles);

            // If the deleted profile was active, clear selection
            var config = _configService.Configuration;
            if (config.SelectedProfileId == profileId)
            {
                config.SelectedProfileId = "";
                #pragma warning disable CS0618
                config.UUID = "";
                config.Nick = "";
                #pragma warning restore CS0618
            }
            _configService.SaveConfig();

            // Delete profile folder from disk
            DeleteProfileFromDisk(profileId, profile.Name);

            Logger.Success("Profile", $"Deleted profile '{profile.Name}'");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to delete profile: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Backwards-compat wrapper — switches by index position in the cached list.</remarks>
    public bool SwitchProfile(int index)
    {
        try
        {
            var profiles = LoadProfilesFromCache();
            if (index < 0 || index >= profiles.Count)
                return false;
            return SwitchProfile(profiles[index].Id);
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to switch profile (by index): {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Backups current profile's skin data and restores the new profile's skin data.</remarks>
    public bool SwitchProfile(string profileId)
    {
        try
        {
            var profiles = LoadProfilesFromCache();
            var profile = profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
                return false;

            // Backup current profile's skin data before switching
            var currentUuid = _userIdentityService.GetCurrentUuid();
            if (!string.IsNullOrWhiteSpace(currentUuid))
                _skinService.BackupProfileSkinData(currentUuid);

            // Restore skin data for the incoming profile
            _skinService.RestoreProfileSkinData(profile);

            var config = _configService.Configuration;
            config.SelectedProfileId = profile.Id;
            #pragma warning disable CS0618
            config.UUID = profile.UUID;
            config.Nick = profile.Name;
            #pragma warning restore CS0618

            // Auth domain handling
            if (profile.IsOfficial)
            {
                config.AuthDomain = "sessions.hytale.com";
                Logger.Info("Profile", "Official profile selected: auth domain switched to sessions.hytale.com");
            }
            else if (config.AuthDomain == "sessions.hytale.com")
            {
                config.AuthDomain = "";
                Logger.Info("Profile", "Non-official profile selected: cleared official auth domain");
            }

            EnsureInstanceModsDirectory(profile);
            _configService.SaveConfig();

            Logger.Success("Profile", $"Switched to profile '{profile.Name}'");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to switch profile: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public bool UpdateProfile(string profileId, string? newName, string? newUuid)
    {
        try
        {
            var profiles = LoadProfilesFromCache();
            var profile = profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
                return false;

            if (!string.IsNullOrWhiteSpace(newName))
                profile.Name = newName.Trim();

            if (!string.IsNullOrWhiteSpace(newUuid) && Guid.TryParse(newUuid.Trim(), out var parsedUuid))
                profile.UUID = parsedUuid.ToString();

            SaveProfilesToCache(profiles);

            // If this is the active profile, also update config UUID/Nick for legacy compat
            var config = _configService.Configuration;
            if (config.SelectedProfileId == profileId)
            {
                #pragma warning disable CS0618
                config.UUID = profile.UUID;
                config.Nick = profile.Name;
                #pragma warning restore CS0618
                _configService.SaveConfig();
            }

            // Update profile on disk
            UpdateProfileOnDisk(profile);

            Logger.Success("Profile", $"Updated profile '{profile.Name}'");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to update profile: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Updates existing profile if UUID already exists, otherwise creates new.</remarks>
    public Profile? SaveCurrentAsProfile()
    {
        var config = _configService.Configuration;
        #pragma warning disable CS0618
        var uuid = config.UUID;
        var name = config.Nick;
        #pragma warning restore CS0618

        if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(name))
            return null;

        var profiles = LoadProfilesFromCache();
        var existing = profiles.FirstOrDefault(p => p.UUID == uuid);
        if (existing != null)
        {
            existing.Name = name;
            SaveProfilesToCache(profiles);
            UpdateProfileOnDisk(existing);
            return existing;
        }

        return CreateProfile(name, uuid);
    }

    /// <inheritdoc/>
    /// <remarks>Copies UserData folder, mods folder, and skin data from the source profile.</remarks>
    public Profile? DuplicateProfile(string profileId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                Logger.Warning("Profile", "Cannot duplicate profile with empty ID");
                return null;
            }

            var allProfiles = LoadProfilesFromCache();
            var sourceProfile = allProfiles.FirstOrDefault(p => p.Id == profileId);
            if (sourceProfile == null)
            {
                Logger.Warning("Profile", $"Profile not found: {profileId}");
                return null;
            }

            var newUuid = Guid.NewGuid().ToString();
            var newName = $"{sourceProfile.Name} Copy";
            int copyCount = 1;
            while (allProfiles.Any(p => p.Name == newName)) { copyCount++; newName = $"{sourceProfile.Name} Copy {copyCount}"; }

            var newProfile = new Profile { Id = Guid.NewGuid().ToString(), UUID = newUuid, Name = newName, CreatedAt = DateTime.UtcNow };
            allProfiles.Add(newProfile);
            SaveProfilesToCache(allProfiles);
            SaveProfileToDisk(newProfile);
            
            // Copy source profile's mods folder to new profile
            try
            {
                var sourceModsPath = GetProfileModsFolder(sourceProfile);
                var destModsPath = GetProfileModsFolder(newProfile);
                
                if (Directory.Exists(sourceModsPath))
                {
                    CopyDirectory(sourceModsPath, destModsPath);
                    Logger.Info("Profile", $"Copied mods from '{sourceProfile.Name}' to '{newProfile.Name}'");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Profile", $"Failed to copy mods during duplication: {ex.Message}");
            }
            
            // Copy UserData from source profile if it exists
            try
            {
                var versionPath = TryGetCurrentExistingInstancePath();
                if (string.IsNullOrWhiteSpace(versionPath))
                {
                    Logger.Info("Profile", "No existing instance selected, skipping UserData copy during duplication");
                }
                else
                {
                    var userDataPath = _instanceService.GetInstanceUserDataPath(versionPath);
                
                    if (Directory.Exists(userDataPath))
                    {
                        var sourceProfileFolder = UtilityService.GetProfileFolderPath(_appDir, sourceProfile);
                        var sourceUserDataBackup = Path.Combine(sourceProfileFolder, "UserData");
                        var destProfileFolder = UtilityService.GetProfileFolderPath(_appDir, newProfile);
                        var destUserDataBackup = Path.Combine(destProfileFolder, "UserData");
                        
                        // If source profile has a UserData backup, copy it
                        if (Directory.Exists(sourceUserDataBackup))
                        {
                            CopyDirectory(sourceUserDataBackup, destUserDataBackup);
                            Logger.Info("Profile", $"Copied UserData from '{sourceProfile.Name}' to '{newProfile.Name}'");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Profile", $"Failed to copy UserData during duplication: {ex.Message}");
            }
            
            // Copy skin/avatar data
            try
            {
                var sourceProfileDir = UtilityService.GetProfileFolderPath(_appDir, sourceProfile);
                var destProfileDir = UtilityService.GetProfileFolderPath(_appDir, newProfile);
                
                // Copy skin.png if exists
                var sourceSkin = Path.Combine(sourceProfileDir, "skin.png");
                if (File.Exists(sourceSkin))
                {
                    File.Copy(sourceSkin, Path.Combine(destProfileDir, "skin.png"), true);
                }
                
                // Copy avatar.png if exists
                var sourceAvatar = Path.Combine(sourceProfileDir, "avatar.png");
                if (File.Exists(sourceAvatar))
                {
                    File.Copy(sourceAvatar, Path.Combine(destProfileDir, "avatar.png"), true);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Profile", $"Failed to copy skin/avatar during duplication: {ex.Message}");
            }
            
            Logger.Success("Profile", $"Duplicated profile '{sourceProfile.Name}' → '{newProfile.Name}'");
            return newProfile;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to duplicate profile: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Copies mods and skin/avatar but NOT UserData folder.</remarks>
    public Profile? DuplicateProfileWithoutData(string profileId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                Logger.Warning("Profile", "Cannot duplicate profile with empty ID");
                return null;
            }

            var allProfiles = LoadProfilesFromCache();
            var sourceProfile = allProfiles.FirstOrDefault(p => p.Id == profileId);
            if (sourceProfile == null)
            {
                Logger.Warning("Profile", $"Profile not found: {profileId}");
                return null;
            }

            var newUuid = Guid.NewGuid().ToString();
            var newName = $"{sourceProfile.Name} Copy";
            int copyCount = 1;
            while (allProfiles.Any(p => p.Name == newName)) { copyCount++; newName = $"{sourceProfile.Name} Copy {copyCount}"; }

            var newProfile = new Profile { Id = Guid.NewGuid().ToString(), UUID = newUuid, Name = newName, CreatedAt = DateTime.UtcNow };
            allProfiles.Add(newProfile);
            SaveProfilesToCache(allProfiles);

            // Save profile to disk
            SaveProfileToDisk(newProfile);
            
            // Copy source profile's mods folder to new profile
            try
            {
                var sourceModsPath = GetProfileModsFolder(sourceProfile);
                var destModsPath = GetProfileModsFolder(newProfile);
                
                if (Directory.Exists(sourceModsPath))
                {
                    CopyDirectory(sourceModsPath, destModsPath);
                    Logger.Info("Profile", $"Copied mods from '{sourceProfile.Name}' to '{newProfile.Name}'");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Profile", $"Failed to copy mods during duplication: {ex.Message}");
            }
            
            // Copy skin/avatar data (but NOT UserData)
            try
            {
                var sourceProfileDir = UtilityService.GetProfileFolderPath(_appDir, sourceProfile);
                var destProfileDir = UtilityService.GetProfileFolderPath(_appDir, newProfile);
                
                // Copy skin.png if exists
                var sourceSkin = Path.Combine(sourceProfileDir, "skin.png");
                if (File.Exists(sourceSkin))
                {
                    File.Copy(sourceSkin, Path.Combine(destProfileDir, "skin.png"), true);
                }
                
                // Copy avatar.png if exists
                var sourceAvatar = Path.Combine(sourceProfileDir, "avatar.png");
                if (File.Exists(sourceAvatar))
                {
                    File.Copy(sourceAvatar, Path.Combine(destProfileDir, "avatar.png"), true);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Profile", $"Failed to copy skin/avatar during duplication: {ex.Message}");
            }
            
            Logger.Success("Profile", $"Duplicated profile (without UserData) '{sourceProfile.Name}' → '{newProfile.Name}'");
            return newProfile;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to duplicate profile without data: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public bool OpenCurrentProfileFolder()
    {
        try
        {
            var profile = GetSelectedProfile();

            // Fallback: find profile matching current UUID
            if (profile == null)
            {
                #pragma warning disable CS0618
                var uuid = _configService.Configuration.UUID;
                #pragma warning restore CS0618
                if (!string.IsNullOrWhiteSpace(uuid))
                {
                    var profiles = LoadProfilesFromCache();
                    profile = profiles.FirstOrDefault(p => p.UUID == uuid);
                    if (profile != null)
                    {
                        _configService.Configuration.SelectedProfileId = profile.Id;
                        _configService.SaveConfig();
                        Logger.Info("Profile", $"Auto-activated profile '{profile.Name}' by UUID match");
                    }
                }
            }

            // Last resort: activate first profile
            if (profile == null)
            {
                var profiles = LoadProfilesFromCache();
                profile = profiles.FirstOrDefault();
                if (profile != null)
                {
                    _configService.Configuration.SelectedProfileId = profile.Id;
                    _configService.SaveConfig();
                    Logger.Info("Profile", $"Auto-activated first profile '{profile.Name}'");
                }
            }

            if (profile == null)
            {
                Logger.Warning("Profile", "No active profile to open folder for");
                return false;
            }
            var profileDir = UtilityService.GetProfileFolderPath(_appDir, profile);
            
            if (!Directory.Exists(profileDir))
            {
                Directory.CreateDirectory(profileDir);
                Logger.Info("Profile", $"Created profile folder: {profileDir}");
                
                // Write profile info to the folder so it always has matching data
                try
                {
                    var profileInfo = new
                    {
                        username = profile.Name,
                        uuid = profile.UUID,
                        createdAt = DateTime.UtcNow.ToString("o")
                    };
                    var infoPath = Path.Combine(profileDir, "profile.json");
                    var json = System.Text.Json.JsonSerializer.Serialize(profileInfo, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(infoPath, json);
                    Logger.Info("Profile", $"Created profile info file: {infoPath}");
                }
                catch (Exception infoEx)
                {
                    Logger.Warning("Profile", $"Failed to write profile info: {infoEx.Message}");
                }
            }
            
            // Open folder in file manager (cross-platform) — use ProcessStartInfo to handle paths with spaces
            var psi = new ProcessStartInfo();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi.FileName = "explorer.exe";
                psi.Arguments = $"\"{profileDir}\"";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                psi.FileName = "open";
                psi.Arguments = $"\"{profileDir}\"";
            }
            else // Linux
            {
                psi.FileName = "xdg-open";
                psi.Arguments = $"\"{profileDir}\"";
            }
            psi.UseShellExecute = false;
            Process.Start(psi);
            
            Logger.Success("Profile", $"Opened profile folder: {profileDir}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Profile", $"Failed to open profile folder: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public void InitializeProfileModsSymlink()
    {
        try
        {
            var profile = GetSelectedProfile();
            if (profile == null)
            {
                Logger.Info("Mods", "No active profile, ensuring instance mods directory without profile linking");
                EnsureInstanceModsDirectory(null);
                return;
            }

            // Backward compatibility: if old builds created profile symlink/junction,
            // migrate it back into the real instance UserData/Mods directory.
            EnsureInstanceModsDirectory(profile);
        }
        catch (Exception ex)
        {
            Logger.Warning("Mods", $"Failed to initialize instance mods directory: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public string GetProfilesFolder()
    {
        return UtilityService.GetProfilesRoot(_appDir);
    }

    #region Private Helper Methods
    
    /// <summary>
    /// Gets the path to a profile's mods folder.
    /// </summary>
    private string GetProfileModsFolder(Profile profile)
    {
        var profileDir = UtilityService.GetProfileFolderPath(_appDir, profile);
        var modsDir = Path.Combine(profileDir, "Mods");
        Directory.CreateDirectory(modsDir);
        return modsDir;
    }
    
    /// <summary>
    /// Ensures the active instance has a real UserData/Mods directory.
    /// If a legacy profile symlink/junction is detected, migrates files back.
    /// </summary>
    private void EnsureInstanceModsDirectory(Profile? profile)
    {
        try
        {
            var versionPath = TryGetCurrentExistingInstancePath();
            if (string.IsNullOrWhiteSpace(versionPath))
            {
                Logger.Info("Mods", "No existing instance found for mods directory initialization");
                return;
            }

            var userDataPath = Path.Combine(versionPath, "UserData");
            var gameModsPath = Path.Combine(userDataPath, "Mods");

            Directory.CreateDirectory(userDataPath);

            // Handle the case where Hytale created a file named "Mods" instead of a directory
            if (File.Exists(gameModsPath))
            {
                Logger.Warning("Mods",
                    $"Found a file where the Mods directory should be ({gameModsPath}), removing it");
                File.Delete(gameModsPath);
            }

            if (!Directory.Exists(gameModsPath))
            {
                Directory.CreateDirectory(gameModsPath);
                return;
            }

            // Detect legacy symlink/junction created by older builds
            var dirInfo = new DirectoryInfo(gameModsPath);
            bool isSymlink = dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint);
            if (!isSymlink)
            {
                return;
            }

            string? targetPath = null;
            try
            {
                targetPath = dirInfo.ResolveLinkTarget(true)?.FullName;
            }
            catch
            {
                // ResolveLinkTarget may fail for some junctions; continue best-effort
            }

            var migrationSources = new List<string>();
            if (!string.IsNullOrWhiteSpace(targetPath) && Directory.Exists(targetPath))
            {
                migrationSources.Add(targetPath);
            }

            if (profile != null)
            {
                var profileModsPath = GetProfileModsFolder(profile);
                if (Directory.Exists(profileModsPath) &&
                    !migrationSources.Any(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(profileModsPath), StringComparison.OrdinalIgnoreCase)))
                {
                    migrationSources.Add(profileModsPath);
                }
            }

            Logger.Info("Mods", "Legacy profile mods link detected, migrating back to instance UserData/Mods");

            try
            {
                Directory.Delete(gameModsPath, false);
            }
            catch
            {
                Directory.Delete(gameModsPath, true);
            }

            Directory.CreateDirectory(gameModsPath);

            foreach (var source in migrationSources)
            if (Directory.Exists(source))
            {
                foreach (var file in Directory.GetFiles(source))
                {
                    var destFile = Path.Combine(gameModsPath, Path.GetFileName(file));
                    File.Copy(file, destFile, true);
                }
            }

            Logger.Success("Mods", $"Using instance-local mods directory: {gameModsPath}");
        }
        catch (Exception ex)
        {
            Logger.Warning("Mods", $"Failed to ensure instance mods directory: {ex.Message}");
        }
    }

    private string? TryGetCurrentExistingInstancePath()
    {
        var selected = _instanceService.GetSelectedInstance();
        if (selected != null)
        {
            var path = _instanceService.GetInstancePathById(selected.Id);
            if (!string.IsNullOrWhiteSpace(path))
                return path;
        }

        // Fall back to any installed instance when nothing is explicitly selected.
        return _instanceService.GetInstalledInstances().FirstOrDefault()?.Path;
    }
    
    /// <summary>
    /// Saves a profile to disk as a .sh file with name and UUID, plus avatar if available.
    /// </summary>
    private void SaveProfileToDisk(Profile profile)
    {
        try
        {
            var profileDir = UtilityService.GetProfileFolderPath(_appDir, profile);
            Directory.CreateDirectory(profileDir);
            
            // Create the Mods folder for this profile
            var modsDir = Path.Combine(profileDir, "Mods");
            Directory.CreateDirectory(modsDir);
            
            // Create the shell script with profile info
            var shPath = Path.Combine(profileDir, $"{profile.Name}.sh");
            var shContent = $@"#!/bin/bash
# HyPrism Profile - {profile.Name}
# Created: {profile.CreatedAt:yyyy-MM-dd HH:mm:ss}

export HYPRISM_PROFILE_NAME=""{profile.Name}""
export HYPRISM_PROFILE_UUID=""{profile.UUID}""
export HYPRISM_PROFILE_ID=""{profile.Id}""

# This file is auto-generated by HyPrism launcher
# You can source this file to use this profile's settings
";
            File.WriteAllText(shPath, shContent);
            
            // Copy skin and avatar from game cache to profile folder
            _skinService.CopyProfileSkinData(profile.UUID, profileDir);
            
            Logger.Info("Profile", $"Saved profile to disk: {profileDir}");
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to save profile to disk: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Updates a profile's disk files when it's modified.
    /// </summary>
    private void UpdateProfileOnDisk(Profile profile)
    {
        try
        {
            var profileDir = UtilityService.GetProfileFolderPath(_appDir, profile);
            
            if (!Directory.Exists(profileDir))
            {
                SaveProfileToDisk(profile);
                return;
            }
            
            // Remove old .sh files
            foreach (var oldSh in Directory.GetFiles(profileDir, "*.sh"))
            {
                File.Delete(oldSh);
            }
            
            // Create new .sh file
            var shPath = Path.Combine(profileDir, $"{profile.Name}.sh");
            var shContent = $@"#!/bin/bash
# HyPrism Profile - {profile.Name}
# Created: {profile.CreatedAt:yyyy-MM-dd HH:mm:ss}
# Updated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}

export HYPRISM_PROFILE_NAME=""{profile.Name}""
export HYPRISM_PROFILE_UUID=""{profile.UUID}""
export HYPRISM_PROFILE_ID=""{profile.Id}""

# This file is auto-generated by HyPrism launcher
# You can source this file to use this profile's settings
";
            File.WriteAllText(shPath, shContent);
            
            Logger.Info("Profile", $"Updated profile on disk: {profileDir}");
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to update profile on disk: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Deletes a profile's disk folder.
    /// </summary>
    private void DeleteProfileFromDisk(string profileId, string? profileName = null)
    {
        try
        {
            var profilesDir = GetProfilesFolder();
            
            // Try to delete by name first if provided
            if (!string.IsNullOrEmpty(profileName))
            {
                var safeName = UtilityService.SanitizeFileName(profileName);
                var profileDirByName = Path.Combine(profilesDir, safeName);
                if (Directory.Exists(profileDirByName))
                {
                    Directory.Delete(profileDirByName, true);
                    Logger.Info("Profile", $"Deleted profile from disk: {profileDirByName}");
                }
            }
            
            // Fallback to ID-based folder (for migration)
            var profileDir = Path.Combine(profilesDir, profileId);
            if (Directory.Exists(profileDir))
            {
                Directory.Delete(profileDir, true);
                Logger.Info("Profile", $"Deleted profile from disk: {profileDir}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to delete profile from disk: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Recursively copies a directory and all its contents.
    /// </summary>
    private void CopyDirectory(string sourceDir, string destDir)
    {
        UtilityService.CopyDirectory(sourceDir, destDir);
    }
    #endregion
}
