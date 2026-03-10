using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Game;
using HyPrism.Services.Game.Instance;

namespace HyPrism.Services.User;

/// <summary>
/// Manages player skin data including protection from game overwrites,
/// backup/restore operations, and orphaned skin recovery.
/// </summary>
/// <remarks>
/// Implements file watching to protect custom skins from being 
/// overwritten during gameplay. Backs up skin data to profile directories.
/// </remarks>
public class SkinService : ISkinService
{
    // Skin protection: Watch for skin file overwrites during gameplay
    private FileSystemWatcher? _skinWatcher;
    private string? _protectedSkinPath;
    private string? _protectedSkinContent;
    private bool _skinProtectionEnabled;
    private readonly object _skinProtectionLock = new object();

    private readonly IConfigService _configService;
    private readonly IInstanceService _instanceService;
    private readonly IProfileService _profileService;
    private readonly string _appDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="SkinService"/> class.
    /// </summary>
    /// <param name="appPath">The application path configuration.</param>
    /// <param name="configService">The configuration service.</param>
    /// <param name="instanceService">The game instance service.</param>
    public SkinService(
        AppPathConfiguration appPath,
        IConfigService configService,
        IInstanceService instanceService,
        IProfileService profileService)
    {
        _appDir = appPath.AppDir;
        _configService = configService;
        _instanceService = instanceService;
        _profileService = profileService;
    }

    #region Skin Protection

