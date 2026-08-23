// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Infrastructure;
using System.IO.Compression;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HyPrism.Core.Models;

namespace HyPrism.Core.Game.Instances;

/// <summary>
/// Manages game instance paths, versioning, and data organization.
/// Handles instance discovery, creation, and migration from legacy launcher versions
/// </summary>
/// <remarks>
/// Instances are organized in a flat layout: {InstanceRoot}/{instanceId}/.
/// Branch and version information is stored in each instance's meta.json.
/// Legacy layouts (branch subdirectories, version-named folders) are migrated on startup.
/// This service also handles user data directories and cosmetic skins
/// </remarks>
public partial class InstanceRepository : IInstanceRepository
{
    private readonly string _appDir;

    private readonly IConfigStore _configStore;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="InstanceRepository"/> class
    /// </summary>
    /// <param name="appDir">The application data directory path</param>
    /// <param name="configStore">The configuration service for accessing settings</param>
    public InstanceRepository(string appDir, IConfigStore configStore)
    {
        _appDir = appDir;
        _configStore = configStore;
    }

    /// <inheritdoc/>
    public event Action? InstancesChanged;

    private void RaiseInstancesChanged() => InstancesChanged?.Invoke();

    /// <summary>
    /// Gets the current configuration from the config service
    /// </summary>
    /// <returns>The current configuration object</returns>
    private Config GetConfig() => _configStore.Configuration;

    #region Instance cache (instances.json)

    /// <summary>Returns the path to the instance cache file</summary>
    private string GetInstanceCachePath() => Path.Combine(GetInstanceRoot(), "instances.json");

    /// <summary>
    /// Loads the instance list from instances.json.
    /// On first run migrates from the deprecated config.Instances field
    /// </summary>
    private List<InstanceInfo> LoadInstanceCache()
    {
        var path = GetInstanceCachePath();
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<InstanceInfo>>(json, JsonOptions) ?? [];
            }
            catch (Exception ex)
            {
                Logger.Warning("InstanceRepository", $"Failed to read instances.json, rescanning: {ex.Message}");
            }
        }

#pragma warning disable CS0618
        var config = GetConfig();
        if (config.Instances?.Count > 0)
        {
            Logger.Info("InstanceRepository", $"Migrating {config.Instances.Count} instances from config to instances.json");
            SaveInstanceCache(config.Instances);
            config.Instances = null;
            _configStore.SaveConfig();
            return LoadInstanceCache();
        }
