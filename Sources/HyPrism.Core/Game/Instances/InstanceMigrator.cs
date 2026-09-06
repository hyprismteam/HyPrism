// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;
using System.Text.Json;
using HyPrism.Core.Models;
using HyPrism.Core.Infrastructure;

namespace HyPrism.Core.Game.Instances;

/// <summary>
/// Handles one-time and on-startup migrations of legacy instance folder structures
/// and legacy configuration data to the current format
/// </summary>
/// <remarks>
/// Extracted from <see cref="InstanceRepository"/> to keep the instance service focused on
/// path resolution and instance CRUD. All methods are safe to call even when the
/// relevant legacy artefacts no longer exist, in which case they become no-ops
/// </remarks>
public class InstanceMigrator : IInstanceMigrator
{
    private readonly string _appDir;
    private readonly IConfigStore _configStore;
    private readonly IInstanceRepository _instances;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstanceMigrator"/> class
    /// </summary>
    /// <param name="appPath">The application path configuration</param>
    /// <param name="configStore">The configuration service</param>
    /// <param name="instances">The instance service for path resolution and meta operations</param>
    public InstanceMigrator(
        AppPathConfiguration appPath,
        IConfigStore configStore,
        IInstanceRepository instances)
    {
        _appDir = appPath.AppDir;
        _configStore = configStore;
        _instances = instances;
    }

