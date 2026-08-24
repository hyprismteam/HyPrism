// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Models;
using HyPrism.Core.Infrastructure;
using HyPrism.Core.Game;
using HyPrism.Core.Game.Instances;

namespace HyPrism.Core.Accounts;

/// <summary>
/// Manages player skin data including protection from game overwrites,
/// backup/restore operations, and orphaned skin recovery
/// </summary>
/// <remarks>
/// Implements file watching to protect custom skins from being
/// overwritten during gameplay. Backs up skin data to profile directories
/// </remarks>
public class SkinRepository : ISkinRepository
{
    private FileSystemWatcher? _skinWatcher;
    private string? _protectedSkinPath;
    private string? _protectedSkinContent;
    private bool _skinProtectionEnabled;
    private readonly Lock _skinProtectionLock = new();

    private readonly IConfigStore _configStore;
    private readonly IInstanceRepository _instances;
    private readonly IProfileManager _profiles;
    private readonly string _appDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="SkinRepository"/> class
    /// </summary>
    /// <param name="appPath">The application path configuration</param>
    /// <param name="configStore">The configuration service</param>
    /// <param name="instances">The game instance service</param>
    /// <param name="profiles">The active profile service</param>
    public SkinRepository(
        AppPathConfiguration appPath,
        IConfigStore configStore,
        IInstanceRepository instances,
        IProfileManager profiles)
    {
        _appDir = appPath.AppDir;
        _configStore = configStore;
        _instances = instances;
        _profiles = profiles;
    }

    #region Skin Protection

