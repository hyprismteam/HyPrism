using HyPrism.Models;

namespace HyPrism.Services.Game.Instance;

/// <summary>
/// Manages game instances including installation paths, version tracking, and instance lifecycle.
/// </summary>
public interface IInstanceService
{
    /// <summary>
    /// Gets the root directory where all game instances are stored.
    /// </summary>
    /// <returns>The absolute path to the instances root directory.</returns>
    string GetInstanceRoot();

    /// <summary>
    /// Gets the directory path for a specific game branch.
    /// </summary>
    /// <param name="branch">The game branch ("release" or "pre-release").</param>
    /// <returns>The absolute path to the branch directory.</returns>
    string GetBranchPath(string branch);

    /// <summary>
    /// Gets the user data path for a specific game instance.
    /// </summary>
    /// <param name="versionPath">The path to the game version directory.</param>
    /// <returns>The absolute path to the user data directory.</returns>
    string GetInstanceUserDataPath(string versionPath);

    /// <summary>
    /// Resolves a version number, returning the latest if the specified version is not available.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <param name="version">The requested version number.</param>
    /// <returns>The resolved version number.</returns>
    int ResolveVersionOrLatest(string branch, int version);

    /// <summary>
    /// Finds an existing instance path for the specified branch and version.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <param name="version">The version number.</param>
    /// <returns>The path to the existing instance, or <c>null</c> if not found.</returns>
    string? FindExistingInstancePath(string branch, int version);

    /// <summary>
    /// Gets all instance root paths including legacy installation locations.
    /// </summary>
    /// <returns>An enumerable of all instance root paths.</returns>
    IEnumerable<string> GetInstanceRootsIncludingLegacy();

    /// <summary>
    /// Gets the path to the latest installed instance for a branch.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <returns>The path to the latest instance directory.</returns>
    string GetLatestInstancePath(string branch);

    /// <summary>
    /// Gets the path to the latest instance info file for a branch.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <returns>The path to the latest info JSON file.</returns>
    string GetLatestInfoPath(string branch);

    /// <summary>
    /// Loads the latest instance info from disk.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <returns>The latest instance info, or <c>null</c> if not found.</returns>
    LatestInstanceInfo? LoadLatestInfo(string branch);

    /// <summary>
    /// Saves the latest instance info to disk.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <param name="version">The version number to save as latest.</param>
    void SaveLatestInfo(string branch, int version);

    /// <summary>
    /// Checks if the game client executable is present at the specified path.
    /// </summary>
    /// <param name="versionPath">The path to the game version directory.</param>
    /// <returns><c>true</c> if the client is present; otherwise, <c>false</c>.</returns>
    bool IsClientPresent(string versionPath);

    /// <summary>
    /// Checks if the game assets are present at the specified path.
    /// </summary>
    /// <param name="versionPath">The path to the game version directory.</param>
    /// <returns><c>true</c> if assets are present; otherwise, <c>false</c>.</returns>
    bool AreAssetsPresent(string versionPath);

    /// <summary>
    /// Gets the path for a specific game instance.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <param name="version">The version number.</param>
    /// <returns>The absolute path to the instance directory.</returns>
    string GetInstancePath(string branch, int version);

    /// <summary>
    /// Resolves the instance path, optionally preferring existing installations.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <param name="version">The version number.</param>
    /// <param name="preferExisting">Whether to prefer existing installations over creating new paths.</param>
    /// <returns>The resolved instance path.</returns>
    string ResolveInstancePath(string branch, int version, bool preferExisting);

    /// <summary>
    /// Deletes a game instance from disk.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <param name="versionNumber">The version number to delete.</param>
    /// <returns><c>true</c> if the instance was successfully deleted; otherwise, <c>false</c>.</returns>
    bool DeleteGame(string branch, int versionNumber);

    /// <summary>
    /// Deletes a game instance from disk by its unique ID.
    /// </summary>
    /// <param name="instanceId">The instance ID.</param>
    /// <returns><c>true</c> if the instance was successfully deleted; otherwise, <c>false</c>.</returns>
    bool DeleteGameById(string instanceId);

    /// <summary>
    /// Gets a list of all installed game instances.
    /// </summary>
    /// <returns>A list of installed instance metadata.</returns>
    List<InstalledInstance> GetInstalledInstances();

