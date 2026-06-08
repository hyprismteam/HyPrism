using HyPrism.Services.Core.Infrastructure;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using HyPrism.Models;

namespace HyPrism.Services.Game.Instance;

/// <summary>
/// Manages game instance paths, versioning, and data organization.
/// Handles instance discovery, creation, and migration from legacy launcher versions.
/// </summary>
/// <remarks>
/// Instances are organized in a flat layout: {InstanceRoot}/{instanceId}/.
/// Branch and version information is stored in each instance's meta.json.
/// Legacy layouts (branch subdirectories, version-named folders) are migrated on startup.
/// This service also handles user data directories and cosmetic skins.
/// </remarks>
public class InstanceService : IInstanceService
{
    private readonly string _appDir;
    
    private readonly IConfigService _configService;
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="InstanceService"/> class.
    /// </summary>
    /// <param name="appDir">The application data directory path.</param>
    /// <param name="configService">The configuration service for accessing settings.</param>
    public InstanceService(string appDir, IConfigService configService)
    {
        _appDir = appDir;
        _configService = configService;
    } 

    /// <summary>
    /// Gets the current configuration from the config service.
    /// </summary>
    /// <returns>The current configuration object.</returns>
    private Config GetConfig() => _configService.Configuration;
    
    /// <summary>
    /// Persists the current configuration to disk.
    /// </summary>
    /// <param name="config">The configuration object (parameter kept for API compatibility).</param>
    private void SaveConfig(Config config) => _configService.SaveConfig();

    #region Instance cache (instances.json)

    /// <summary>Returns the path to the instance cache file.</summary>
    private string GetInstanceCachePath() => Path.Combine(GetInstanceRoot(), "instances.json");