    /// <inheritdoc/>
    public void StartSkinProtection(Profile profile, string skinCachePath)
    {
        try
        {
            StopSkinProtection(); // Clean up any existing watcher
            
            if (!File.Exists(skinCachePath))
            {
                Logger.Warning("SkinProtection", $"Skin file doesn't exist, cannot protect: {skinCachePath}");
                return;
            }
            
            // Store the original skin content
            lock (_skinProtectionLock)
            {
                _protectedSkinPath = skinCachePath;
                _protectedSkinContent = File.ReadAllText(skinCachePath);
                _skinProtectionEnabled = true;
            }
            
            // Set file to READ-ONLY to prevent game from overwriting it
            // This is more reliable than FileSystemWatcher because the game will fail to write
            try
            {
                var fileInfo = new FileInfo(skinCachePath);
                fileInfo.IsReadOnly = true;
                Logger.Success("SkinProtection", $"Set skin file to READ-ONLY to prevent overwrites");
            }
            catch (Exception ex)
            {
                Logger.Warning("SkinProtection", $"Failed to set read-only: {ex.Message}");
            }
            
            var directory = Path.GetDirectoryName(skinCachePath);
            var filename = Path.GetFileName(skinCachePath);
            
            if (string.IsNullOrEmpty(directory))
            {
                Logger.Warning("SkinProtection", "Invalid skin path");
                return;
            }
            
            _skinWatcher = new FileSystemWatcher(directory, filename)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            
            _skinWatcher.Changed += OnSkinFileChanged;
            _skinWatcher.Created += OnSkinFileChanged;
            
            Logger.Success("SkinProtection", $"Started protecting skin file for {profile.Name}");
        }
        catch (Exception ex)
        {
            Logger.Warning("SkinProtection", $"Failed to start skin protection: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles skin file changes - restores the protected content if it was overwritten.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The file system event arguments.</param>
    private void OnSkinFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_skinProtectionLock)
        {
            if (!_skinProtectionEnabled || string.IsNullOrEmpty(_protectedSkinPath) || string.IsNullOrEmpty(_protectedSkinContent))
                return;
            
            try
            {
                // Small delay to let the file write complete
                Thread.Sleep(100);
                
                // Read current content
                var currentContent = File.ReadAllText(_protectedSkinPath);
                
                // Compare - if different, the game overwrote our skin
                if (currentContent != _protectedSkinContent)
                {
                    Logger.Warning("SkinProtection", "Detected skin overwrite - restoring protected skin!");
                    
                    // Temporarily disable watcher to avoid triggering ourselves
                    _skinProtectionEnabled = false;
                    
                    // Restore the protected content
                    File.WriteAllText(_protectedSkinPath, _protectedSkinContent);
                    
                    // Re-enable protection
                    _skinProtectionEnabled = true;
                    
                    Logger.Success("SkinProtection", "Skin restored successfully");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("SkinProtection", $"Failed to check/restore skin: {ex.Message}");
            }
        }
    }

    /// <inheritdoc/>
    public void StopSkinProtection()
    {
        try
        {
            string? pathToUnprotect = null;
            lock (_skinProtectionLock)
            {
                pathToUnprotect = _protectedSkinPath;
                _skinProtectionEnabled = false;
                _protectedSkinPath = null;
                _protectedSkinContent = null;
            }
            
            // Remove READ-ONLY flag so file can be modified again
            if (!string.IsNullOrEmpty(pathToUnprotect) && File.Exists(pathToUnprotect))
            {
                try
                {
                    var fileInfo = new FileInfo(pathToUnprotect);
                    fileInfo.IsReadOnly = false;
                    Logger.Info("SkinProtection", "Removed READ-ONLY flag from skin file");
                }
                catch (Exception ex)
                {
                    Logger.Warning("SkinProtection", $"Failed to remove read-only: {ex.Message}");
                }
            }
            
            if (_skinWatcher != null)
            {
                _skinWatcher.EnableRaisingEvents = false;
                _skinWatcher.Changed -= OnSkinFileChanged;
                _skinWatcher.Created -= OnSkinFileChanged;
                _skinWatcher.Dispose();
                _skinWatcher = null;
                Logger.Info("SkinProtection", "Stopped skin protection");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("SkinProtection", $"Failed to stop skin protection: {ex.Message}");
        }
    }

    #endregion

    #region Orphaned Skin Recovery

    /// <inheritdoc/>
    public void TryRecoverOrphanedSkinOnStartup()
    {
        try
        {
            var config = _configService.Configuration;
            var currentUuid = config.UUID;
            if (string.IsNullOrEmpty(currentUuid) || string.IsNullOrEmpty(config.Nick))
            {
                return;
            }
            
            // Get the current instance's UserData path
            var versionPath = TryGetCurrentExistingInstancePath();
            if (string.IsNullOrWhiteSpace(versionPath))
            {
                return;
            }
            var userDataPath = _instanceService.GetInstanceUserDataPath(versionPath);
            var skinCacheDir = Path.Combine(userDataPath, "CachedPlayerSkins");
            var avatarCacheDir = Path.Combine(userDataPath, "CachedAvatarPreviews");
            
            // Check if current UUID already has skin data
            var currentSkinPath = Path.Combine(skinCacheDir, $"{currentUuid}.json");
            if (File.Exists(currentSkinPath))
            {
                // Current UUID has skin - no recovery needed
                return;
            }
            
            // No skin for current UUID - look for orphaned skins
            if (!Directory.Exists(skinCacheDir))
            {
                return;
            }
            
            // Get all existing UUIDs from Profiles
            var knownUuids = new HashSet<string>(
                _profileService.GetProfiles().Select(p => p.UUID)
                    .Concat(new[] { config.UUID ?? "" })
                    .Where(u => !string.IsNullOrEmpty(u)),
                StringComparer.OrdinalIgnoreCase
            );
            
            // Scan for orphaned skin files
            var skinFiles = Directory.GetFiles(skinCacheDir, "*.json");
            string? orphanedUuid = null;
            DateTime latestTime = DateTime.MinValue;
            
            foreach (var file in skinFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (Guid.TryParse(fileName, out var uuid))
                {
                    var uuidStr = uuid.ToString();
                    if (!knownUuids.Contains(uuidStr))
                    {
                        // This is an orphaned skin
                        var modTime = File.GetLastWriteTime(file);
                        if (modTime > latestTime)
                        {
                            latestTime = modTime;
                            orphanedUuid = uuidStr;
                        }
                    }
                }
            }
            
            if (orphanedUuid == null)
            {
                return; // No orphans found
            }
            
            Logger.Info("Startup", $"Found orphaned skin with UUID {orphanedUuid}");
            Logger.Info("Startup", $"Current user '{config.Nick}' has no skin - recovering orphaned skin");
            
            // Strategy: Update the current user's UUID to match the orphaned skin
            config.UUID = orphanedUuid;
            _configService.SaveConfig();
            
            Logger.Success("Startup", $"Recovered orphaned skin! User '{config.Nick}' now uses UUID {orphanedUuid}");
        }
        catch (Exception ex)
        {
            Logger.Warning("Startup", $"Failed to recover orphaned skins: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public string? FindOrphanedSkinUuid()
    {
        try
        {
            var config = _configService.Configuration;
            // Get the current instance's UserData path
            var versionPath = TryGetCurrentExistingInstancePath();
            if (string.IsNullOrWhiteSpace(versionPath))
            {
                return null;
            }
            var userDataPath = _instanceService.GetInstanceUserDataPath(versionPath);
            var skinCacheDir = Path.Combine(userDataPath, "CachedPlayerSkins");
            
            if (!Directory.Exists(skinCacheDir))
            {
                return null;
            }
            
            // Get all existing UUIDs from Profiles
            var knownUuids = new HashSet<string>(
                _profileService.GetProfiles().Select(p => p.UUID)
                    .Concat(new[] { config.UUID ?? "" })
                    .Where(u => !string.IsNullOrEmpty(u)),
                StringComparer.OrdinalIgnoreCase
            );
            
            // Scan skin files for orphaned UUIDs
            var skinFiles = Directory.GetFiles(skinCacheDir, "*.json");
            var orphanedUuids = new List<string>();
            
            foreach (var file in skinFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                // Check if it looks like a UUID
                if (Guid.TryParse(fileName, out var uuid))
                {
                    var uuidStr = uuid.ToString();
                    // If this UUID is not in our known UUIDs, it's orphaned
                    if (!knownUuids.Contains(uuidStr))
                    {
                        orphanedUuids.Add(uuidStr);
                        Logger.Info("UUID", $"Found orphaned skin file: {fileName}.json");
                    }
                }
            }
            
            // If exactly one orphaned UUID found, we can safely adopt it
            // If multiple are found, we can't determine which is correct
            if (orphanedUuids.Count == 1)
            {
                return orphanedUuids[0];
            }
            else if (orphanedUuids.Count > 1)
            {
                // Multiple orphans - pick the most recently modified one
                string? mostRecent = null;
                DateTime latestTime = DateTime.MinValue;
                
                foreach (var orphanUuid in orphanedUuids)
                {
                    var skinPath = Path.Combine(skinCacheDir, $"{orphanUuid}.json");
                    if (File.Exists(skinPath))
                    {
                        var modTime = File.GetLastWriteTime(skinPath);
                        if (modTime > latestTime)
                        {
                            latestTime = modTime;
                            mostRecent = orphanUuid;
                        }
                    }
                }
                
                if (mostRecent != null)
                {
                    Logger.Info("UUID", $"Multiple orphaned skins found, using most recent: {mostRecent}");
                    return mostRecent;
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warning("UUID", $"Error scanning for orphaned skins: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public bool RecoverOrphanedSkinData(string currentUuid)
    {
        try
        {
            var config = _configService.Configuration;
            var orphanedUuid = FindOrphanedSkinUuid();
            
            if (string.IsNullOrEmpty(orphanedUuid))
            {
                Logger.Info("UUID", "No orphaned skin data found to recover");
                return false;
            }
            
            // If the current UUID already has a skin, don't overwrite
            var versionPath = TryGetCurrentExistingInstancePath();
            if (string.IsNullOrWhiteSpace(versionPath))
            {
                return false;
            }
            var userDataPath = _instanceService.GetInstanceUserDataPath(versionPath);
            var skinCacheDir = Path.Combine(userDataPath, "CachedPlayerSkins");
            var avatarCacheDir = Path.Combine(userDataPath, "CachedAvatarPreviews");
            
            var currentSkinPath = Path.Combine(skinCacheDir, $"{currentUuid}.json");
            
            // If current user already has a skin, ask them to use "switch to orphan" instead
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
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("UUID", $"Failed to recover orphaned skin data: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Profile Skin Management

    /// <inheritdoc/>
    public void BackupProfileSkinData(string uuid)
    {
        try
        {
            var config = _configService.Configuration;
            // Find the profile by UUID
            var profile = _profileService.GetProfiles().FirstOrDefault(p => p.UUID == uuid);
            if (profile == null)
            {
                return;
            }
            
            var profileDir = UtilityService.GetProfileFolderPath(_appDir, profile);
            Directory.CreateDirectory(profileDir);
            
            // Get game UserData path
            var versionPath = TryGetCurrentExistingInstancePath();
            if (string.IsNullOrWhiteSpace(versionPath))
            {
                return;
            }
            var userDataPath = _instanceService.GetInstanceUserDataPath(versionPath);
            
            // Backup skin JSON
            var skinCacheDir = Path.Combine(userDataPath, "CachedPlayerSkins");
            var skinPath = Path.Combine(skinCacheDir, $"{uuid}.json");
            if (File.Exists(skinPath))
            {
                var destPath = Path.Combine(profileDir, "skin.json");
                // Remove read-only attribute from destination if it exists
                if (File.Exists(destPath))
                {
                    var destInfo = new FileInfo(destPath);
                    if (destInfo.IsReadOnly)
                    {
                        destInfo.IsReadOnly = false;
                    }
                }
                var skinJson = File.ReadAllText(skinPath);
                File.Copy(skinPath, destPath, true);
                Logger.Info("Profile", $"Backed up skin for {profile.Name} ({skinJson.Length} bytes)");
            }
            else
            {
                Logger.Warning("Profile", $"No skin file found to backup for {profile.Name} at {skinPath}");
            }
            
            // Backup avatar preview
            var avatarCacheDir = Path.Combine(userDataPath, "CachedAvatarPreviews");
            var avatarPath = Path.Combine(avatarCacheDir, $"{uuid}.png");
            if (File.Exists(avatarPath))
            {
                var destPath = Path.Combine(profileDir, "avatar.png");
                File.Copy(avatarPath, destPath, true);
                Logger.Info("Profile", $"Backed up avatar for {profile.Name}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to backup skin data: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void RestoreProfileSkinData(Profile profile)
    {
        try
        {
            var config = _configService.Configuration;
            var profileDir = UtilityService.GetProfileFolderPath(_appDir, profile);
            
            if (!Directory.Exists(profileDir))
            {
                Logger.Info("Profile", $"No profile folder to restore from for {profile.Name}");
                return;
            }
            
            // Get game UserData path
            var versionPath = TryGetCurrentExistingInstancePath();
            if (string.IsNullOrWhiteSpace(versionPath))
            {
                return;
            }
            var userDataPath = _instanceService.GetInstanceUserDataPath(versionPath);
            
            // Restore skin JSON
            var skinBackupPath = Path.Combine(profileDir, "skin.json");
            if (File.Exists(skinBackupPath))
            {
                var skinCacheDir = Path.Combine(userDataPath, "CachedPlayerSkins");
                Directory.CreateDirectory(skinCacheDir);
                var skinPath = Path.Combine(skinCacheDir, $"{profile.UUID}.json");
                File.Copy(skinBackupPath, skinPath, true);
                Logger.Info("Profile", $"Restored skin for {profile.Name}");
            }
            
            // Restore avatar preview
            var avatarBackupPath = Path.Combine(profileDir, "avatar.png");
            if (File.Exists(avatarBackupPath))
            {
                var avatarCacheDir = Path.Combine(userDataPath, "CachedAvatarPreviews");
                Directory.CreateDirectory(avatarCacheDir);
                var avatarPath = Path.Combine(avatarCacheDir, $"{profile.UUID}.png");
                File.Copy(avatarBackupPath, avatarPath, true);
                Logger.Info("Profile", $"Restored avatar for {profile.Name}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to restore skin data: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void CopyProfileSkinData(string uuid, string profileDir)
    {
        try
        {
            var config = _configService.Configuration;
            // Get game UserData path
            var versionPath = TryGetCurrentExistingInstancePath();
            if (string.IsNullOrWhiteSpace(versionPath))
            {
                return;
            }
            var userDataPath = _instanceService.GetInstanceUserDataPath(versionPath);
            
            // Copy skin JSON
            var skinCacheDir = Path.Combine(userDataPath, "CachedPlayerSkins");
            var skinPath = Path.Combine(skinCacheDir, $"{uuid}.json");
            if (File.Exists(skinPath))
            {
                var destPath = Path.Combine(profileDir, "skin.json");
                File.Copy(skinPath, destPath, true);
                Logger.Info("Profile", $"Copied skin for UUID {uuid}");
            }
            
            // Copy avatar PNG
            var avatarCacheDir = Path.Combine(userDataPath, "CachedAvatarPreviews");
            var avatarPath = Path.Combine(avatarCacheDir, $"{uuid}.png");
            if (File.Exists(avatarPath))
            {
                var destPath = Path.Combine(profileDir, "avatar.png");
                File.Copy(avatarPath, destPath, true);
                Logger.Info("Profile", $"Copied avatar for UUID {uuid}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Profile", $"Failed to copy skin data: {ex.Message}");
        }
    }

    #endregion

    /// <summary>
    /// Releases resources used by the skin service, stopping any active skin protection.
    /// </summary>
    public void Dispose()
    {
        StopSkinProtection();
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
}