    /// <inheritdoc/>
    public void StartSkinProtection(Profile profile, string skinCachePath)
    {
        try
        {
            StopSkinProtection();

            if (!File.Exists(skinCachePath))
            {
                Logger.Warning("SkinProtection", $"Skin file doesn't exist, cannot protect: {skinCachePath}");
                return;
            }

            lock (_skinProtectionLock)
            {
                _protectedSkinPath = skinCachePath;
                _protectedSkinContent = File.ReadAllText(skinCachePath);
                _skinProtectionEnabled = true;
            }

            // Read-only protection stops the game before it can overwrite the cached skin
            try
            {
                var fileInfo = new FileInfo(skinCachePath)
                {
                    IsReadOnly = true
                };
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
    /// Handles skin file changes - restores the protected content if it was overwritten
    /// </summary>
    /// <param name="sender">The event sender</param>
    /// <param name="e">The file system event arguments</param>
    private void OnSkinFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_skinProtectionLock)
        {
            if (!_skinProtectionEnabled || string.IsNullOrEmpty(_protectedSkinPath) || string.IsNullOrEmpty(_protectedSkinContent))
                return;

            try
            {
                // FileSystemWatcher can fire before the writer releases the file
                Thread.Sleep(100);

                var currentContent = File.ReadAllText(_protectedSkinPath);

                if (currentContent != _protectedSkinContent)
                {
                    Logger.Warning("SkinProtection", "Detected skin overwrite - restoring protected skin!");

                    _skinProtectionEnabled = false;

                    File.WriteAllText(_protectedSkinPath, _protectedSkinContent);

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

            if (!string.IsNullOrEmpty(pathToUnprotect) && File.Exists(pathToUnprotect))
            {
                try
                {
                    var fileInfo = new FileInfo(pathToUnprotect)
                    {
                        IsReadOnly = false
                    };
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
            var selectedProfileId = _configStore.Configuration.SelectedProfileId;
            var selectedProfile = _profiles.GetProfiles()
                .FirstOrDefault(profile => profile.Id == selectedProfileId);
            if (selectedProfile is null)
            {
                return;
            }
            var currentUuid = selectedProfile.UUID;

            var versionPath = TryGetCurrentExistingInstancePath();
            if (string.IsNullOrWhiteSpace(versionPath))
            {
                return;
            }
            var userDataPath = _instances.GetInstanceUserDataPath(versionPath);
            var skinCacheDir = Path.Combine(userDataPath, "CachedPlayerSkins");
            var avatarCacheDir = Path.Combine(userDataPath, "CachedAvatarPreviews");

            var currentSkinPath = Path.Combine(skinCacheDir, $"{currentUuid}.json");
            if (File.Exists(currentSkinPath))
            {
                return;
            }

            if (!Directory.Exists(skinCacheDir))
            {
                return;
            }

            var knownUuids = new HashSet<string>(
                _profiles.GetProfiles().Select(p => p.UUID)
                    .Where(u => !string.IsNullOrEmpty(u)),
                StringComparer.OrdinalIgnoreCase
            );

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
                return;
            }

            Logger.Info("Startup", $"Found orphaned skin with UUID {orphanedUuid}");
            Logger.Info("Startup", $"Selected profile '{selectedProfile.Name}' has no skin, recovering orphaned skin");

            if (!_profiles.SetUUID(orphanedUuid))
                return;

            Logger.Success("Startup", $"Recovered orphaned skin for '{selectedProfile.Name}' with UUID {orphanedUuid}");
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
            var versionPath = TryGetCurrentExistingInstancePath();
            if (string.IsNullOrWhiteSpace(versionPath))
            {
                return null;
            }
            var userDataPath = _instances.GetInstanceUserDataPath(versionPath);
            var skinCacheDir = Path.Combine(userDataPath, "CachedPlayerSkins");

            if (!Directory.Exists(skinCacheDir))
            {
                return null;
            }

            var knownUuids = new HashSet<string>(
                _profiles.GetProfiles().Select(p => p.UUID)
                    .Where(u => !string.IsNullOrEmpty(u)),
                StringComparer.OrdinalIgnoreCase
            );

            var skinFiles = Directory.GetFiles(skinCacheDir, "*.json");
            var orphanedUuids = new List<string>();

            foreach (var file in skinFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (Guid.TryParse(fileName, out var uuid))
                {
                    var uuidStr = uuid.ToString();
                    if (!knownUuids.Contains(uuidStr))
                    {
                        orphanedUuids.Add(uuidStr);
                        Logger.Info("UUID", $"Found orphaned skin file: {fileName}.json");
                    }
                }
            }

            if (orphanedUuids.Count == 1)
            {
                return orphanedUuids[0];
            }
            else if (orphanedUuids.Count > 1)
            {
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
            var orphanedUuid = FindOrphanedSkinUuid();

            if (string.IsNullOrEmpty(orphanedUuid))
            {
                Logger.Info("UUID", "No orphaned skin data found to recover");
                return false;
            }

            var versionPath = TryGetCurrentExistingInstancePath();
            if (string.IsNullOrWhiteSpace(versionPath))
            {
                return false;
            }
            var userDataPath = _instances.GetInstanceUserDataPath(versionPath);
            var skinCacheDir = Path.Combine(userDataPath, "CachedPlayerSkins");
            var avatarCacheDir = Path.Combine(userDataPath, "CachedAvatarPreviews");

            var currentSkinPath = Path.Combine(skinCacheDir, $"{currentUuid}.json");

            if (File.Exists(currentSkinPath))
            {
                Logger.Info("UUID", $"Current user already has skin data. Use SetUuidForUser to switch to the orphaned UUID: {orphanedUuid}");
                return false;
            }

            var orphanSkinPath = Path.Combine(skinCacheDir, $"{orphanedUuid}.json");
            if (File.Exists(orphanSkinPath))
            {
                Directory.CreateDirectory(skinCacheDir);
                File.Copy(orphanSkinPath, currentSkinPath, true);
                Logger.Success("UUID", $"Copied orphaned skin from {orphanedUuid} to {currentUuid}");
            }

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
            var config = _configStore.Configuration;
            var profile = _profiles.GetProfiles().FirstOrDefault(p => p.UUID == uuid);
            if (profile == null)
            {
                return;
            }

            var profileDir = LauncherUtilities.GetProfileFolderPath(_appDir, profile);
            Directory.CreateDirectory(profileDir);

            var versionPath = TryGetCurrentExistingInstancePath();
            if (string.IsNullOrWhiteSpace(versionPath))
            {
                return;
            }
            var userDataPath = _instances.GetInstanceUserDataPath(versionPath);

            var skinCacheDir = Path.Combine(userDataPath, "CachedPlayerSkins");
            var skinPath = Path.Combine(skinCacheDir, $"{uuid}.json");
            if (File.Exists(skinPath))
            {
                var destPath = LauncherJsonFile.GetPath(profileDir, "Skin.json", "skin.json");
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
            var config = _configStore.Configuration;
            var profileDir = LauncherUtilities.GetProfileFolderPath(_appDir, profile);

            if (!Directory.Exists(profileDir))
            {
                Logger.Info("Profile", $"No profile folder to restore from for {profile.Name}");
                return;
            }

            var versionPath = TryGetCurrentExistingInstancePath();
            if (string.IsNullOrWhiteSpace(versionPath))
            {
                return;
            }
            var userDataPath = _instances.GetInstanceUserDataPath(versionPath);

            var skinBackupPath = LauncherJsonFile.GetPath(profileDir, "Skin.json", "skin.json");
            if (File.Exists(skinBackupPath))
            {
                var skinCacheDir = Path.Combine(userDataPath, "CachedPlayerSkins");
                Directory.CreateDirectory(skinCacheDir);
                var skinPath = Path.Combine(skinCacheDir, $"{profile.UUID}.json");
                File.Copy(skinBackupPath, skinPath, true);
                Logger.Info("Profile", $"Restored skin for {profile.Name}");
            }

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
            var config = _configStore.Configuration;
            var versionPath = TryGetCurrentExistingInstancePath();
            if (string.IsNullOrWhiteSpace(versionPath))
            {
                return;
            }
            var userDataPath = _instances.GetInstanceUserDataPath(versionPath);

            var skinCacheDir = Path.Combine(userDataPath, "CachedPlayerSkins");
            var skinPath = Path.Combine(skinCacheDir, $"{uuid}.json");
            if (File.Exists(skinPath))
            {
                var destPath = LauncherJsonFile.GetPath(profileDir, "Skin.json", "skin.json");
                File.Copy(skinPath, destPath, true);
                Logger.Info("Profile", $"Copied skin for UUID {uuid}");
            }

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
    /// Releases resources used by the skin service, stopping any active skin protection
    /// </summary>
    public void Dispose()
    {
        StopSkinProtection();
        GC.SuppressFinalize(this);
    }

    private string? TryGetCurrentExistingInstancePath()
    {
        var selected = _instances.GetSelectedInstance();
        if (selected != null)
        {
            var path = _instances.GetInstancePathById(selected.Id);
            if (!string.IsNullOrWhiteSpace(path))
                return path;
        }

        // Skin caches are instance-scoped even when no instance is selected
        return _instances.GetInstalledInstances().FirstOrDefault()?.Path;
    }
}