    /// <inheritdoc/>
    public void MigrateLegacyData()
    {
        try
        {
            var config = _configStore.Configuration;

            foreach (var legacyRoot in GetLegacyRoots())
            {
                if (!Directory.Exists(legacyRoot)) continue;

                Logger.Info("Migrate", $"Found legacy data at {legacyRoot}");

                var legacyConfigPath = Path.Combine(legacyRoot, "config.json");
                var legacyTomlPath = Path.Combine(legacyRoot, "config.toml");

                var jsonConfig = LoadConfigFromPath(legacyConfigPath);
                var tomlConfig = LoadConfigFromToml(legacyTomlPath);

                var legacyConfig = tomlConfig ?? jsonConfig;
                if (legacyConfig is not null)
                {
                    Logger.Info("Migrate", "Using legacy config for instance settings only");
                }
                else
                {
                    Logger.Warning("Migrate", $"No valid config found in {legacyRoot}");
                }

                var updated = false;

                if (legacyConfig != null)
                {
                    if (string.IsNullOrWhiteSpace(config.InstanceDirectory) && !string.IsNullOrWhiteSpace(legacyConfig.InstanceDirectory))
                    {
                        config.InstanceDirectory = legacyConfig.InstanceDirectory;
                        updated = true;
                    }

#pragma warning disable CS0618 // Legacy migration: reading old config values
                    if (config.SelectedVersion == 0 && legacyConfig.SelectedVersion > 0)
                    {
                        config.SelectedVersion = legacyConfig.SelectedVersion;
                        updated = true;
                    }

                    if (string.IsNullOrWhiteSpace(config.VersionType) && !string.IsNullOrWhiteSpace(legacyConfig.VersionType))
                    {
                        config.VersionType = LauncherUtilities.NormalizeVersionType(legacyConfig.VersionType);
                        updated = true;
                    }
#pragma warning restore CS0618
                }

                if (updated)
                {
                    _configStore.SaveConfig();

                    if (File.Exists(legacyTomlPath))
                    {
                        try
                        {
                            File.Delete(legacyTomlPath);
                            Logger.Success("Migrate", $"Deleted legacy config.toml at {legacyTomlPath}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning("Migrate", $"Failed to delete legacy config.toml: {ex.Message}");
                        }
                    }
                }

                var legacyInstanceRoot = Path.Combine(legacyRoot, "instance");
                var legacyInstancesRoot = Path.Combine(legacyRoot, "instances");
                if (!Directory.Exists(legacyInstanceRoot) && Directory.Exists(legacyInstancesRoot))
                {
                    legacyInstanceRoot = legacyInstancesRoot;
                }

                if (Directory.Exists(legacyInstanceRoot))
                {
                    Logger.Info("Migrate", $"Legacy instances detected at {legacyInstanceRoot}");
                    MigrateLegacyInstances(legacyInstanceRoot);
                }
            }

            var oldInstanceDir = Path.Combine(_appDir, "instance");
            if (Directory.Exists(oldInstanceDir))
            {
                Logger.Info("Migrate", $"Old 'instance' folder detected at {oldInstanceDir}");
                MigrateLegacyInstances(oldInstanceDir);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Migrate", $"Legacy migration skipped: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void MigrateLegacyInstances(string legacyInstanceRoot)
    {
        try
        {
            var newInstanceRoot = _instances.GetInstanceRoot();

            var normalizedSource = Path.GetFullPath(legacyInstanceRoot).TrimEnd(Path.DirectorySeparatorChar);
            var normalizedDest = Path.GetFullPath(newInstanceRoot).TrimEnd(Path.DirectorySeparatorChar);
            var isSameDirectory = normalizedSource.Equals(normalizedDest, StringComparison.OrdinalIgnoreCase);

            if (isSameDirectory)
            {
                Logger.Info("Migrate", "Source equals destination - will restructure legacy folders in-place");
                RestructureLegacyFoldersInPlace(legacyInstanceRoot);
                return;
            }

            if (normalizedSource.StartsWith(normalizedDest + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info("Migrate", "Skipping migration - source is inside destination");
                return;
            }

            Logger.Info("Migrate", $"Copying legacy instances from {legacyInstanceRoot} to {newInstanceRoot}");

            foreach (var legacyDir in Directory.GetDirectories(legacyInstanceRoot))
            {
                var folderName = Path.GetFileName(legacyDir);
                if (string.IsNullOrEmpty(folderName)) continue;

                var normalizedFolderName = folderName.ToLowerInvariant();
                if (normalizedFolderName == "release" || normalizedFolderName == "pre-release" ||
                    normalizedFolderName == "prerelease" || normalizedFolderName == "latest")
                {
                    Logger.Info("Migrate", $"Skipping {folderName} - already in new structure format");
                    continue;
                }

                string branch;
                string versionSegment;

                if (folderName.Contains('/'))
                {
                    var parts = folderName.Split('/');
                    branch = parts[0];
                    versionSegment = parts.Length > 1 ? parts[1] : "latest";
                }
                else if (folderName.Contains('-'))
                {
                    var parts = folderName.Split('-', 2);
                    branch = parts[0];
                    versionSegment = parts.Length > 1 ? parts[1] : "latest";

                    if (versionSegment.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                    {
                        versionSegment = versionSegment[1..];
                    }
                }
                else
                {
                    Logger.Info("Migrate", $"Skipping {folderName} - unknown format, may be new structure");
                    continue;
                }

                branch = LauncherUtilities.NormalizeVersionType(branch);

                var targetBranch = Path.Combine(newInstanceRoot, branch);
                var targetVersion = Path.Combine(targetBranch, versionSegment);

                var normalizedLegacy = Path.GetFullPath(legacyDir).TrimEnd(Path.DirectorySeparatorChar);
                var normalizedTarget = Path.GetFullPath(targetVersion).TrimEnd(Path.DirectorySeparatorChar);
                if (normalizedLegacy.Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase) ||
                    normalizedTarget.StartsWith(normalizedLegacy + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    normalizedLegacy.StartsWith(normalizedTarget + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Info("Migrate", $"Skipping {folderName} - would cause recursive copy");
                    continue;
                }

                if (Directory.Exists(targetVersion) && _instances.IsClientPresent(targetVersion))
                {
                    Logger.Info("Migrate", $"Skipping {folderName} - already exists at {targetVersion}");
                    continue;
                }

                Logger.Info("Migrate", $"Copying {folderName} -> {branch}/{versionSegment}");
                Directory.CreateDirectory(targetVersion);

                var legacyGameDir = Path.Combine(legacyDir, "game");
                var legacyClientDir = Path.Combine(legacyDir, "Client");

                if (Directory.Exists(legacyGameDir))
                {
                    foreach (var item in Directory.GetFileSystemEntries(legacyGameDir))
                    {
                        var name = Path.GetFileName(item);
                        var dest = Path.Combine(targetVersion, name);

                        if (Directory.Exists(item))
                        {
                            LauncherUtilities.CopyDirectory(item, dest, false);
                        }
                        else if (File.Exists(item))
                        {
                            File.Copy(item, dest, overwrite: false);
                        }
                    }
                    Logger.Success("Migrate", $"Migrated {folderName} (from game/ subfolder)");
                }
                else if (Directory.Exists(legacyClientDir))
                {
                    foreach (var item in Directory.GetFileSystemEntries(legacyDir))
                    {
                        var name = Path.GetFileName(item);
                        var dest = Path.Combine(targetVersion, name);

                        if (Directory.Exists(item))
                        {
                            LauncherUtilities.CopyDirectory(item, dest, false);
                        }
                        else if (File.Exists(item))
                        {
                            File.Copy(item, dest, overwrite: false);
                        }
                    }
                    Logger.Success("Migrate", $"Migrated {folderName} (direct structure)");
                }
                else
                {
                    LauncherUtilities.CopyDirectory(legacyDir, targetVersion, false);
                    Logger.Success("Migrate", $"Migrated {folderName} (full copy)");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Migrate", $"Failed to migrate legacy instances: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void RestructureLegacyFoldersInPlace(string instanceRoot)
    {
        try
        {
            foreach (var legacyDir in Directory.GetDirectories(instanceRoot))
            {
                var folderName = Path.GetFileName(legacyDir);
                if (string.IsNullOrEmpty(folderName)) continue;

                var normalizedFolderName = folderName.ToLowerInvariant();
                if (normalizedFolderName == "release" || normalizedFolderName == "pre-release" ||
                    normalizedFolderName == "prerelease" || normalizedFolderName == "latest")
                {
                    continue;
                }

                if (!folderName.Contains('-'))
                {
                    continue;
                }

                var parts = folderName.Split('-', 2);
                var branch = parts[0];
                var versionSegment = parts.Length > 1 ? parts[1] : "latest";

                if (versionSegment.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                {
                    versionSegment = versionSegment[1..];
                }

                branch = LauncherUtilities.NormalizeVersionType(branch);

                var targetBranch = Path.Combine(instanceRoot, branch);
                var targetVersion = Path.Combine(targetBranch, versionSegment);

                if (Directory.Exists(targetVersion))
                {
                    Logger.Info("Migrate", $"Skipping {folderName} - target {branch}/{versionSegment} already exists");
                    continue;
                }

                Logger.Info("Migrate", $"Restructuring {folderName} -> {branch}/{versionSegment}");

                Directory.CreateDirectory(targetBranch);

                var legacyGameDir = Path.Combine(legacyDir, "game");

                if (Directory.Exists(legacyGameDir))
                {
                    Directory.CreateDirectory(targetVersion);

                    foreach (var item in Directory.GetFileSystemEntries(legacyGameDir))
                    {
                        var name = Path.GetFileName(item);
                        var dest = Path.Combine(targetVersion, name);

                        if (Directory.Exists(item))
                        {
                            Directory.Move(item, dest);
                        }
                        else if (File.Exists(item))
                        {
                            File.Move(item, dest);
                        }
                    }

                    try
                    {
                        Directory.Delete(legacyDir, recursive: true);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning("Migrate", $"Could not delete old folder {legacyDir}: {ex.Message}");
                    }

                    Logger.Success("Migrate", $"Restructured {folderName} (from game/ subfolder)");
                }
                else
                {
                    try
                    {
                        Directory.Move(legacyDir, targetVersion);
                        Logger.Success("Migrate", $"Restructured {folderName} (direct rename)");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Migrate", $"Failed to rename {folderName}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Migrate", $"Failed to restructure legacy folders: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void MigrateVersionFoldersToIdFolders()
    {
        try
        {
            Logger.Info("Migrate", "Starting version-to-ID folder migration...");
            var root = _instances.GetInstanceRoot();
            if (!Directory.Exists(root))
            {
                Logger.Info("Migrate", "No instance root directory found, skipping migration");
                return;
            }

            int migratedCount = 0;

            foreach (var branchDir in Directory.GetDirectories(root))
            {
                var branchName = Path.GetFileName(branchDir);
                if (!branchName.Equals("release", StringComparison.OrdinalIgnoreCase) &&
                    !branchName.Equals("pre-release", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var instanceDir in Directory.GetDirectories(branchDir))
                {
                    var folderName = Path.GetFileName(instanceDir);

                    if (Guid.TryParse(folderName, out _))
                    {
                        continue;
                    }

                    if (folderName.Equals("latest", StringComparison.OrdinalIgnoreCase))
                    {
                        var latestMeta = _instances.GetInstanceMeta(instanceDir);
                        string latestId;

                        if (latestMeta != null && !string.IsNullOrEmpty(latestMeta.Id))
                        {
                            latestId = latestMeta.Id;
                            if (!latestMeta.IsLatest)
                            {
                                latestMeta.IsLatest = true;
                                latestMeta.Version = 0;
                                if (string.IsNullOrEmpty(latestMeta.Name))
                                    latestMeta.Name = $"{branchName} (Latest)";
                                _instances.SaveInstanceMeta(instanceDir, latestMeta);
                            }
                        }
                        else
                        {
                            latestId = Guid.NewGuid().ToString();
                            var newLatestMeta = new InstanceMeta
                            {
                                Id = latestId,
                                Name = $"{branchName} (Latest)",
                                Branch = branchName,
                                Version = 0,
                                CreatedAt = DateTime.UtcNow,
                                IsLatest = true
                            };
                            _instances.SaveInstanceMeta(instanceDir, newLatestMeta);
                            Logger.Info("Migrate", $"Created meta.json for latest instance in {branchName}");
                        }

                        var newLatestPath = Path.Combine(branchDir, latestId);
                        if (!Directory.Exists(newLatestPath))
                        {
                            try
                            {
                                Directory.Move(instanceDir, newLatestPath);
                                Logger.Success("Migrate", $"Migrated {branchName}/latest -> {branchName}/{latestId}");
                                migratedCount++;
                            }
                            catch (Exception ex)
                            {
                                Logger.Error("Migrate", $"Failed to rename latest folder: {ex.Message}");
                            }
                        }
                        continue;
                    }

                    if (!int.TryParse(folderName, out var version))
                    {
                        continue;
                    }

                    var meta = _instances.GetInstanceMeta(instanceDir);
                    string instanceId;

                    if (meta != null && !string.IsNullOrEmpty(meta.Id))
                    {
                        instanceId = meta.Id;
                    }
                    else
                    {
                        instanceId = Guid.NewGuid().ToString();
                        meta = new InstanceMeta
                        {
                            Id = instanceId,
                            Name = $"{branchName} v{version}",
                            Branch = branchName,
                            Version = version,
                            CreatedAt = DateTime.UtcNow,
                            IsLatest = false
                        };
                        _instances.SaveInstanceMeta(instanceDir, meta);
                    }

                    var newPath = Path.Combine(branchDir, instanceId);
                    if (Directory.Exists(newPath))
                    {
                        Logger.Warning("Migrate", $"Target folder already exists: {newPath}, skipping {instanceDir}");
                        continue;
                    }

                    try
                    {
                        Directory.Move(instanceDir, newPath);
                        Logger.Success("Migrate", $"Migrated {branchName}/{version} -> {branchName}/{instanceId}");
                        migratedCount++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Migrate", $"Failed to rename {instanceDir} to {newPath}: {ex.Message}");
                    }
                }
            }

            if (migratedCount > 0)
            {
                Logger.Success("Migrate", $"Migrated {migratedCount} instance folder(s) to ID-based naming");
                _instances.SyncInstancesWithConfig();
            }
            else
            {
                Logger.Info("Migrate", "No version-named folders found to migrate");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Migrate", $"Failed to migrate version folders to ID folders: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void MigrateBranchSubdirectoriesToFlat()
    {
        try
        {
            Logger.Info("Migrate", "Starting branch-subdirectory → flat instance migration...");
            var root = _instances.GetInstanceRoot();
            if (!Directory.Exists(root))
            {
                Logger.Info("Migrate", "No instance root directory found, skipping flat migration");
                return;
            }

            int migratedCount = 0;

            foreach (var branch in new[] { "release", "pre-release" })
            {
                var branchDir = Path.Combine(root, branch);
                if (!Directory.Exists(branchDir)) continue;

                foreach (var instanceDir in Directory.GetDirectories(branchDir))
                {
                    var folderName = Path.GetFileName(instanceDir);

                    if (!Guid.TryParse(folderName, out _))
                    {
                        Logger.Warning("Migrate", $"Skipping non-GUID folder in branch dir: {instanceDir}");
                        continue;
                    }

                    var target = Path.Combine(root, folderName);
                    if (Directory.Exists(target))
                    {
                        Logger.Debug("Migrate", $"Flat target already exists: {target}, skipping {instanceDir}");
                        continue;
                    }

                    try
                    {
                        Directory.Move(instanceDir, target);
                        migratedCount++;
                        Logger.Success("Migrate", $"Flattened {branch}/{folderName} → {folderName}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Migrate", $"Failed to flatten {instanceDir}: {ex.Message}");
                    }
                }

                try
                {
                    if (Directory.Exists(branchDir) && !Directory.EnumerateFileSystemEntries(branchDir).Any())
                    {
                        Directory.Delete(branchDir);
                        Logger.Info("Migrate", $"Removed empty branch directory: {branchDir}");
                    }
                }
                catch { }
            }

            if (migratedCount > 0)
            {
                Logger.Success("Migrate", $"Flattened {migratedCount} instance(s) into {root}");
                _instances.SyncInstancesWithConfig();
            }
            else
            {
                Logger.Info("Migrate", "No branch-subdirectory instances found to flatten");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Migrate", $"Failed to flatten branch subdirectories: {ex.Message}");
        }
    }

    #region Private helpers (legacy data reading)

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

    /// <summary>
    /// Loads configuration from a JSON file at the specified path
    /// </summary>
    private static Config? LoadConfigFromPath(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Config>(json, JsonDefaults.CaseInsensitive);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loads configuration from a TOML file at the specified path
    /// </summary>
    private static Config? LoadConfigFromToml(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            var cfg = new Config();
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;

                static string Unquote(string value)
                {
                    value = value.Trim();
                    if (value.StartsWith('"') && value.EndsWith('"') && value.Length >= 2)
                    {
                        return value[1..^1];
                    }
                    if (value.StartsWith('\'') && value.EndsWith('\'') && value.Length >= 2)
                    {
                        return value[1..^1];
                    }
                    return value;
                }

                var parts = trimmed.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2) continue;

                var key = parts[0].Trim().ToLowerInvariant();
                var val = Unquote(parts[1]);

                switch (key)
                {
                    case "instance_directory":
                    case "instancedirectory":
                    case "instance_dir":
                    case "instancepath":
                    case "instance_path":
                        cfg.InstanceDirectory = val;
                        break;
                    case "versiontype":
                    case "branch":
#pragma warning disable CS0618 // Legacy migration: parsing old config format
                        cfg.VersionType = LauncherUtilities.NormalizeVersionType(val);
#pragma warning restore CS0618
                        break;
                    case "selectedversion":
#pragma warning disable CS0618 // Legacy migration: parsing old config format
                        if (int.TryParse(val, out var sel)) cfg.SelectedVersion = sel;
#pragma warning restore CS0618
                        break;
                }
            }
            return cfg;
        }
        catch
        {
            return null;
        }
    }

    #endregion
}