    /// <summary>
    /// Loads the instance list from instances.json.
    /// On first run migrates from the deprecated config.Instances field.
    /// </summary>
    private List<InstanceInfo> LoadInstanceCache()
    {
        var path = GetInstanceCachePath();
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<InstanceInfo>>(json, JsonOptions) ?? new();
            }
            catch (Exception ex)
            {
                Logger.Warning("InstanceService", $"Failed to read instances.json, rescanning: {ex.Message}");
            }
        }

        // Migration: seed from deprecated config field if present
        #pragma warning disable CS0618
        var config = GetConfig();
        if (config.Instances?.Count > 0)
        {
            Logger.Info("InstanceService", $"Migrating {config.Instances.Count} instances from config to instances.json");
            SaveInstanceCache(config.Instances);
            config.Instances = null;
            _configService.SaveConfig();
            return LoadInstanceCache();
        }
        #pragma warning restore CS0618

        return new List<InstanceInfo>();
    }

    /// <summary>Saves the instance list to instances.json.</summary>
    private void SaveInstanceCache(IEnumerable<InstanceInfo> instances)
    {
        try
        {
            var list = instances.ToList();
            var path = GetInstanceCachePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(list, JsonOptions));
        }
        catch (Exception ex)
        {
            Logger.Warning("InstanceService", $"Failed to save instances.json: {ex.Message}");
        }
    }

    /// <inheritdoc cref="IInstanceService.GetCachedInstances"/>
    public List<InstanceInfo> GetCachedInstances() => LoadInstanceCache();

    /// <inheritdoc/>
    public string GetInstanceRoot()
    {
        var config = GetConfig();
        var root = string.IsNullOrWhiteSpace(config.InstanceDirectory)
            ? Path.Combine(_appDir, "Instances")
            : config.InstanceDirectory;

        root = Environment.ExpandEnvironmentVariables(root);

        if (!Path.IsPathRooted(root))
        {
            root = Path.GetFullPath(Path.Combine(_appDir, root));
        }

        try
        {
            Directory.CreateDirectory(root);
        }
        catch (Exception ex)
        {
            Logger.Error("Config", $"Failed to create instance root at {root}: {ex.Message}");
        }

        return root;
    }

    /// <summary>
    /// Get the path for a specific branch (release/pre-release).
    /// </summary>
    public string GetBranchPath(string branch)
    {
        string normalizedBranch = NormalizeVersionType(branch);
        return Path.Combine(GetInstanceRoot(), normalizedBranch);
    }

    /// <summary>
    /// Get the UserData path for a specific instance version.
    /// </summary>
    public string GetInstanceUserDataPath(string versionPath)
    {
        return Path.Combine(versionPath, "UserData");
    }

    /// <summary>
    /// Resolve version to actual number. Returns 0 if not found.
    /// Checks in order: provided version > config.SelectedVersion > latest.json > local folders
    /// </summary>
    public int ResolveVersionOrLatest(string branch, int version)
    {
        var config = GetConfig();
        if (version > 0) return version;
        #pragma warning disable CS0618 // Backward compatibility: SelectedVersion and VersionType kept for migration
        if (config.SelectedVersion > 0) return config.SelectedVersion;

        var info = LoadLatestInfo(branch);
        if (info?.Version > 0) return info.Version;

        string resolvedBranch = string.IsNullOrWhiteSpace(branch) ? config.VersionType : branch;
        #pragma warning restore CS0618
        string branchDir = GetBranchPath(resolvedBranch);
        if (Directory.Exists(branchDir))
        {
            var latest = Directory.GetDirectories(branchDir)
                .Select(Path.GetFileName)
                .Select(name => int.TryParse(name, out var v) ? v : -1)
                .Where(v => v > 0)
                .OrderByDescending(v => v)
                .FirstOrDefault();
            return latest;
        }

        return 0;
    }

    /// <summary>
    /// Find existing instance path by branch and version.
    /// Checks multiple locations including legacy naming formats and GUID-named folders.
    /// </summary>
    public string? FindExistingInstancePath(string branch, int version)
    {
        string normalizedBranch = NormalizeVersionType(branch);
        string versionSegment = version == 0 ? "latest" : version.ToString();

        // Primary: flat structure — {root}/{guid}/
        var flatRoot = GetInstanceRoot();
        if (Directory.Exists(flatRoot))
        {
            foreach (var instanceDir in Directory.GetDirectories(flatRoot))
            {
                var dirName = Path.GetFileName(instanceDir);
                // Skip legacy branch subdirectories
                if (dirName.Equals("release", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("pre-release", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!Guid.TryParse(dirName, out _))
                    continue;

                var meta = GetInstanceMeta(instanceDir);
                if (meta == null) continue;
                if (!meta.Branch.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase)) continue;

                if (version == 0 && meta.IsLatest) return instanceDir;
                if (version > 0 && meta.Version == version) return instanceDir;
            }
        }

        // Legacy fallback: branch subdirectories and dash-layout roots
        foreach (var root in GetInstanceRootsIncludingLegacy())
        {
            var branchPath = Path.Combine(root, normalizedBranch);

            if (Directory.Exists(branchPath))
            {
                foreach (var instanceDir in Directory.GetDirectories(branchPath))
                {
                    var folderName = Path.GetFileName(instanceDir);

                    if (Guid.TryParse(folderName, out _))
                    {
                        var meta = GetInstanceMeta(instanceDir);
                        if (meta != null)
                        {
                            if (version == 0 && meta.IsLatest) return instanceDir;
                            if (version > 0 && meta.Version == version &&
                                meta.Branch.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase))
                                return instanceDir;
                        }
                    }

                    if (version == 0 && folderName.Equals("latest", StringComparison.OrdinalIgnoreCase))
                        return instanceDir;

                    if (version > 0 && folderName == version.ToString())
                        return instanceDir;
                }
            }

            var candidate2 = Path.Combine(root, $"{normalizedBranch}-{versionSegment}");
            if (Directory.Exists(candidate2)) return candidate2;

            var candidate3 = Path.Combine(root, $"{normalizedBranch}-v{versionSegment}");
            if (Directory.Exists(candidate3)) return candidate3;
        }

        return null;
    }

    /// <summary>
    /// Get all instance roots including legacy locations.
    /// </summary>
    public IEnumerable<string> GetInstanceRootsIncludingLegacy()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> YieldIfExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) yield break;
            if (!Directory.Exists(path)) yield break;

            var full = Path.GetFullPath(path);
            if (seen.Add(full))
            {
                yield return full;
            }
        }

        foreach (var root in YieldIfExists(GetInstanceRoot()))
        {
            yield return root;
        }

        foreach (var legacy in GetLegacyRoots())
        {
            // Check legacy naming: 'instance' (singular) and 'instances' (plural)
            foreach (var r in YieldIfExists(Path.Combine(legacy, "instance")))
            {
                yield return r;
            }

            foreach (var r in YieldIfExists(Path.Combine(legacy, "instances")))
            {
                yield return r;
            }
        }

        // Also check old 'instance' folder in current app dir (singular -> plural migration)
        var oldInstanceDir = Path.Combine(_appDir, "instance");
        foreach (var r in YieldIfExists(oldInstanceDir))
        {
            yield return r;
        }
    }

    /// <summary>
    /// Get path for latest instance symlink/info.
    /// </summary>
    public string GetLatestInstancePath(string branch)
    {
        return Path.Combine(GetBranchPath(branch), "latest");
    }

    /// <summary>
    /// Get path for latest.json file (legacy, used for migration only).
    /// </summary>
    public string GetLatestInfoPath(string branch)
    {
            return Path.Combine(GetBranchPath(branch), "latest.json");
    }

        private string GetLegacyLatestInfoPath(string branch)
        {
            return Path.Combine(GetLatestInstancePath(branch), "latest.json");
        }

    /// <summary>
    /// Load latest instance info.
    /// Reads from the "latest" instance's meta.json (InstalledVersion field).
    /// Falls back to legacy latest.json for migration.
    /// </summary>
    public LatestInstanceInfo? LoadLatestInfo(string branch)
    {
        try
        {
            var normalizedBranch = NormalizeVersionType(branch);

            // Primary: read from the "latest" instance's meta.json
            var latestPath = GetLatestInstancePath(normalizedBranch);
            if (Directory.Exists(latestPath))
            {
                var meta = GetInstanceMeta(latestPath);
                if (meta != null && meta.InstalledVersion > 0)
                {
                    return new LatestInstanceInfo { Version = meta.InstalledVersion, UpdatedAt = meta.LastPlayedAt ?? meta.CreatedAt };
                }
            }

            // Fallback: legacy latest.json files (migration path)
            var path = GetLatestInfoPath(normalizedBranch);
            if (!File.Exists(path))
            {
                path = GetLegacyLatestInfoPath(normalizedBranch);
                if (!File.Exists(path)) return null;
            }
            var json = File.ReadAllText(path);
            var info = JsonSerializer.Deserialize<LatestInstanceInfo>(json, JsonOptions);

            // Migrate: write to instance meta so legacy file is no longer needed
            if (info?.Version > 0 && Directory.Exists(latestPath))
            {
                var meta = GetInstanceMeta(latestPath);
                if (meta != null && meta.InstalledVersion == 0)
                {
                    meta.InstalledVersion = info.Version;
                    SaveInstanceMeta(latestPath, meta);
                    Logger.Info("Instance", $"Migrated InstalledVersion={info.Version} from latest.json to instance meta for {branch}");
                }
            }

            return info;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Save latest instance info.
    /// Updates the "latest" instance's meta.json InstalledVersion field.
    /// No longer creates latest.json files.
    /// </summary>
    public void SaveLatestInfo(string branch, int version)
    {
        try
        {
            var normalizedBranch = NormalizeVersionType(branch);
            var latestPath = GetLatestInstancePath(normalizedBranch);

            if (Directory.Exists(latestPath))
            {
                var meta = GetInstanceMeta(latestPath);
                if (meta != null)
                {
                    meta.InstalledVersion = version;
                    SaveInstanceMeta(latestPath, meta);
                    Logger.Debug("Instance", $"Updated InstalledVersion={version} in instance meta for {branch}");
                    return;
                }
            }

            // Fallback: search flat structure for a latest-type instance for this branch
            var latestFlat = GetInstalledInstances()
                .FirstOrDefault(i => i.Version == 0 &&
                    i.Branch.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase));
            if (latestFlat != null)
            {
                var flatMeta = GetInstanceMeta(latestFlat.Path);
                if (flatMeta != null)
                {
                    flatMeta.InstalledVersion = version;
                    SaveInstanceMeta(latestFlat.Path, flatMeta);
                    Logger.Debug("Instance", $"Updated InstalledVersion={version} for latest instance {latestFlat.Id}");
                    return;
                }
            }

            Logger.Warning("Instance", $"SaveLatestInfo: no latest instance found for branch '{branch}', skipping");
        }
        catch (Exception ex)
        {
            Logger.Error("Instance", $"Failed to save latest info: {ex.Message}");
        }
    }


    /// <summary>
    /// Safely copy directory recursively, preventing infinite loops.
    /// </summary>
    public static void SafeCopyDirectory(string sourceDir, string destDir)
    {
        // Use UtilityService implementation which now has infinite loop protection
        UtilityService.CopyDirectory(sourceDir, destDir, false);
    }

    /// <summary>
    /// Normalize version type: "prerelease" or "pre-release" -> "pre-release"
    /// </summary>
    public static string NormalizeVersionType(string versionType)
    {
        return UtilityService.NormalizeVersionType(versionType);
    }
    
    /// <summary>
    /// Checks if the game client executable exists at the specified version path.
    /// Tries multiple layouts: new layout (Client/...) and legacy layout (game/Client/...).
    /// </summary>
    public bool IsClientPresent(string versionPath)
    {
        var subfolders = new[] { "", "game" };

        foreach (var sub in subfolders)
        {
            string basePath = string.IsNullOrEmpty(sub) ? versionPath : Path.Combine(versionPath, sub);
            string clientPath;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                clientPath = Path.Combine(basePath, "Client", "Hytale.app", "Contents", "MacOS", "HytaleClient");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                clientPath = Path.Combine(basePath, "Client", "HytaleClient.exe");
            }
            else
            {
                clientPath = Path.Combine(basePath, "Client", "HytaleClient");
            }

            if (File.Exists(clientPath))
            {
                Logger.Info("Version", $"Client found at {clientPath}");
                return true;
            }
        }

        Logger.Info("Version", $"Client not found in {versionPath}");
        return false;
    }

    /// <summary>
    /// Checks if game assets are present at the specified version path.
    /// </summary>
    public bool AreAssetsPresent(string versionPath)
    {
        string assetsCheck;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            assetsCheck = Path.Combine(versionPath, "Client", "Hytale.app", "Contents", "Assets");
        }
        else
        {
            assetsCheck = Path.Combine(versionPath, "Client", "Assets");
        }

        bool exists = Directory.Exists(assetsCheck) && Directory.EnumerateFileSystemEntries(assetsCheck).Any();
        Logger.Info("Version", $"AreAssetsPresent: path={assetsCheck}, exists={exists}");
        return exists;
    }

    /// <summary>
    /// Gets the path to a specific instance version. Returns latest path if version is 0.
    /// Searches existing instances by branch/version using meta.json.
    /// If not found, returns a path for a new instance (but does not create it).
    /// </summary>
    public string GetInstancePath(string branch, int version)
    {
        if (version == 0)
        {
            return GetLatestInstancePath(branch);
        }
        
        string normalizedBranch = NormalizeVersionType(branch);
        var flatRoot = GetInstanceRoot();

        // Primary: flat structure — {root}/{guid}/
        if (Directory.Exists(flatRoot))
        {
            foreach (var instanceDir in Directory.GetDirectories(flatRoot))
            {
                var dirName = Path.GetFileName(instanceDir);
                if (dirName.Equals("release", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("pre-release", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!Guid.TryParse(dirName, out _))
                    continue;
                var meta = GetInstanceMeta(instanceDir);
                if (meta != null && meta.Version == version &&
                    meta.Branch.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase))
                    return instanceDir;
            }
        }

        // Legacy fallback: branch subdirectory — {root}/{branch}/{...}/
        var branchPath = Path.Combine(flatRoot, normalizedBranch);
        if (Directory.Exists(branchPath))
        {
            foreach (var instanceDir in Directory.GetDirectories(branchPath))
            {
                var folderName = Path.GetFileName(instanceDir);

                // Skip "latest" folder
                if (folderName.Equals("latest", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Legacy version-named folder
                if (folderName == version.ToString())
                    return instanceDir;

                // Check meta.json for matching version
                var meta = GetInstanceMeta(instanceDir);
                if (meta != null && meta.Version == version &&
                    meta.Branch.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase))
                    return instanceDir;
            }
        }

        // Not found - return a placeholder flat path; callers creating instances should use CreateInstanceDirectory
        return Path.Combine(flatRoot, version.ToString());
    }

    /// <summary>
    /// Resolves the instance path, optionally preferring existing legacy paths.
    /// </summary>
    public string ResolveInstancePath(string branch, int version, bool preferExisting)
    {
        if (preferExisting)
        {
            var existing = FindExistingInstancePath(branch, version);
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }
        }

        return GetInstancePath(branch, version);
    }

    #endregion

    #region Legacy Config Migration

    /// <summary>
    /// Gets the list of legacy installation root directories to search for migrations.
    /// </summary>
    private IEnumerable<string> GetLegacyRoots()
    {
        var roots = new List<string>();
        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            roots.Add(path);
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Add(Path.Combine(appData, "hyprism"));
            Add(Path.Combine(appData, "Hyprism"));
            Add(Path.Combine(appData, "HyPrism")); // legacy casing
            Add(Path.Combine(appData, "HyPrismLauncher"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Add(Path.Combine(home, "Library", "Application Support", "hyprism"));
            Add(Path.Combine(home, "Library", "Application Support", "Hyprism"));
        }
        else
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrWhiteSpace(xdg))
            {
                Add(Path.Combine(xdg, "hyprism"));
                Add(Path.Combine(xdg, "Hyprism"));
            }
            Add(Path.Combine(home, ".local", "share", "hyprism"));
            Add(Path.Combine(home, ".local", "share", "Hyprism"));
        }

        return roots;
    }

    #endregion

    /// <summary>
    /// Deletes a game instance by branch and version number.
    /// Also removes latest.json for latest instances (version 0).
    /// </summary>
    public bool DeleteGame(string branch, int versionNumber)
    {
        try
        {
            string normalizedBranch = UtilityService.NormalizeVersionType(branch);
            string versionPath = ResolveInstancePath(normalizedBranch, versionNumber, true);
            
            if (Directory.Exists(versionPath))
            {
                Directory.Delete(versionPath, true);
            }
            
            if (versionNumber == 0)
            {
                var infoPath = GetLatestInfoPath(normalizedBranch);
                if (File.Exists(infoPath))
                {
                    File.Delete(infoPath);
                }
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Error deleting game: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Deletes a game instance by unique ID.
    /// </summary>
    public bool DeleteGameById(string instanceId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return false;
            }

            var info = FindInstanceById(instanceId);
            var versionPath = GetInstancePathById(instanceId);
            if (string.IsNullOrWhiteSpace(versionPath) || !Directory.Exists(versionPath))
            {
                return false;
            }

            Directory.Delete(versionPath, true);

            if (info?.Version == 0)
            {
                var infoPath = GetLatestInfoPath(info.Branch);
                if (File.Exists(infoPath))
                {
                    File.Delete(infoPath);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Error deleting game by id: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Scan for all installed instances in the standard hierarchy.
    /// </summary>
    public List<InstalledInstance> GetInstalledInstances()
    {
        var results = new List<InstalledInstance>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = GetInstanceRoot();

        if (!Directory.Exists(root)) return results;

        // Shared logic for processing a single instance folder.
        // branchHint is provided when descending into a legacy branch subdirectory.
        void ProcessFolder(string folder, string? branchHint)
        {
            var dirName = Path.GetFileName(folder);
            string? customName = null;
            string instanceId = "";
            int version = -1;
            bool isLatest = false;
            string branch = branchHint ?? "";
            var metaPath = Path.Combine(folder, "meta.json");

            if (File.Exists(metaPath))
            {
                try
                {
                    var json = File.ReadAllText(metaPath);
                    var meta = JsonSerializer.Deserialize<InstanceMeta>(json, JsonOptions);
                    if (meta != null)
                    {
                        instanceId = meta.Id ?? "";
                        customName = meta.Name;
                        version = meta.Version;
                        isLatest = meta.IsLatest;
                        if (!string.IsNullOrEmpty(meta.Branch))
                            branch = meta.Branch;
                    }
                }
                catch { }
            }

            // If no meta.json, try to parse folder name
            if (version < 0)
            {
                if (string.Equals(dirName, "latest", StringComparison.OrdinalIgnoreCase))
                {
                    version = 0;
                    isLatest = true;
                }
                else if (int.TryParse(dirName, out var parsedVersion))
                {
                    version = parsedVersion;
                }
                else if (Guid.TryParse(dirName, out _))
                {
                    Logger.Warning("InstanceService", $"GUID folder without meta.json: {folder}");
                    return;
                }
                else
                {
                    return; // Unknown folder format
                }
            }

            var userDataPath = Path.Combine(folder, "UserData");
            bool hasUserData = Directory.Exists(userDataPath);
            long size = 0;
            if (hasUserData)
            {
                try { size = new DirectoryInfo(userDataPath).EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length); }
                catch { }
            }

            long totalSize = 0;
            try { totalSize = new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length); }
            catch { }

            // Fallback: legacy metadata.json for custom name
            if (string.IsNullOrEmpty(instanceId))
            {
                var metadataPath = Path.Combine(folder, "metadata.json");
                if (File.Exists(metadataPath))
                {
                    try
                    {
                        var json = File.ReadAllText(metadataPath);
                        var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
                        metadata?.TryGetValue("customName", out customName);
                    }
                    catch { }
                }
            }

            // Generate ID if not found and persist it
            if (string.IsNullOrEmpty(instanceId))
            {
                if (!IsClientPresent(folder))
                {
                    Logger.Debug("InstanceService", $"Skipping non-installed placeholder folder: {folder}");
                    return;
                }

                instanceId = Guid.NewGuid().ToString();
                try
                {
                    var newMeta = new InstanceMeta
                    {
                        Id = instanceId,
                        Name = customName ?? "",
                        Branch = branch,
                        Version = version,
                        CreatedAt = DateTime.UtcNow,
                        IsLatest = isLatest
                    };
                    var json = JsonSerializer.Serialize(newMeta, JsonOptions);
                    File.WriteAllText(metaPath, json);
                    Logger.Debug("InstanceService", $"Generated and persisted ID for {branch}/{version}: {instanceId}");
                }
                catch (Exception ex)
                {
                    Logger.Warning("InstanceService", $"Failed to persist generated ID: {ex.Message}");
                }
            }

            // Deduplicate: flat scan takes precedence; skip if already seen from flat scan
            if (!string.IsNullOrEmpty(instanceId) && !seenIds.Add(instanceId))
            {
                Logger.Debug("InstanceService", $"Skipping duplicate instance {instanceId} at {folder}");
                return;
            }

            var validationResult = ValidateGameIntegrity(folder);

            results.Add(new InstalledInstance
            {
                Id = instanceId,
                Branch = branch,
                Version = version,
                Path = folder,
                HasUserData = hasUserData,
                UserDataSize = size,
                TotalSize = totalSize,
                IsValid = validationResult.Status == InstanceValidationStatus.Valid,
                ValidationStatus = validationResult.Status,
                ValidationDetails = validationResult.Details,
                CustomName = customName
            });
        }

        // Primary: flat structure — {root}/{guid}/
        foreach (var folder in Directory.GetDirectories(root))
        {
            var dirName = Path.GetFileName(folder);
            // Skip legacy branch subdirectories (handled below)
            if (dirName.Equals("release", StringComparison.OrdinalIgnoreCase) ||
                dirName.Equals("pre-release", StringComparison.OrdinalIgnoreCase))
                continue;
            // Only process GUID-named entries at root level
            if (!Guid.TryParse(dirName, out _))
                continue;
            ProcessFolder(folder, null);
        }

        // Legacy fallback: branch subdirectories — {root}/{branch}/{...}/
        foreach (var branch in new[] { "release", "pre-release" })
        {
            var branchDir = Path.Combine(root, branch);
            if (!Directory.Exists(branchDir)) continue;
            try
            {
                foreach (var folder in Directory.GetDirectories(branchDir))
                    ProcessFolder(folder, branch);
            }
            catch (Exception ex)
            {
                Logger.Error("InstanceService", $"Error scanning branch {branch}: {ex.Message}");
            }
        }

        return results.OrderByDescending(x => x.Version).ToList();
    }

    /// <summary>
    /// Performs deep validation of a game instance, checking all critical components.
    /// Returns detailed information about what's present and what's missing.
    /// </summary>
    public (InstanceValidationStatus Status, InstanceValidationDetails Details) ValidateGameIntegrity(string folder)
    {
        var details = new InstanceValidationDetails();
        var missingComponents = new List<string>();
        
        try
        {
            // 1. Check if the folder exists at all
            if (!Directory.Exists(folder))
            {
                details.ErrorMessage = "Instance directory does not exist";
                return (InstanceValidationStatus.NotInstalled, details);
            }

            // 2. Check for the executable (most critical)
            details.HasExecutable = CheckExecutablePresent(folder);
            if (!details.HasExecutable)
            {
                missingComponents.Add("Game executable");
            }

            // 3. Check for assets folder
            details.HasAssets = CheckAssetsPresent(folder);
            if (!details.HasAssets)
            {
                missingComponents.Add("Game assets");
            }

            // 4. Check for libraries/dependencies
            details.HasLibraries = CheckLibrariesPresent(folder);
            if (!details.HasLibraries)
            {
                missingComponents.Add("Game libraries");
            }

            // 5. Check for essential config files
            details.HasConfig = CheckConfigPresent(folder);
            if (!details.HasConfig)
            {
                // Config missing is not critical, just a warning
            }

            details.MissingComponents = missingComponents;

            // Determine overall status based on what's present
            // If we have the executable, consider it valid - we can't reliably verify file integrity
            if (details.HasExecutable)
            {
                return (InstanceValidationStatus.Valid, details);
            }
            else if (!details.HasExecutable && !details.HasAssets && !details.HasLibraries)
            {
                // Nothing is there - not installed
                return (InstanceValidationStatus.NotInstalled, details);
            }
            else
            {
                // Has some files but no executable - corrupted
                details.ErrorMessage = "Game executable is missing or corrupted";
                return (InstanceValidationStatus.Corrupted, details);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("InstanceService", $"Error validating instance {folder}: {ex.Message}");
            details.ErrorMessage = ex.Message;
            return (InstanceValidationStatus.Unknown, details);
        }
    }

    /// <summary>
    /// Checks if the game executable is present at the specified path.
    /// </summary>
    private bool CheckExecutablePresent(string folder)
    {
        string clientPath;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            clientPath = Path.Combine(folder, "Client", "Hytale.app", "Contents", "MacOS", "HytaleClient");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            clientPath = Path.Combine(folder, "Client", "HytaleClient.exe");
        }
        else
        {
            clientPath = Path.Combine(folder, "Client", "HytaleClient");
        }
        return File.Exists(clientPath);
    }

    /// <summary>
    /// Checks if game assets are present and contain actual files.
    /// </summary>
    private bool CheckAssetsPresent(string folder)
    {
        string assetsPath;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            assetsPath = Path.Combine(folder, "Client", "Hytale.app", "Contents", "Assets");
        }
        else
        {
            assetsPath = Path.Combine(folder, "Client", "Assets");
        }

        if (!Directory.Exists(assetsPath))
        {
            return false;
        }

        // Check that assets folder is not empty and contains expected subfolders
        try
        {
            var entries = Directory.GetFileSystemEntries(assetsPath);
            if (entries.Length == 0)
            {
                return false;
            }

            // Check for at least some expected asset folders/files
            // This is a basic sanity check
            return entries.Length >= 3; // Should have multiple folders
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if required libraries/dependencies are present.
    /// </summary>
    private bool CheckLibrariesPresent(string folder)
    {
        // On macOS, libraries are bundled in the app bundle
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var frameworksPath = Path.Combine(folder, "Client", "Hytale.app", "Contents", "Frameworks");
            if (Directory.Exists(frameworksPath))
            {
                return Directory.EnumerateFileSystemEntries(frameworksPath).Any();
            }
            // Also check MonoBleedingEdge if using Mono runtime
            var monoPath = Path.Combine(folder, "Client", "Hytale.app", "Contents", "MonoBleedingEdge");
            return Directory.Exists(monoPath) && Directory.EnumerateFileSystemEntries(monoPath).Any();
        }
        
        // On Windows/Linux, check for typical library locations
        var clientFolder = Path.Combine(folder, "Client");
        if (!Directory.Exists(clientFolder))
        {
            return false;
        }

        // Check for DLLs on Windows or .so files on Linux
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Check for any DLL files or Mono runtime
            var monoPath = Path.Combine(clientFolder, "MonoBleedingEdge");
            var hasMono = Directory.Exists(monoPath);
            var hasDlls = Directory.EnumerateFiles(clientFolder, "*.dll", SearchOption.TopDirectoryOnly).Any();
            return hasMono || hasDlls;
        }
        else
        {
            // Linux - check for .so files or Mono
            var monoPath = Path.Combine(clientFolder, "MonoBleedingEdge");
            var hasMono = Directory.Exists(monoPath);
            var hasSo = Directory.EnumerateFiles(clientFolder, "*.so*", SearchOption.TopDirectoryOnly).Any();
            return hasMono || hasSo;
        }
    }

    /// <summary>
    /// Checks if essential config files are present.
    /// </summary>
    private bool CheckConfigPresent(string folder)
    {
        // Check for common config files
        var configFiles = new[]
        {
            Path.Combine(folder, "Client", "boot.config"),
            Path.Combine(folder, "Client", "globalgamemanagers"),
            Path.Combine(folder, "Client", "level0"),
        };

        // On macOS, config is inside the app bundle
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var dataPath = Path.Combine(folder, "Client", "Hytale.app", "Contents", "Data");
            return Directory.Exists(dataPath);
        }

        // At least one config file should exist
        return configFiles.Any(File.Exists) || 
               Directory.Exists(Path.Combine(folder, "Client", "HytaleClient_Data"));
    }

    private bool CheckInstanceValidity(string folder)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return File.Exists(Path.Combine(folder, "Client", "Hytale.app", "Contents", "MacOS", "HytaleClient"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return File.Exists(Path.Combine(folder, "Client", "HytaleClient.exe"));
        }
        else
        {
            return File.Exists(Path.Combine(folder, "Client", "HytaleClient"));
        }
    }

    public void SetInstanceCustomName(string branch, int version, string? customName)
    {
        // Use GetInstancePath which properly searches GUID-named folders
        var instancePath = GetInstancePath(branch, version);
        
        if (string.IsNullOrEmpty(instancePath) || !Directory.Exists(instancePath))
        {
            Logger.Warning("InstanceService", $"Instance not found: {branch}/{version}");
            return;
        }

        SetInstanceNameInternal(instancePath, customName, $"{branch}/{version}");
    }

    public void SetInstanceCustomNameById(string instanceId, string? customName)
    {
        var instancePath = GetInstancePathById(instanceId);
        
        if (string.IsNullOrEmpty(instancePath) || !Directory.Exists(instancePath))
        {
            Logger.Warning("InstanceService", $"Instance not found by ID: {instanceId}");
            return;
        }

        SetInstanceNameInternal(instancePath, customName, instanceId);
    }

    private void SetInstanceNameInternal(string instancePath, string? customName, string logIdentifier)
    {
        try
        {
            // Load or create meta.json
            var meta = GetInstanceMeta(instancePath);
            if (meta == null)
            {
                Logger.Warning("InstanceService", $"No meta.json found for instance: {logIdentifier}");
                return;
            }

            // Update existing meta's Name field
            meta.Name = string.IsNullOrWhiteSpace(customName) 
                ? (meta.IsLatest ? $"{meta.Branch} (Latest)" : $"{meta.Branch} v{meta.Version}")
                : customName;

            // Save meta.json
            SaveInstanceMeta(instancePath, meta);
            
            // Also update Config.Instances for quick lookup
            SyncInstancesWithConfig();
            
            Logger.Info("InstanceService", $"Updated instance name for {logIdentifier}: {meta.Name}");
        }
        catch (Exception ex)
        {
            Logger.Error("InstanceService", $"Failed to save instance name: {ex.Message}");
        }
    }

    #region Instance Meta Management

    /// <inheritdoc/>
    public InstanceMeta? GetInstanceMeta(string instancePath)
    {
        var metaPath = Path.Combine(instancePath, "meta.json");
        if (!File.Exists(metaPath))
        {
            // Try legacy metadata.json
            var legacyPath = Path.Combine(instancePath, "metadata.json");
            if (File.Exists(legacyPath))
            {
                return MigrateLegacyMetadata(instancePath, legacyPath);
            }
            return null;
        }

        try
        {
            var json = File.ReadAllText(metaPath);
            return JsonSerializer.Deserialize<InstanceMeta>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Logger.Warning("InstanceService", $"Failed to load meta.json: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public void SaveInstanceMeta(string instancePath, InstanceMeta meta)
    {
        try
        {
            Directory.CreateDirectory(instancePath);
            var metaPath = Path.Combine(instancePath, "meta.json");
            var json = JsonSerializer.Serialize(meta, JsonOptions);
            File.WriteAllText(metaPath, json);
            Logger.Debug("InstanceService", $"Saved meta.json for instance {meta.Id}");
        }
        catch (Exception ex)
        {
            Logger.Error("InstanceService", $"Failed to save meta.json: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public InstanceMeta CreateInstanceMeta(string branch, int version, string? name = null, bool isLatest = false)
    {
        var normalizedBranch = NormalizeVersionType(branch);
        
        // For "latest" instances, check if one already exists (only one latest per branch)
        if (isLatest)
        {
            var existingLatest = FindInstanceByBranchAndVersion(normalizedBranch, 0);
            if (existingLatest != null)
            {
                var existingPath = GetInstancePathById(existingLatest.Id);
                if (!string.IsNullOrEmpty(existingPath))
                {
                    var existingMeta = GetInstanceMeta(existingPath);
                    if (existingMeta != null && existingMeta.IsLatest)
                    {
                        Logger.Debug("InstanceService", $"Latest instance already exists for {branch}");
                        return existingMeta;
                    }
                }
            }
        }
        // For non-latest instances, we allow multiple instances of the same version
        // Each will have a unique ID and folder

        // All instances (including latest) use the flat structure: {root}/{instanceId}
        string instancePath;
        string instanceId = Guid.NewGuid().ToString();
        instancePath = CreateInstanceDirectory(normalizedBranch, instanceId);

        // Check if meta already exists at path (edge case)
        var pathMeta = GetInstanceMeta(instancePath);
        if (pathMeta != null)
        {
            Logger.Debug("InstanceService", $"Instance meta already exists at path {instancePath}");
            return pathMeta;
        }

        // Create new meta with generated ID
        var meta = new InstanceMeta
        {
            Id = instanceId,
            Name = name ?? (isLatest ? $"{normalizedBranch} (Latest)" : $"{normalizedBranch} v{version}"),
            Branch = normalizedBranch,
            Version = version,
            CreatedAt = DateTime.UtcNow,
            IsLatest = isLatest
        };

        // Save to disk
        SaveInstanceMeta(instancePath, meta);

        // Also add to instances.json cache
        var cachedInstances = LoadInstanceCache();
        if (!cachedInstances.Any(i => i.Id == meta.Id))
        {
            cachedInstances.Add(new InstanceInfo
            {
                Id = meta.Id,
                Name = meta.Name,
                Branch = meta.Branch,
                Version = meta.Version
            });
            SaveInstanceCache(cachedInstances);
        }

        Logger.Info("InstanceService", $"Created instance meta: {meta.Id} ({meta.Name})");
        return meta;
    }

    /// <inheritdoc/>
    public InstanceInfo? GetSelectedInstance()
    {
        var config = GetConfig();
        if (string.IsNullOrEmpty(config.SelectedInstanceId))
            return null;

        var info = FindInstanceById(config.SelectedInstanceId);
        if (info == null)
            return null;

        // Check if the instance is actually installed (game files exist)
        // Use GetInstancePathById first (GUID-named folders), fall back to legacy search
        var instancePath = GetInstancePathById(info.Id);
        if (string.IsNullOrEmpty(instancePath))
        {
            instancePath = FindExistingInstancePath(info.Branch, info.Version);
        }
        
        if (!string.IsNullOrEmpty(instancePath))
        {
            var (status, _) = ValidateGameIntegrity(instancePath);
            info.IsInstalled = status == InstanceValidationStatus.Valid;
        }
        else
        {
            info.IsInstalled = false;
        }

        return info;
    }

    /// <inheritdoc/>
    public void SetSelectedInstance(string instanceId)
    {
        var config = GetConfig();

        var selected = FindInstanceById(instanceId);
        if (selected == null)
        {
            Logger.Warning("InstanceService", $"SetSelectedInstance ignored: instance not found ({instanceId})");
            return;
        }

        config.SelectedInstanceId = instanceId;

        // Keep legacy launch config in sync with selected instance so launch paths
        // that still read VersionType/SelectedVersion target the same instance.
        #pragma warning disable CS0618 // Backward compatibility: VersionType and SelectedVersion kept for migration
        config.VersionType = NormalizeVersionType(selected.Branch);
        config.SelectedVersion = selected.Version;
        #pragma warning restore CS0618

        SaveConfig(config);
        Logger.Info("InstanceService", $"Selected instance: {instanceId} ({selected.Branch} v{selected.Version})");
    }

    /// <inheritdoc/>
    public InstanceInfo? FindInstanceById(string instanceId)
    {
        // First check cached list
        var info = LoadInstanceCache().FirstOrDefault(i => i.Id == instanceId);
        if (info != null)
            return info;

        // If not in cache, scan disk and rebuild
        SyncInstancesWithConfig();
        return LoadInstanceCache().FirstOrDefault(i => i.Id == instanceId);
    }

    /// <inheritdoc/>
    public void SyncInstancesWithConfig()
    {
        var config = GetConfig();
        var discoveredById = new Dictionary<string, InstanceInfo>(StringComparer.OrdinalIgnoreCase);

        void ProcessInstanceDir(string instanceDir)
        {
            var meta = GetInstanceMeta(instanceDir);
            if (meta == null) return;

            if (string.IsNullOrWhiteSpace(meta.Id))
            {
                meta.Id = Guid.NewGuid().ToString();
                SaveInstanceMeta(instanceDir, meta);
                Logger.Warning("InstanceService", $"Recovered empty instance ID at {instanceDir}: generated {meta.Id}");
            }

            if (discoveredById.ContainsKey(meta.Id))
            {
                Logger.Warning("InstanceService", $"Duplicate instance ID detected during sync: {meta.Id}. Keeping first entry and skipping {instanceDir}");
                return;
            }

            discoveredById[meta.Id] = new InstanceInfo
            {
                Id = meta.Id,
                Name = meta.Name,
                Branch = meta.Branch,
                Version = meta.Version
            };
        }

        foreach (var root in GetInstanceRootsIncludingLegacy())
        {
            if (!Directory.Exists(root)) continue;

            // Primary: flat structure — {root}/{guid}/
            foreach (var instanceDir in Directory.GetDirectories(root))
            {
                var dirName = Path.GetFileName(instanceDir);
                // Skip legacy branch subdirectories — handled below
                if (dirName.Equals("release", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("pre-release", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!Guid.TryParse(dirName, out _))
                    continue;
                ProcessInstanceDir(instanceDir);
            }

            // Legacy fallback: branch subdirectories — {root}/{branch}/{guid}/
            foreach (var branchDir in Directory.GetDirectories(root))
            {
                var branchName = Path.GetFileName(branchDir);
                if (!branchName.Equals("release", StringComparison.OrdinalIgnoreCase) &&
                    !branchName.Equals("pre-release", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var instanceDir in Directory.GetDirectories(branchDir))
                    ProcessInstanceDir(instanceDir);
            }
        }

        var synced = discoveredById.Values
            .OrderBy(i => i.Branch)
            .ThenByDescending(i => i.Version)
            .ToList();

        SaveInstanceCache(synced);
        Logger.Debug("InstanceService", $"Synced {synced.Count} instances with config");
    }

    /// <summary>
    /// Migrates legacy metadata.json to new meta.json format.
    /// </summary>
    private InstanceMeta? MigrateLegacyMetadata(string instancePath, string legacyPath)
    {
        try
        {
            var json = File.ReadAllText(legacyPath);
            var legacyData = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            
            // Parse branch and version from path
            var dirName = Path.GetFileName(instancePath);
            var parentName = Path.GetFileName(Path.GetDirectoryName(instancePath) ?? "");
            
            int version = 0;
            bool isLatest = dirName.Equals("latest", StringComparison.OrdinalIgnoreCase);
            if (!isLatest && int.TryParse(dirName, out var parsedVersion))
            {
                version = parsedVersion;
            }

            var meta = new InstanceMeta
            {
                Id = Guid.NewGuid().ToString(),
                Name = legacyData?.GetValueOrDefault("customName") ?? (isLatest ? $"{parentName} (Latest)" : $"{parentName} v{version}"),
                Branch = parentName,
                Version = version,
                CreatedAt = DateTime.UtcNow,
                IsLatest = isLatest
            };

            // Save new format
            SaveInstanceMeta(instancePath, meta);

            // Delete legacy file
            try { File.Delete(legacyPath); } catch { }

            Logger.Info("InstanceService", $"Migrated legacy metadata to meta.json: {meta.Id}");
            return meta;
        }
        catch (Exception ex)
        {
            Logger.Warning("InstanceService", $"Failed to migrate legacy metadata: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public string? GetInstancePathById(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId))
            return null;

        var root = GetInstanceRoot();
        if (!Directory.Exists(root))
            return null;

        // Primary: flat structure — {root}/{instanceId}
        var flatPath = Path.Combine(root, instanceId);
        if (Directory.Exists(flatPath))
            return flatPath;

        // Legacy fallback: branch subdirectories — {root}/{branch}/{instanceId or version-name}
        foreach (var branchDir in Directory.GetDirectories(root))
        {
            var branchName = Path.GetFileName(branchDir);
            // Only descend into known branch directories to avoid scanning GUID siblings
            if (!branchName.Equals("release", StringComparison.OrdinalIgnoreCase) &&
                !branchName.Equals("pre-release", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var instanceDir in Directory.GetDirectories(branchDir))
            {
                var folderName = Path.GetFileName(instanceDir);
                if (folderName == instanceId)
                    return instanceDir;

                var meta = GetInstanceMeta(instanceDir);
                if (meta?.Id == instanceId)
                    return instanceDir;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public InstanceInfo? FindInstanceByBranchAndVersion(string branch, int version)
    {
        var normalizedBranch = NormalizeVersionType(branch);
        var config = GetConfig();

        // Legacy fallback: some old config.json files still have Instances embedded.
#pragma warning disable CS0618
        var legacyInfo = config.Instances?.FirstOrDefault(i =>
            i.Branch.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase) && i.Version == version);
#pragma warning restore CS0618
        if (legacyInfo != null)
            return legacyInfo;

        // Check cache first before scanning disk
        var cached = LoadInstanceCache();
        var cachedMatch = cached.FirstOrDefault(i =>
            i.Branch.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase) && i.Version == version);
        if (cachedMatch != null)
            return cachedMatch;

        // If not in cache, scan disk — primary: flat structure {root}/{guid}/
        var root = GetInstanceRoot();
        if (Directory.Exists(root))
        {
            foreach (var instanceDir in Directory.GetDirectories(root))
            {
                var dirName = Path.GetFileName(instanceDir);
                if (dirName.Equals("release", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("pre-release", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!Guid.TryParse(dirName, out _))
                    continue;
                var meta = GetInstanceMeta(instanceDir);
                if (meta != null && meta.Branch.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase) && meta.Version == version)
                    return new InstanceInfo { Id = meta.Id, Name = meta.Name, Branch = meta.Branch, Version = meta.Version };
            }
        }

        // Legacy fallback: branch subdirectory — {root}/{branch}/{...}/
        var branchPath = GetBranchPath(normalizedBranch);
        if (!Directory.Exists(branchPath))
            return null;

        foreach (var instanceDir in Directory.GetDirectories(branchPath))
        {
            var meta = GetInstanceMeta(instanceDir);
            if (meta != null && meta.Branch.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase) && meta.Version == version)
                return new InstanceInfo { Id = meta.Id, Name = meta.Name, Branch = meta.Branch, Version = meta.Version };
        }

        return null;
    }

    /// <inheritdoc/>
    public string CreateInstanceDirectory(string branch, string instanceId)
    {
        // Branch is retained as a parameter for callers that pass it for metadata purposes,
        // but the folder is created flat at {InstanceRoot}/{instanceId} — no branch subdir.
        var path = Path.Combine(GetInstanceRoot(), instanceId);
        Directory.CreateDirectory(path);
        return path;
    }


    /// <summary>
    /// Changes the version/branch of an existing instance.
    /// For upgrades within the same branch: preserves game files and sets up for patching.
    /// For downgrades or branch changes: removes game client files and prepares for fresh download.
    /// Always keeps UserData and meta.json, and marks IsLatest = false.
    /// </summary>
    public bool ChangeInstanceVersion(string instanceId, string branch, int version)
    {
        try
        {
            var instancePath = GetInstancePathById(instanceId);
            if (string.IsNullOrEmpty(instancePath) || !Directory.Exists(instancePath))
            {
                Logger.Warning("InstanceService", $"ChangeInstanceVersion: instance path not found for {instanceId}");
                return false;
            }

            var meta = GetInstanceMeta(instancePath);
            if (meta == null)
            {
                Logger.Warning("InstanceService", $"ChangeInstanceVersion: meta.json not found for {instanceId}");
                return false;
            }

            var normalizedBranch = UtilityService.NormalizeVersionType(branch);
            var currentBranch = UtilityService.NormalizeVersionType(meta.Branch);
            var currentInstalledVersion = meta.InstalledVersion;
            var hasInstalledGame = currentInstalledVersion > 0 && IsClientPresent(instancePath);

            // Determine if we can use patching:
            // - Same branch (or compatible branches)
            // - Upgrade (target version > installed version)
            // - Game is actually installed
            bool canUsePatch = hasInstalledGame
                && currentBranch == normalizedBranch
                && version > currentInstalledVersion;

            if (canUsePatch)
            {
                // PATCH MODE: Keep game files, set up for differential update
                Logger.Info("InstanceService", $"Patch mode: upgrading {instanceId} from v{currentInstalledVersion} to v{version}");
                
                // Set PendingVersion to trigger patching on next launch
                meta.Branch = normalizedBranch;
                meta.Version = version;
                meta.PendingVersion = version;
                // Keep InstalledVersion as-is so patcher knows the starting point
                meta.IsLatest = false;

                SaveInstanceMeta(instancePath, meta);
            }
            else
            {
                // FULL DOWNLOAD MODE: Remove game files for clean install
                Logger.Info("InstanceService", $"Full download mode: {instanceId} from {currentBranch} v{currentInstalledVersion} to {normalizedBranch} v{version}");
                
                // Remove game client directories while keeping UserData and meta.json
                var clientDir = Path.Combine(instancePath, "Client");
                var gameDir = Path.Combine(instancePath, "game");

                if (Directory.Exists(clientDir))
                {
                    Directory.Delete(clientDir, true);
                    Logger.Info("InstanceService", $"Removed Client directory for {instanceId}");
                }

                if (Directory.Exists(gameDir))
                {
                    Directory.Delete(gameDir, true);
                    Logger.Info("InstanceService", $"Removed game directory for {instanceId}");
                }

                // Update meta.json with new branch, version, and mark as non-latest
                meta.Branch = normalizedBranch;
                meta.Version = version;
                meta.InstalledVersion = 0;
                meta.PendingVersion = 0;
                meta.IsLatest = false;

                SaveInstanceMeta(instancePath, meta);
            }

            // Update instances.json cache entry
            var cachedInstances = LoadInstanceCache();
            var cachedEntry = cachedInstances.FirstOrDefault(i => i.Id == instanceId);
            if (cachedEntry != null)
            {
                cachedEntry.Branch = normalizedBranch;
                cachedEntry.Version = version;
                SaveInstanceCache(cachedInstances);
            }

            var mode = canUsePatch ? "patch" : "full-download";
            Logger.Success("InstanceService", $"Changed instance {instanceId} to {normalizedBranch} v{version} (non-latest, mode={mode})");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("InstanceService", $"Failed to change instance version: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region ZIP Import

    private static readonly JsonSerializerOptions ImportJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <inheritdoc/>
    public async Task ImportFromZipAsync(string zipPath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hyprism-import-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, tempDir, true));

        var metaPath = Path.Combine(tempDir, "meta.json");
        var branch = "release";
        var version = 0;
        string? existingId = null;

        if (File.Exists(metaPath))
        {
            var meta = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(metaPath), ImportJsonOpts);
            branch = meta?.TryGetValue("branch", out var b) == true ? b.GetString() ?? "release" : "release";
            if (meta?.TryGetValue("version", out var v) == true) version = v.GetInt32();
            if (meta?.TryGetValue("id", out var idEl) == true) existingId = idEl.GetString();
        }

        var existingInstances = GetInstalledInstances();
        var idAlreadyExists = !string.IsNullOrEmpty(existingId) &&
            existingInstances.Any(i => i.Id == existingId);

        var newInstanceId = idAlreadyExists || string.IsNullOrEmpty(existingId)
            ? Guid.NewGuid().ToString()
            : existingId;

        var targetPath = CreateInstanceDirectory(branch, newInstanceId);

        if (File.Exists(metaPath) && (idAlreadyExists || string.IsNullOrEmpty(existingId)))
        {
            var metaContent = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(metaPath), ImportJsonOpts);
            if (metaContent != null)
            {
                metaContent["id"] = newInstanceId;
                File.WriteAllText(metaPath, JsonSerializer.Serialize(metaContent, ImportJsonOpts));
                Logger.Info("InstanceService", $"Updated instance ID from '{existingId}' to '{newInstanceId}'");
            }
        }

        foreach (var file in Directory.GetFiles(tempDir))
        {
            var destFile = Path.Combine(targetPath, Path.GetFileName(file));
            File.Move(file, destFile, true);
        }
        foreach (var dir in Directory.GetDirectories(tempDir))
        {
            var destDir = Path.Combine(targetPath, Path.GetFileName(dir));
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
            Directory.Move(dir, destDir);
        }

        try { Directory.Delete(tempDir, true); } catch { /* ignore */ }

        Logger.Success("InstanceService", $"Imported ZIP instance to: {targetPath}");
    }

    /// <summary>
    /// Tries to parse version number from a PWR filename.
    /// Supports patterns: v{version}-{os}-{arch}, 0_to_{version}, {version}, etc.
    /// </summary>
    /// <param name="filename">The filename without extension.</param>
    /// <returns>The parsed version number, or 0 if parsing fails.</returns>
    public static int TryParseVersionFromPwrFilename(string filename)
    {
        // Pattern: v{version}-{os}-{arch} (e.g., v123-linux-x64)
        var versionMatch = System.Text.RegularExpressions.Regex.Match(filename, @"^v(\d+)");
        if (versionMatch.Success && int.TryParse(versionMatch.Groups[1].Value, out var v1))
            return v1;

        // Pattern: 0_to_{version} or {from}_to_{version} (e.g., 0_to_456)
        var patchMatch = System.Text.RegularExpressions.Regex.Match(filename, @"_to_(\d+)");
        if (patchMatch.Success && int.TryParse(patchMatch.Groups[1].Value, out var v2))
            return v2;

        // Pattern: just a number (e.g., 123)
        if (int.TryParse(filename, out var v3))
            return v3;

        // Pattern: number at start (e.g., 123-something)
        var startMatch = System.Text.RegularExpressions.Regex.Match(filename, @"^(\d+)");
        if (startMatch.Success && int.TryParse(startMatch.Groups[1].Value, out var v4))
            return v4;

        return 0; // Unknown version
    }

    #endregion
}