    /// <summary>
    /// Sets or clears the custom name for an instance by branch and version.
    /// </summary>
    /// <param name="branch">The game branch (e.g., "release", "pre-release").</param>
    /// <param name="version">The version number.</param>
    /// <param name="customName">The custom name to set, or null to clear.</param>
    void SetInstanceCustomName(string branch, int version, string? customName);

    /// <summary>
    /// Sets or clears the custom name for an instance by ID.
    /// </summary>
    /// <param name="instanceId">The instance ID (GUID).</param>
    /// <param name="customName">The custom name to set, or null to clear.</param>
    void SetInstanceCustomNameById(string instanceId, string? customName);

    /// <summary>
    /// Gets the instance metadata from the meta.json file.
    /// </summary>
    /// <param name="instancePath">The path to the instance directory.</param>
    /// <returns>The instance metadata, or null if not found.</returns>
    InstanceMeta? GetInstanceMeta(string instancePath);

    /// <summary>
    /// Saves instance metadata to the meta.json file.
    /// </summary>
    /// <param name="instancePath">The path to the instance directory.</param>
    /// <param name="meta">The metadata to save.</param>
    void SaveInstanceMeta(string instancePath, InstanceMeta meta);

    /// <summary>
    /// Creates a new instance with a generated ID.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <param name="version">The version number.</param>
    /// <param name="name">Optional custom name for the instance.</param>
    /// <param name="isLatest">Whether this is the auto-updating "latest" instance.</param>
    /// <returns>The created instance metadata.</returns>
    InstanceMeta CreateInstanceMeta(string branch, int version, string? name = null, bool isLatest = false);

    /// <summary>
    /// Gets the currently selected instance based on SelectedInstanceId.
    /// </summary>
    /// <returns>The selected instance info, or null if none selected.</returns>
    InstanceInfo? GetSelectedInstance();

    /// <summary>
    /// Sets the selected instance by ID.
    /// </summary>
    /// <param name="instanceId">The instance ID to select.</param>
    void SetSelectedInstance(string instanceId);

    /// <summary>
    /// Synchronizes Config.Instances with the actual instance folders on disk.
    /// Removes entries for missing instances and adds entries for new ones.
    /// </summary>
    void SyncInstancesWithConfig();

    /// <summary>
    /// Returns the in-memory/file-backed instance cache without rescanning the disk.
    /// Useful for fast lookups when a full sync is not required.
    /// </summary>
    List<InstanceInfo> GetCachedInstances();

    /// <summary>
    /// Finds an instance by its ID.
    /// </summary>
    /// <param name="instanceId">The instance ID.</param>
    /// <returns>The instance info, or null if not found.</returns>
    InstanceInfo? FindInstanceById(string instanceId);

    /// <summary>
    /// Gets the instance path by its unique ID.
    /// </summary>
    /// <param name="instanceId">The instance ID.</param>
    /// <returns>The absolute path to the instance directory, or null if not found.</returns>
    string? GetInstancePathById(string instanceId);

    /// <summary>
    /// Finds an instance by branch and version.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <param name="version">The version number.</param>
    /// <returns>The instance info, or null if not found.</returns>
    InstanceInfo? FindInstanceByBranchAndVersion(string branch, int version);

    /// <summary>
    /// Creates a new instance directory with the given ID and returns the path.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <param name="instanceId">The unique instance ID (will be folder name).</param>
    /// <returns>The absolute path to the new instance directory.</returns>
    string CreateInstanceDirectory(string branch, string instanceId);

    /// <summary>
    /// Changes the version/branch of an existing instance.
    /// For upgrades within the same branch: preserves game files and sets up for patching.
    /// For downgrades or branch changes: clears game client files and prepares for fresh download.
    /// Always keeps UserData, and marks the instance as non-latest so it never suggests updates.
    /// </summary>
    /// <param name="instanceId">The unique instance ID.</param>
    /// <param name="branch">The new game branch (e.g. "release").</param>
    /// <param name="version">The new version number.</param>
    /// <returns>True if the operation succeeded.</returns>
    bool ChangeInstanceVersion(string instanceId, string branch, int version);

    /// <summary>
    /// Imports a ZIP archive as a new game instance.
    /// Extracts the archive, reads meta.json for branch/version/id info,
    /// deduplicates instance IDs, and moves the contents to the instances directory.
    /// </summary>
    /// <param name="zipPath">The path to the ZIP archive to import.</param>
    Task ImportFromZipAsync(string zipPath);
}
