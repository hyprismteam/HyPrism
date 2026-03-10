using System.Runtime.InteropServices;
using System.Text.Json;
using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.Game.Instance;

/// <summary>
/// Handles one-time and on-startup migrations of legacy instance folder structures
/// and legacy configuration data to the current format.
/// </summary>
/// <remarks>
/// Extracted from <see cref="InstanceService"/> to keep the instance service focused on
/// path resolution and instance CRUD. All methods are safe to call even when the
/// relevant legacy artefacts no longer exist — they simply become no-ops.
/// </remarks>
public class InstanceMigrationService : IInstanceMigrationService
{
    private readonly string _appDir;
    private readonly IConfigService _configService;
    private readonly IInstanceService _instanceService;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstanceMigrationService"/> class.
    /// </summary>
    /// <param name="appPath">The application path configuration.</param>
    /// <param name="configService">The configuration service.</param>
    /// <param name="instanceService">The instance service for path resolution and meta operations.</param>
    public InstanceMigrationService(
        AppPathConfiguration appPath,
        IConfigService configService,
        IInstanceService instanceService)
    {
        _appDir = appPath.AppDir;
        _configService = configService;
        _instanceService = instanceService;
    }

    /// <inheritdoc/>
    public void MigrateLegacyData()
    {
        try
        {
            var config = _configService.Configuration;

            foreach (var legacyRoot in GetLegacyRoots())
            {
                if (!Directory.Exists(legacyRoot)) continue;

                Logger.Info("Migrate", $"Found legacy data at {legacyRoot}");

                var legacyConfigPath = Path.Combine(legacyRoot, "config.json");
                var legacyTomlPath = Path.Combine(legacyRoot, "config.toml");

                // Load both JSON and TOML configs
                var jsonConfig = LoadConfigFromPath(legacyConfigPath);
                var tomlConfig = LoadConfigFromToml(legacyTomlPath);

                // Prefer TOML if it has a custom nick (not default), or prefer whichever has custom data
                Config? legacyConfig = null;
                bool tomlHasCustomNick = tomlConfig != null && !string.IsNullOrWhiteSpace(tomlConfig.Nick)
                    && !string.Equals(tomlConfig.Nick, "Hyprism", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(tomlConfig.Nick, "Player", StringComparison.OrdinalIgnoreCase);
                bool jsonHasCustomNick = jsonConfig != null && !string.IsNullOrWhiteSpace(jsonConfig.Nick)
                    && !string.Equals(jsonConfig.Nick, "Hyprism", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(jsonConfig.Nick, "Player", StringComparison.OrdinalIgnoreCase);

                if (tomlHasCustomNick)
                {
                    legacyConfig = tomlConfig;
                    Logger.Info("Migrate", $"Using legacy config.toml (has custom nick): nick={legacyConfig?.Nick}, uuid={legacyConfig?.UUID}");
                }
                else if (jsonHasCustomNick)
                {
                    legacyConfig = jsonConfig;
                    Logger.Info("Migrate", $"Using legacy config.json (has custom nick): nick={legacyConfig?.Nick}, uuid={legacyConfig?.UUID}");
                }
                else if (tomlConfig != null)
                {
                    legacyConfig = tomlConfig;
                    Logger.Info("Migrate", $"Using legacy config.toml: nick={legacyConfig?.Nick}, uuid={legacyConfig?.UUID}");
                }
                else if (jsonConfig != null)
                {
                    legacyConfig = jsonConfig;
                    Logger.Info("Migrate", $"Using legacy config.json: nick={legacyConfig?.Nick}, uuid={legacyConfig?.UUID}");
                }
                else
                {
                    Logger.Warning("Migrate", $"No valid config found in {legacyRoot}");
                }

                // Only merge legacy config when current user name is still a default/placeholder
                bool allowMerge = string.IsNullOrWhiteSpace(config.Nick)
                                  || string.Equals(config.Nick, "Hyprism", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(config.Nick, "Player", StringComparison.OrdinalIgnoreCase);

                if (!allowMerge)
                {
                    Logger.Info("Migrate", "Skipping legacy config merge because current nickname is custom.");
                }

                var updated = false;

                if (legacyConfig != null && allowMerge)
                {
                    Logger.Info("Migrate", $"Merging legacy config: nick={legacyConfig.Nick}");
                    if (!string.IsNullOrWhiteSpace(legacyConfig.Nick))
                    {
                        config.Nick = legacyConfig.Nick;
                        updated = true;
                        Logger.Success("Migrate", $"Migrated nickname: {legacyConfig.Nick}");
                    }

                    if (string.IsNullOrWhiteSpace(config.UUID) && !string.IsNullOrWhiteSpace(legacyConfig.UUID))
                    {
                        config.UUID = legacyConfig.UUID;
                        updated = true;
                    }

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
                        config.VersionType = UtilityService.NormalizeVersionType(legacyConfig.VersionType);
                        updated = true;
                    }
                    #pragma warning restore CS0618
                }

                // Fallback: pick up a legacy uuid file if config lacked one
                if (string.IsNullOrWhiteSpace(config.UUID))
                {
                    var legacyUuid = LoadLegacyUuid(legacyRoot);
                    if (!string.IsNullOrWhiteSpace(legacyUuid))
                    {
                        config.UUID = legacyUuid;
                        updated = true;
                        Logger.Info("Migrate", "Recovered legacy UUID from legacy folder.");
                    }
                }

                if (updated)
                {
                    _configService.SaveConfig();

                    // Delete old config.toml after successful migration
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

                // Detect legacy instance folders and copy to new structure
                var legacyInstanceRoot = Path.Combine(legacyRoot, "instance");
                var legacyInstancesRoot = Path.Combine(legacyRoot, "instances"); // v1 naming
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

            // Also migrate old 'instance' folder in current app dir (singular -> plural)
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
            var newInstanceRoot = _instanceService.GetInstanceRoot();

            // Check if source is the same as destination (case-insensitive for macOS)
            var normalizedSource = Path.GetFullPath(legacyInstanceRoot).TrimEnd(Path.DirectorySeparatorChar);
            var normalizedDest = Path.GetFullPath(newInstanceRoot).TrimEnd(Path.DirectorySeparatorChar);
            var isSameDirectory = normalizedSource.Equals(normalizedDest, StringComparison.OrdinalIgnoreCase);

            // If same directory, we'll restructure in-place (rename release-v5 to release/5)
            // If different directories, we'll copy as before
            if (isSameDirectory)
            {
                Logger.Info("Migrate", "Source equals destination - will restructure legacy folders in-place");
                RestructureLegacyFoldersInPlace(legacyInstanceRoot);
                return;
            }

            // CRITICAL: Prevent migration if source is inside destination (would cause infinite loop)
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

                // CRITICAL: Skip folders that are already branch names (new structure)
                // These indicate we're looking at already-migrated data
                var normalizedFolderName = folderName.ToLowerInvariant();
                if (normalizedFolderName == "release" || normalizedFolderName == "pre-release" ||
                    normalizedFolderName == "prerelease" || normalizedFolderName == "latest")
                {
                    Logger.Info("Migrate", $"Skipping {folderName} - already in new structure format");
                    continue;
                }

                // Parse legacy naming: "release-v5" or "release-5" or "release/5"
                string branch;
                string versionSegment;

                if (folderName.Contains("/"))
                {
                    // Already new format: release/5
                    var parts = folderName.Split('/');
                    branch = parts[0];
                    versionSegment = parts.Length > 1 ? parts[1] : "latest";
                }
                else if (folderName.Contains("-"))
                {
                    // Legacy dash format: release-v5 or release-5
                    var parts = folderName.Split('-', 2);
                    branch = parts[0];
                    versionSegment = parts.Length > 1 ? parts[1] : "latest";

                    // Strip 'v' prefix if present
                    if (versionSegment.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                    {
                        versionSegment = versionSegment.Substring(1);
                    }
                }
                else
                {
                    // Unknown format - skip to be safe (could be new structure subfolder)
                    Logger.Info("Migrate", $"Skipping {folderName} - unknown format, may be new structure");
                    continue;
                }

                // Normalize branch name
                branch = UtilityService.NormalizeVersionType(branch);

                // Create target path in new structure: instance/release/5
                var targetBranch = Path.Combine(newInstanceRoot, branch);
                var targetVersion = Path.Combine(targetBranch, versionSegment);

                // CRITICAL: Ensure we're not copying a folder into itself
                var normalizedLegacy = Path.GetFullPath(legacyDir).TrimEnd(Path.DirectorySeparatorChar);
                var normalizedTarget = Path.GetFullPath(targetVersion).TrimEnd(Path.DirectorySeparatorChar);
                if (normalizedLegacy.Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase) ||
                    normalizedTarget.StartsWith(normalizedLegacy + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    normalizedLegacy.StartsWith(normalizedTarget + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Info("Migrate", $"Skipping {folderName} - would cause recursive copy");
                    continue;
                }

                // Skip if already exists in new location
                if (Directory.Exists(targetVersion) && _instanceService.IsClientPresent(targetVersion))
                {
                    Logger.Info("Migrate", $"Skipping {folderName} - already exists at {targetVersion}");
                    continue;
                }

                Logger.Info("Migrate", $"Copying {folderName} -> {branch}/{versionSegment}");
                Directory.CreateDirectory(targetVersion);

                // Check if legacy has game/ subfolder or direct Client/ folder
                var legacyGameDir = Path.Combine(legacyDir, "game");
                var legacyClientDir = Path.Combine(legacyDir, "Client");

                if (Directory.Exists(legacyGameDir))
                {
                    // Legacy structure: release-v5/game/Client -> release/5/Client
                    foreach (var item in Directory.GetFileSystemEntries(legacyGameDir))
                    {
                        var name = Path.GetFileName(item);
                        var dest = Path.Combine(targetVersion, name);

                        if (Directory.Exists(item))
                        {
                            UtilityService.CopyDirectory(item, dest, false);
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
                    // Direct Client/ folder structure
                    foreach (var item in Directory.GetFileSystemEntries(legacyDir))
                    {
                        var name = Path.GetFileName(item);
                        var dest = Path.Combine(targetVersion, name);

                        if (Directory.Exists(item))
                        {
                            UtilityService.CopyDirectory(item, dest, false);
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
                    // Copy everything as-is
                    UtilityService.CopyDirectory(legacyDir, targetVersion, false);
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

                // Skip folders that are already branch names (new structure)
                var normalizedFolderName = folderName.ToLowerInvariant();
                if (normalizedFolderName == "release" || normalizedFolderName == "pre-release" ||
                    normalizedFolderName == "prerelease" || normalizedFolderName == "latest")
                {
                    // This is already new structure, skip
                    continue;
                }

                // Only process legacy dash format: release-v5 or release-5
                if (!folderName.Contains("-"))
                {
                    continue;
                }

                var parts = folderName.Split('-', 2);
                var branch = parts[0];
                var versionSegment = parts.Length > 1 ? parts[1] : "latest";

                // Strip 'v' prefix if present
                if (versionSegment.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                {
                    versionSegment = versionSegment.Substring(1);
                }

                // Normalize branch name
                branch = UtilityService.NormalizeVersionType(branch);

                // Create target path in new structure: instances/release/5
                var targetBranch = Path.Combine(instanceRoot, branch);
                var targetVersion = Path.Combine(targetBranch, versionSegment);

                // Skip if target already exists
                if (Directory.Exists(targetVersion))
                {
                    Logger.Info("Migrate", $"Skipping {folderName} - target {branch}/{versionSegment} already exists");
                    continue;
                }

                Logger.Info("Migrate", $"Restructuring {folderName} -> {branch}/{versionSegment}");

                // Create the branch directory
                Directory.CreateDirectory(targetBranch);

                // Check if legacy has game/ subfolder - if so, move contents up
                var legacyGameDir = Path.Combine(legacyDir, "game");

                if (Directory.Exists(legacyGameDir))
                {
                    // Legacy structure: release-v5/game/Client -> release/5/Client
                    // Move the contents of game/ to the new version folder
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

                    // Clean up old structure
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
                    // Direct structure - just rename the folder
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
            var root = _instanceService.GetInstanceRoot();
            if (!Directory.Exists(root))
            {
                Logger.Info("Migrate", "No instance root directory found, skipping migration");
                return;
            }

            int migratedCount = 0;

            foreach (var branchDir in Directory.GetDirectories(root))
            {
                var branchName = Path.GetFileName(branchDir);
                // Skip non-branch folders
                if (!branchName.Equals("release", StringComparison.OrdinalIgnoreCase) &&
                    !branchName.Equals("pre-release", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var instanceDir in Directory.GetDirectories(branchDir))
                {
                    var folderName = Path.GetFileName(instanceDir);

                    // Skip if folder is already named as GUID (new structure)
                    if (Guid.TryParse(folderName, out _))
                    {
                        continue;
                    }

                    // Handle "latest" folder - also needs to be renamed to ID
                    if (folderName.Equals("latest", StringComparison.OrdinalIgnoreCase))
                    {
                        var latestMeta = _instanceService.GetInstanceMeta(instanceDir);
                        string latestId;

                        if (latestMeta != null && !string.IsNullOrEmpty(latestMeta.Id))
                        {
                            latestId = latestMeta.Id;
                            // Ensure IsLatest is set correctly
                            if (!latestMeta.IsLatest)
                            {
                                latestMeta.IsLatest = true;
                                latestMeta.Version = 0;
                                if (string.IsNullOrEmpty(latestMeta.Name))
                                    latestMeta.Name = $"{branchName} (Latest)";
                                _instanceService.SaveInstanceMeta(instanceDir, latestMeta);
                            }
                        }
                        else
                        {
                            // Create meta for latest
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
                            _instanceService.SaveInstanceMeta(instanceDir, newLatestMeta);
                            Logger.Info("Migrate", $"Created meta.json for latest instance in {branchName}");
                        }

                        // Rename folder from "latest" to ID
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

                    // Check if this is a version-named folder (numeric)
                    if (!int.TryParse(folderName, out var version))
                    {
                        // Not a version number, skip
                        continue;
                    }

                    // This is a version-named folder, need to migrate
                    var meta = _instanceService.GetInstanceMeta(instanceDir);
                    string instanceId;

                    if (meta != null && !string.IsNullOrEmpty(meta.Id))
                    {
                        instanceId = meta.Id;
                    }
                    else
                    {
                        // Create new meta with ID
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
                        _instanceService.SaveInstanceMeta(instanceDir, meta);
                    }

                    // Rename folder from version to ID
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
                // Sync config with new folder structure
                _instanceService.SyncInstancesWithConfig();
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
            var root = _instanceService.GetInstanceRoot();
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

                    // Only migrate GUID-named folders (version-to-ID migration must run first)
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

                // Remove the branch directory if it is now empty
                try
                {
                    if (Directory.Exists(branchDir) && !Directory.EnumerateFileSystemEntries(branchDir).Any())
                    {
                        Directory.Delete(branchDir);
                        Logger.Info("Migrate", $"Removed empty branch directory: {branchDir}");
                    }
                }
                catch { /* non-critical */ }
            }

            if (migratedCount > 0)
            {
                Logger.Success("Migrate", $"Flattened {migratedCount} instance(s) into {root}");
                _instanceService.SyncInstancesWithConfig();
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

    /// <summary>
    /// Loads configuration from a JSON file at the specified path.
    /// </summary>
    private static Config? LoadConfigFromPath(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Config>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loads configuration from a TOML file at the specified path.
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
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;

                static string Unquote(string value)
                {
                    value = value.Trim();
                    // Handle double quotes
                    if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
                    {
                        return value.Substring(1, value.Length - 2);
                    }
                    // Handle single quotes (TOML style)
                    if (value.StartsWith("'") && value.EndsWith("'") && value.Length >= 2)
                    {
                        return value.Substring(1, value.Length - 2);
                    }
                    return value;
                }

                var parts = trimmed.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2) continue;

                var key = parts[0].Trim().ToLowerInvariant();
                var val = Unquote(parts[1]);

                switch (key)
                {
                    case "nick":
                    case "name":
                    case "username":
                        cfg.Nick = val;
                        break;
                    case "uuid":
                        cfg.UUID = val;
                        break;
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
                        cfg.VersionType = UtilityService.NormalizeVersionType(val);
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

    /// <summary>
    /// Loads a UUID from legacy uuid.txt/uuid.dat files.
    /// </summary>
    private static string? LoadLegacyUuid(string legacyRoot)
    {
        var candidates = new[] { "uuid.txt", "uuid", "uuid.dat" };
        foreach (var name in candidates)
        {
            var path = Path.Combine(legacyRoot, name);
            if (!File.Exists(path)) continue;

            try
            {
                var content = File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(content) && Guid.TryParse(content, out var guid))
                {
                    return guid.ToString();
                }
            }
            catch
            {
                // ignore malformed legacy uuid files
            }
        }

        return null;
    }

    #endregion
}