#pragma warning restore CS0618

        return [];
    }

    /// <summary>Saves the instance list to instances.json</summary>
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
            Logger.Warning("InstanceRepository", $"Failed to save instances.json: {ex.Message}");
        }
    }

    /// <inheritdoc cref="IInstanceRepository.GetCachedInstances"/>
    public List<InstanceInfo> GetCachedInstances() => LoadInstanceCache();

    /// <inheritdoc/>
    public void SetInstanceOrder(IReadOnlyList<string> instanceIds)
    {
        ArgumentNullException.ThrowIfNull(instanceIds);

        var cached = LoadInstanceCache();
        var byId = cached.ToDictionary(instance => instance.Id, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<InstanceInfo>(cached.Count);
        var addedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var instanceId in instanceIds)
        {
            if (byId.TryGetValue(instanceId, out var instance) && addedIds.Add(instanceId))
                ordered.Add(instance);
        }

        ordered.AddRange(cached.Where(instance => addedIds.Add(instance.Id)));
        SaveInstanceCache(ordered);
        RaiseInstancesChanged();
    }

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
    /// Get the path for a specific branch (release/pre-release)
    /// </summary>
    public string GetBranchPath(string branch)
    {
        string normalizedBranch = NormalizeVersionType(branch);
        return Path.Combine(GetInstanceRoot(), normalizedBranch);
    }

    /// <summary>
    /// Get the UserData path for a specific instance version
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
    /// Checks multiple locations including legacy naming formats and GUID-named folders
    /// </summary>
    public string? FindExistingInstancePath(string branch, int version)
    {
        string normalizedBranch = NormalizeVersionType(branch);
        string versionSegment = version == 0 ? "latest" : version.ToString();

        var flatRoot = GetInstanceRoot();
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
                if (meta == null) continue;
                if (!meta.Branch.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase)) continue;

                if (version == 0 && meta.IsLatest) return instanceDir;
                if (version > 0 && meta.Version == version) return instanceDir;
            }
        }

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
    /// Get all instance roots including legacy locations
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
            foreach (var r in YieldIfExists(Path.Combine(legacy, "instance")))
            {
                yield return r;
            }

            foreach (var r in YieldIfExists(Path.Combine(legacy, "instances")))
            {
                yield return r;
            }
        }

        var oldInstanceDir = Path.Combine(_appDir, "instance");
        foreach (var r in YieldIfExists(oldInstanceDir))
        {
            yield return r;
        }
    }

    /// <summary>
    /// Get path for latest instance symlink/info
    /// </summary>
    public string GetLatestInstancePath(string branch)
    {
        return Path.Combine(GetBranchPath(branch), "latest");
    }

    /// <summary>
    /// Get path for latest.json file (legacy, used for migration only)
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
    /// Falls back to legacy latest.json for migration
    /// </summary>
    public LatestInstanceInfo? LoadLatestInfo(string branch)
    {
        try
        {
            var normalizedBranch = NormalizeVersionType(branch);

            var latestPath = GetLatestInstancePath(normalizedBranch);
            if (Directory.Exists(latestPath))
            {
                var meta = GetInstanceMeta(latestPath);
                if (meta != null && meta.InstalledVersion > 0)
                {
                    return new LatestInstanceInfo { Version = meta.InstalledVersion, UpdatedAt = meta.LastPlayedAt ?? meta.CreatedAt };
                }
            }

            var path = GetLatestInfoPath(normalizedBranch);
            if (!File.Exists(path))
            {
                path = GetLegacyLatestInfoPath(normalizedBranch);
                if (!File.Exists(path)) return null;
            }
            var json = File.ReadAllText(path);
            var info = JsonSerializer.Deserialize<LatestInstanceInfo>(json, JsonOptions);

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
    /// No longer creates latest.json files
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
    /// Safely copy directory recursively, preventing infinite loops
    /// </summary>
    public static void SafeCopyDirectory(string sourceDir, string destDir)
    {
        LauncherUtilities.CopyDirectory(sourceDir, destDir, false);
    }

    /// <summary>
    /// Normalize version type: "prerelease" or "pre-release" -> "pre-release"
    /// </summary>
    public static string NormalizeVersionType(string versionType)
    {
        return LauncherUtilities.NormalizeVersionType(versionType);
    }

    /// <summary>
    /// Checks if the game client executable exists at the specified version path.
    /// Tries multiple layouts: new layout (Client/...) and legacy layout (game/Client/...)
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
    /// Checks if game assets are present at the specified version path
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
    /// If not found, returns a path for a new instance (but does not create it)
    /// </summary>
    public string GetInstancePath(string branch, int version)
    {
        if (version == 0)
        {
            return GetLatestInstancePath(branch);
        }

        string normalizedBranch = NormalizeVersionType(branch);
        var flatRoot = GetInstanceRoot();

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

        var branchPath = Path.Combine(flatRoot, normalizedBranch);
        if (Directory.Exists(branchPath))
        {
            foreach (var instanceDir in Directory.GetDirectories(branchPath))
            {
                var folderName = Path.GetFileName(instanceDir);

                if (folderName.Equals("latest", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (folderName == version.ToString())
                    return instanceDir;

                var meta = GetInstanceMeta(instanceDir);
                if (meta != null && meta.Version == version &&
                    meta.Branch.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase))
                    return instanceDir;
            }
        }

        return Path.Combine(flatRoot, version.ToString());
    }

    /// <summary>
    /// Resolves the instance path, optionally preferring existing legacy paths
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
    /// Gets the list of legacy installation root directories to search for migrations
    /// </summary>
    private static List<string> GetLegacyRoots()
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
            Add(Path.Combine(appData, "HyPrism"));
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
    /// Also removes latest.json for latest instances (version 0)
    /// </summary>
    public bool DeleteGame(string branch, int versionNumber)
    {
        try
        {
            string normalizedBranch = LauncherUtilities.NormalizeVersionType(branch);
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

            SyncInstancesWithConfig();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Error deleting game: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Deletes a game instance by unique ID
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

            SyncInstancesWithConfig();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Game", $"Error deleting game by id: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Scan for all installed instances in the standard hierarchy
    /// </summary>
    public List<InstalledInstance> GetInstalledInstances()
    {
        var results = new List<InstalledInstance>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = GetInstanceRoot();

        if (!Directory.Exists(root)) return results;

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
                    Logger.Warning("InstanceRepository", $"GUID folder without meta.json: {folder}");
                    return;
                }
                else
                {
                    return;
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

            if (string.IsNullOrEmpty(instanceId))
            {
                if (!IsClientPresent(folder))
                {
                    Logger.Debug("InstanceRepository", $"Skipping non-installed placeholder folder: {folder}");
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
                    Logger.Debug("InstanceRepository", $"Generated and persisted ID for {branch}/{version}: {instanceId}");
                }
                catch (Exception ex)
                {
                    Logger.Warning("InstanceRepository", $"Failed to persist generated ID: {ex.Message}");
                }
            }

            if (!string.IsNullOrEmpty(instanceId) && !seenIds.Add(instanceId))
            {
                Logger.Debug("InstanceRepository", $"Skipping duplicate instance {instanceId} at {folder}");
                return;
            }

            var (Status, Details) = ValidateGameIntegrity(folder);

            results.Add(new InstalledInstance
            {
                Id = instanceId,
                Branch = branch,
                Version = version,
                Path = folder,
                HasUserData = hasUserData,
                UserDataSize = size,
                TotalSize = totalSize,
                IsValid = Status == InstanceValidationStatus.Valid,
                ValidationStatus = Status,
                ValidationDetails = Details,
                CustomName = customName
            });
        }

        foreach (var folder in Directory.GetDirectories(root))
        {
            var dirName = Path.GetFileName(folder);
            if (dirName.Equals("release", StringComparison.OrdinalIgnoreCase) ||
                dirName.Equals("pre-release", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!Guid.TryParse(dirName, out _))
                continue;
            ProcessFolder(folder, null);
        }

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
                Logger.Error("InstanceRepository", $"Error scanning branch {branch}: {ex.Message}");
            }
        }

        return [.. results.OrderByDescending(x => x.Version)];
    }

    /// <summary>
    /// Performs deep validation of a game instance, checking all critical components.
    /// Returns detailed information about what's present and what's missing
    /// </summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Public instance API is retained for source compatibility")]
    public (InstanceValidationStatus Status, InstanceValidationDetails Details) ValidateGameIntegrity(string folder)
    {
        var details = new InstanceValidationDetails();
        var missingComponents = new List<string>();

        try
        {
            if (!Directory.Exists(folder))
            {
                details.ErrorMessage = "Instance directory does not exist";
                return (InstanceValidationStatus.NotInstalled, details);
            }

            details.HasExecutable = CheckExecutablePresent(folder);
            if (!details.HasExecutable)
            {
                missingComponents.Add("Game executable");
            }

            details.HasAssets = CheckAssetsPresent(folder);
            if (!details.HasAssets)
            {
                missingComponents.Add("Game assets");
            }

            details.HasLibraries = CheckLibrariesPresent(folder);
            if (!details.HasLibraries)
            {
                missingComponents.Add("Game libraries");
            }

            details.HasConfig = CheckConfigPresent(folder);
            if (!details.HasConfig)
            {
            }

            details.MissingComponents = missingComponents;

            if (details.HasExecutable)
            {
                return (InstanceValidationStatus.Valid, details);
            }
            else if (!details.HasExecutable && !details.HasAssets && !details.HasLibraries)
            {
                return (InstanceValidationStatus.NotInstalled, details);
            }
            else
            {
                details.ErrorMessage = "Game executable is missing or corrupted";
                return (InstanceValidationStatus.Corrupted, details);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("InstanceRepository", $"Error validating instance {folder}: {ex.Message}");
            details.ErrorMessage = ex.Message;
            return (InstanceValidationStatus.Unknown, details);
        }
    }

    /// <summary>
    /// Checks if the game executable is present at the specified path
    /// </summary>
    private static bool CheckExecutablePresent(string folder)
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
    /// Checks if game assets are present and contain actual files
    /// </summary>
    private static bool CheckAssetsPresent(string folder)
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

        try
        {
            var entries = Directory.GetFileSystemEntries(assetsPath);
            if (entries.Length == 0)
            {
                return false;
            }

            return entries.Length >= 3;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if required libraries/dependencies are present
    /// </summary>
    private static bool CheckLibrariesPresent(string folder)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var frameworksPath = Path.Combine(folder, "Client", "Hytale.app", "Contents", "Frameworks");
            if (Directory.Exists(frameworksPath))
            {
                return Directory.EnumerateFileSystemEntries(frameworksPath).Any();
            }
            var monoPath = Path.Combine(folder, "Client", "Hytale.app", "Contents", "MonoBleedingEdge");
            return Directory.Exists(monoPath) && Directory.EnumerateFileSystemEntries(monoPath).Any();
        }

        var clientFolder = Path.Combine(folder, "Client");
        if (!Directory.Exists(clientFolder))
        {
            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var monoPath = Path.Combine(clientFolder, "MonoBleedingEdge");
            var hasMono = Directory.Exists(monoPath);
            var hasDlls = Directory.EnumerateFiles(clientFolder, "*.dll", SearchOption.TopDirectoryOnly).Any();
            return hasMono || hasDlls;
        }
        else
        {
            var monoPath = Path.Combine(clientFolder, "MonoBleedingEdge");
            var hasMono = Directory.Exists(monoPath);
            var hasSo = Directory.EnumerateFiles(clientFolder, "*.so*", SearchOption.TopDirectoryOnly).Any();
            return hasMono || hasSo;
        }
    }

    /// <summary>
    /// Checks if essential config files are present
    /// </summary>
    private static bool CheckConfigPresent(string folder)
    {
        var configFiles = new[]
        {
            Path.Combine(folder, "Client", "boot.config"),
            Path.Combine(folder, "Client", "globalgamemanagers"),
            Path.Combine(folder, "Client", "level0"),
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var dataPath = Path.Combine(folder, "Client", "Hytale.app", "Contents", "Data");
            return Directory.Exists(dataPath);
        }

        return configFiles.Any(File.Exists) ||
               Directory.Exists(Path.Combine(folder, "Client", "HytaleClient_Data"));
    }

    private static bool CheckInstanceValidity(string folder)
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

    /// <inheritdoc/>
    public void SetInstanceCustomName(string branch, int version, string? customName)
    {
        var instancePath = GetInstancePath(branch, version);

        if (string.IsNullOrEmpty(instancePath) || !Directory.Exists(instancePath))
        {
            Logger.Warning("InstanceRepository", $"Instance not found: {branch}/{version}");
            return;
        }

        SetInstanceNameInternal(instancePath, customName, $"{branch}/{version}");
    }

    /// <inheritdoc/>
    public void SetInstanceCustomNameById(string instanceId, string? customName)
    {
        var instancePath = GetInstancePathById(instanceId);

        if (string.IsNullOrEmpty(instancePath) || !Directory.Exists(instancePath))
        {
            Logger.Warning("InstanceRepository", $"Instance not found by ID: {instanceId}");
            return;
        }

        SetInstanceNameInternal(instancePath, customName, instanceId);
    }

    private void SetInstanceNameInternal(string instancePath, string? customName, string logIdentifier)
    {
        try
        {
            var meta = GetInstanceMeta(instancePath);
            if (meta == null)
            {
                Logger.Warning("InstanceRepository", $"No meta.json found for instance: {logIdentifier}");
                return;
            }

            meta.Name = string.IsNullOrWhiteSpace(customName)
                ? (meta.IsLatest ? $"{meta.Branch} (Latest)" : $"{meta.Branch} v{meta.Version}")
                : customName;

            SaveInstanceMeta(instancePath, meta);

            SyncInstancesWithConfig();

            Logger.Info("InstanceRepository", $"Updated instance name for {logIdentifier}: {meta.Name}");
        }
        catch (Exception ex)
        {
            Logger.Error("InstanceRepository", $"Failed to save instance name: {ex.Message}");
        }
    }

    #region Instance Meta Management

    /// <inheritdoc/>
    public InstanceMeta? GetInstanceMeta(string instancePath)
    {
        var metaPath = Path.Combine(instancePath, "meta.json");
        if (!File.Exists(metaPath))
        {
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
            Logger.Warning("InstanceRepository", $"Failed to load meta.json: {ex.Message}");
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
            Logger.Debug("InstanceRepository", $"Saved meta.json for instance {meta.Id}");
        }
        catch (Exception ex)
        {
            Logger.Error("InstanceRepository", $"Failed to save meta.json: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public InstanceMeta CreateInstanceMeta(string branch, int version, string? name = null, bool isLatest = false)
    {
        var normalizedBranch = NormalizeVersionType(branch);

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
                        Logger.Debug("InstanceRepository", $"Latest instance already exists for {branch}");
                        return existingMeta;
                    }
                }
            }
        }

        string instancePath;
        string instanceId = Guid.NewGuid().ToString();
        instancePath = CreateInstanceDirectory(normalizedBranch, instanceId);

        var pathMeta = GetInstanceMeta(instancePath);
        if (pathMeta != null)
        {
            Logger.Debug("InstanceRepository", $"Instance meta already exists at path {instancePath}");
            return pathMeta;
        }

        var meta = new InstanceMeta
        {
            Id = instanceId,
            Name = name ?? (isLatest ? $"{normalizedBranch} (Latest)" : $"{normalizedBranch} v{version}"),
            Branch = normalizedBranch,
            Version = version,
            CreatedAt = DateTime.UtcNow,
            IsLatest = isLatest
        };

        SaveInstanceMeta(instancePath, meta);

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
            RaiseInstancesChanged();
        }

        Logger.Info("InstanceRepository", $"Created instance meta: {meta.Id} ({meta.Name})");
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
            Logger.Warning("InstanceRepository", $"SetSelectedInstance ignored: instance not found ({instanceId})");
            return;
        }

        config.SelectedInstanceId = instanceId;

        // Keep legacy launch config in sync with selected instance so launch paths
        // that still read VersionType/SelectedVersion target the same instance.
#pragma warning disable CS0618 // Backward compatibility: VersionType and SelectedVersion kept for migration
        config.VersionType = NormalizeVersionType(selected.Branch);
        config.SelectedVersion = selected.Version;
#pragma warning restore CS0618

        _configStore.SaveConfig();
        Logger.Info("InstanceRepository", $"Selected instance: {instanceId} ({selected.Branch} v{selected.Version})");
        RaiseInstancesChanged();
    }

    /// <inheritdoc/>
    public InstanceInfo? FindInstanceById(string instanceId)
    {
        var info = LoadInstanceCache().FirstOrDefault(i => i.Id == instanceId);
        if (info != null)
            return info;

        SyncInstancesWithConfig();
        return LoadInstanceCache().FirstOrDefault(i => i.Id == instanceId);
    }

    /// <inheritdoc/>
    public void SyncInstancesWithConfig()
    {
        var config = GetConfig();
        var cachedOrder = LoadInstanceCache()
            .Select((instance, index) => (instance.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.OrdinalIgnoreCase);
        var discoveredById = new Dictionary<string, InstanceInfo>(StringComparer.OrdinalIgnoreCase);

        void ProcessInstanceDir(string instanceDir)
        {
            var meta = GetInstanceMeta(instanceDir);
            if (meta == null) return;

            if (string.IsNullOrWhiteSpace(meta.Id))
            {
                meta.Id = Guid.NewGuid().ToString();
                SaveInstanceMeta(instanceDir, meta);
                Logger.Warning("InstanceRepository", $"Recovered empty instance ID at {instanceDir}: generated {meta.Id}");
            }

            if (discoveredById.ContainsKey(meta.Id))
            {
                Logger.Warning("InstanceRepository", $"Duplicate instance ID detected during sync: {meta.Id}. Keeping first entry and skipping {instanceDir}");
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

            foreach (var instanceDir in Directory.GetDirectories(root))
            {
                var dirName = Path.GetFileName(instanceDir);
                if (dirName.Equals("release", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("pre-release", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!Guid.TryParse(dirName, out _))
                    continue;
                ProcessInstanceDir(instanceDir);
            }

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
            .OrderBy(instance => cachedOrder.TryGetValue(instance.Id, out var index) ? index : int.MaxValue)
            .ThenBy(instance => instance.Branch)
            .ThenByDescending(instance => instance.Version)
            .ToList();

        SaveInstanceCache(synced);
        Logger.Debug("InstanceRepository", $"Synced {synced.Count} instances with config");
        RaiseInstancesChanged();
    }

    /// <summary>
    /// Migrates legacy metadata.json to new meta.json format
    /// </summary>
    private InstanceMeta? MigrateLegacyMetadata(string instancePath, string legacyPath)
    {
        try
        {
            var json = File.ReadAllText(legacyPath);
            var legacyData = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);

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

            SaveInstanceMeta(instancePath, meta);

            try { File.Delete(legacyPath); } catch { }

            Logger.Info("InstanceRepository", $"Migrated legacy metadata to meta.json: {meta.Id}");
            return meta;
        }
        catch (Exception ex)
        {
            Logger.Warning("InstanceRepository", $"Failed to migrate legacy metadata: {ex.Message}");
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

        var flatPath = Path.Combine(root, instanceId);
        if (Directory.Exists(flatPath))
            return flatPath;

        foreach (var branchDir in Directory.GetDirectories(root))
        {
            var branchName = Path.GetFileName(branchDir);
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

#pragma warning disable CS0618
        var legacyInfo = config.Instances?.FirstOrDefault(i =>
            i.Branch.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase) && i.Version == version);
#pragma warning restore CS0618
        if (legacyInfo != null)
            return legacyInfo;

        var cached = LoadInstanceCache();
        var cachedMatch = cached.FirstOrDefault(i =>
            i.Branch.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase) && i.Version == version);
        if (cachedMatch != null)
            return cachedMatch;

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
        var path = Path.Combine(GetInstanceRoot(), instanceId);
        Directory.CreateDirectory(path);
        return path;
    }


    /// <summary>
    /// Changes the version/branch of an existing instance.
    /// For upgrades within the same branch: preserves game files and sets up for patching.
    /// For downgrades or branch changes: removes game client files and prepares for fresh download.
    /// Always keeps UserData and meta.json, and marks IsLatest = false
    /// </summary>
    public bool ChangeInstanceVersion(string instanceId, string branch, int version)
    {
        try
        {
            var instancePath = GetInstancePathById(instanceId);
            if (string.IsNullOrEmpty(instancePath) || !Directory.Exists(instancePath))
            {
                Logger.Warning("InstanceRepository", $"ChangeInstanceVersion: instance path not found for {instanceId}");
                return false;
            }

            var meta = GetInstanceMeta(instancePath);
            if (meta == null)
            {
                Logger.Warning("InstanceRepository", $"ChangeInstanceVersion: meta.json not found for {instanceId}");
                return false;
            }

            var normalizedBranch = LauncherUtilities.NormalizeVersionType(branch);
            var currentBranch = LauncherUtilities.NormalizeVersionType(meta.Branch);
            var currentInstalledVersion = meta.InstalledVersion;
            var hasInstalledGame = currentInstalledVersion > 0 && IsClientPresent(instancePath);

            bool canUsePatch = hasInstalledGame
                && currentBranch == normalizedBranch
                && version > currentInstalledVersion;

            if (canUsePatch)
            {
                Logger.Info("InstanceRepository", $"Patch mode: upgrading {instanceId} from v{currentInstalledVersion} to v{version}");

                meta.Branch = normalizedBranch;
                meta.Version = version;
                meta.PendingVersion = version;
                // Keep InstalledVersion as-is so patcher knows the starting point
                meta.IsLatest = false;

                SaveInstanceMeta(instancePath, meta);
            }
            else
            {
                Logger.Info("InstanceRepository", $"Full download mode: {instanceId} from {currentBranch} v{currentInstalledVersion} to {normalizedBranch} v{version}");

                var clientDir = Path.Combine(instancePath, "Client");
                var gameDir = Path.Combine(instancePath, "game");

                if (Directory.Exists(clientDir))
                {
                    Directory.Delete(clientDir, true);
                    Logger.Info("InstanceRepository", $"Removed Client directory for {instanceId}");
                }

                if (Directory.Exists(gameDir))
                {
                    Directory.Delete(gameDir, true);
                    Logger.Info("InstanceRepository", $"Removed game directory for {instanceId}");
                }

                meta.Branch = normalizedBranch;
                meta.Version = version;
                meta.InstalledVersion = 0;
                meta.PendingVersion = 0;
                meta.IsLatest = false;

                SaveInstanceMeta(instancePath, meta);
            }

            var cachedInstances = LoadInstanceCache();
            var cachedEntry = cachedInstances.FirstOrDefault(i => i.Id == instanceId);
            if (cachedEntry != null)
            {
                cachedEntry.Branch = normalizedBranch;
                cachedEntry.Version = version;
                SaveInstanceCache(cachedInstances);
            }

            var mode = canUsePatch ? "patch" : "full-download";
            Logger.Success("InstanceRepository", $"Changed instance {instanceId} to {normalizedBranch} v{version} (non-latest, mode={mode})");
            RaiseInstancesChanged();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("InstanceRepository", $"Failed to change instance version: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region ZIP Import

    private static readonly JsonSerializerOptions ImportJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <inheritdoc/>
    public async Task ImportFromZipAsync(
        string zipPath,
        CancellationToken cancellationToken = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hyprism-import-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        await ZipFile.ExtractToDirectoryAsync(
            zipPath,
            tempDir,
            overwriteFiles: true,
            cancellationToken);

        var metaPath = Path.Combine(tempDir, "meta.json");
        var branch = "release";
        var version = 0;
        string? existingId = null;

        if (File.Exists(metaPath))
        {
            var metaJson = await File.ReadAllTextAsync(metaPath, cancellationToken);
            var meta = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(metaJson, ImportJsonOpts);
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
            var metaJson = await File.ReadAllTextAsync(metaPath, cancellationToken);
            var metaContent = JsonSerializer.Deserialize<Dictionary<string, object>>(metaJson, ImportJsonOpts);
            if (metaContent != null)
            {
                metaContent["id"] = newInstanceId;
                await File.WriteAllTextAsync(
                    metaPath,
                    JsonSerializer.Serialize(metaContent, ImportJsonOpts),
                    cancellationToken);
                Logger.Info("InstanceRepository", $"Updated instance ID from '{existingId}' to '{newInstanceId}'");
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

        try { Directory.Delete(tempDir, true); } catch { }

        Logger.Success("InstanceRepository", $"Imported ZIP instance to: {targetPath}");
        SyncInstancesWithConfig();
    }

    /// <summary>
    /// Tries to parse version number from a PWR filename.
    /// Supports patterns: v{version}-{os}-{arch}, 0_to_{version}, {version}, etc
    /// </summary>
    /// <param name="filename">The filename without extension</param>
    /// <returns>The parsed version number, or 0 if parsing fails</returns>
    public static int TryParseVersionFromPwrFilename(string filename)
    {
        var versionMatch = PrefixedVersionRegex().Match(filename);
        if (versionMatch.Success && int.TryParse(versionMatch.Groups[1].Value, out var v1))
            return v1;

        var patchMatch = PatchTargetVersionRegex().Match(filename);
        if (patchMatch.Success && int.TryParse(patchMatch.Groups[1].Value, out var v2))
            return v2;

        if (int.TryParse(filename, out var v3))
            return v3;

        var startMatch = LeadingVersionRegex().Match(filename);
        if (startMatch.Success && int.TryParse(startMatch.Groups[1].Value, out var v4))
            return v4;

        return 0;
    }

    [GeneratedRegex(@"^v(\d+)")]
    private static partial Regex PrefixedVersionRegex();

    [GeneratedRegex(@"_to_(\d+)")]
    private static partial Regex PatchTargetVersionRegex();

    [GeneratedRegex(@"^(\d+)")]
    private static partial Regex LeadingVersionRegex();

    #endregion
}
