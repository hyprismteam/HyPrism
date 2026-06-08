using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Game.Instance;

namespace HyPrism.Services.Game.Asset;

/// <summary>
/// Manages user avatar cache and preview images for game instances.
/// Handles persistent avatar backup and cache cleanup across all instances.
/// </summary>
public class AvatarService : IAvatarService
{
    private readonly IInstanceService _instanceService;
    private readonly string _appDir;

    /// <inheritdoc/>
    public event Action<string>? AvatarUpdated;

    /// <summary>
    /// Initializes a new instance of the <see cref="AvatarService"/> class.
    /// </summary>
    /// <param name="instanceService">The instance service for accessing game instance paths.</param>
    /// <param name="appDir">The application data directory path.</param>
    public AvatarService(IInstanceService instanceService, string appDir)
    {
        _instanceService = instanceService;
        _appDir = appDir;
    }

    /// <inheritdoc/>
    public string GetAvatarBackupPath(string uuid)
    {
        return Path.Combine(_appDir, "AvatarBackups", $"{uuid}.png");
    }

    /// <inheritdoc/>
    public bool BackupAvatar(string uuid)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(uuid)) return false;

            // Search all instances for the latest cached avatar
            var instanceRoot = _instanceService.GetInstanceRoot();
            if (!Directory.Exists(instanceRoot)) return false;

            string? latestAvatarPath = null;
            DateTime latestWriteTime = DateTime.MinValue;

            foreach (var branchDir in Directory.GetDirectories(instanceRoot))
            {
                foreach (var versionDir in Directory.GetDirectories(branchDir))
                {
                    var avatarPath = Path.Combine(versionDir, "UserData", "CachedAvatarPreviews", $"{uuid}.png");
                    if (File.Exists(avatarPath))
                    {
                        var writeTime = File.GetLastWriteTimeUtc(avatarPath);
                        if (writeTime > latestWriteTime)
                        {
                            latestWriteTime = writeTime;
                            latestAvatarPath = avatarPath;
                        }
                    }
                }
            }

            if (latestAvatarPath == null) return false;

            // Copy to persistent backup
            var backupDir = Path.Combine(_appDir, "AvatarBackups");
            Directory.CreateDirectory(backupDir);
            var backupPath = GetAvatarBackupPath(uuid);
            File.Copy(latestAvatarPath, backupPath, overwrite: true);
            Logger.Info("Avatar", $"Backed up avatar for {uuid} from {latestAvatarPath}");

            AvatarUpdated?.Invoke(backupPath);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Avatar", $"Failed to backup avatar: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public bool ClearAvatarCache(string uuid)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(uuid)) return false;

            // Clear persistent backup
            var persistentPath = Path.Combine(_appDir, "AvatarBackups", $"{uuid}.png");
            if (File.Exists(persistentPath))
            {
                File.Delete(persistentPath);
                Logger.Info("Avatar", $"Deleted persistent avatar for {uuid}");
            }

            // Clear game cache for all instances
            var instanceRoot = _instanceService.GetInstanceRoot();
            if (Directory.Exists(instanceRoot))
            {
                foreach (var branchDir in Directory.GetDirectories(instanceRoot))
                {
                    foreach (var versionDir in Directory.GetDirectories(branchDir))
                    {
                        var avatarPath = Path.Combine(versionDir, "UserData", "CachedAvatarPreviews", $"{uuid}.png");
                        if (File.Exists(avatarPath))
                        {
                            File.Delete(avatarPath);
                            Logger.Info("Avatar", $"Deleted cached avatar at {avatarPath}");
                        }
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Avatar", $"Failed to clear avatar cache: {ex.Message}");
            return false;
        }
    }
}
