using HyPrism.Models;
using HyPrism.Services.Game.Sources;

namespace HyPrism.Services.Game.Version;

/// <summary>
/// Provides version management and update detection for game installations.
/// </summary>
public interface IVersionService
{
    /// <summary>
    /// Gets the list of available versions for a branch from the server.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <param name="ct">Token to cancel the request.</param>
    /// <returns>A list of available version numbers, sorted descending.</returns>
    Task<List<int>> GetVersionListAsync(string branch, CancellationToken ct = default);

    /// <summary>
    /// Attempts to retrieve cached version information if it's still valid.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <param name="maxAge">The maximum age of cached data to accept.</param>
    /// <param name="versions">The cached versions if found and valid.</param>
    /// <returns><c>true</c> if valid cached data was found; otherwise, <c>false</c>.</returns>
    bool TryGetCachedVersions(string branch, TimeSpan maxAge, out List<int> versions);

    /// <summary>
    /// Checks if the latest installed version needs an update.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <param name="isClientPresent">Function to check if client exists at a path.</param>
    /// <param name="getLatestInstancePath">Function to get the latest instance path.</param>
    /// <param name="loadLatestInfo">Function to load the latest version info from a path.</param>
    /// <returns><c>true</c> if an update is available; otherwise, <c>false</c>.</returns>
    Task<bool> CheckLatestNeedsUpdateAsync(string branch, Func<string, bool> isClientPresent, Func<string> getLatestInstancePath, Func<string, LatestVersionInfo?> loadLatestInfo);

    /// <summary>
    /// Gets detailed status information about the latest version.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <param name="isClientPresent">Function to check if client exists at a path.</param>
    /// <param name="getLatestInstancePath">Function to get the latest instance path.</param>
    /// <param name="loadLatestInfo">Function to load the latest version info from a path.</param>
    /// <returns>The version status including installed and available versions.</returns>
    Task<VersionStatus> GetLatestVersionStatusAsync(string branch, Func<string, bool> isClientPresent, Func<string> getLatestInstancePath, Func<string, LatestVersionInfo?> loadLatestInfo);

    /// <summary>
    /// Gets information about a pending update if one is available.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <param name="getLatestInstancePath">Function to get the latest instance path.</param>
    /// <param name="loadLatestInfo">Function to load the latest version info from a path.</param>
    /// <returns>Update information if an update is pending, or <c>null</c> if up to date.</returns>
    Task<UpdateInfo?> GetPendingUpdateInfoAsync(string branch, Func<string> getLatestInstancePath, Func<string, LatestVersionInfo?> loadLatestInfo);

    /// <summary>
    /// Calculates the sequence of patch versions needed to update from one version to another.
    /// </summary>
    /// <param name="fromVersion">The starting version number.</param>
    /// <param name="toVersion">The target version number.</param>
    /// <returns>A list of version numbers representing the patch sequence.</returns>
    List<int> GetPatchSequence(int fromVersion, int toVersion);

    /// <summary>
    /// Checks whether versions for a branch were sourced from the mirror
    /// (indicating the official server is down).
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <returns><c>true</c> if versions came from mirror; otherwise, <c>false</c>.</returns>
    bool IsOfficialServerDown(string branch);

    /// <summary>
    /// Gets whether the user has an official Hytale account authenticated.
    /// </summary>
    bool HasOfficialAccount { get; }

    /// <summary>
    /// Gets the version source for a branch.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <returns>The version source (Official or Mirror).</returns>
    VersionSource GetVersionSource(string branch);

    /// <summary>
    /// Gets the list of available versions with source information.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <param name="ct">Token to cancel the request.</param>
    /// <returns>A response containing version info and source details.</returns>
    Task<VersionListResponse> GetVersionListWithSourcesAsync(string branch, CancellationToken ct = default);

    /// <summary>
    /// Gets the download URL for a specific version from the cache.
    /// Prefers official source if available (contains signed token).
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <param name="version">The version number.</param>
    /// <returns>The download URL, or null if not cached.</returns>
    string? GetVersionDownloadUrl(string branch, int version);

    /// <summary>
    /// Gets the cached version entry with full download information.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <param name="version">The version number.</param>
    /// <returns>The cached version entry, or null if not found.</returns>
    CachedVersionEntry? GetVersionEntry(string branch, int version);

    /// <summary>
    /// Gets the download URL for a version, refreshing the cache if needed.
    /// This is the primary method for obtaining download URLs - it will:
    /// 1. Check the cache for an existing URL
    /// 2. If not found, refresh the cache from all sources
    /// 3. Return the URL or throw if still unavailable
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <param name="version">The version number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The download URL.</returns>
    /// <exception cref="Exception">Thrown if no URL is available after refresh.</exception>
    Task<string> RefreshAndGetDownloadUrlAsync(string branch, int version, CancellationToken ct = default);

    /// <summary>
    /// Gets the version entry, refreshing the cache if needed.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <param name="version">The version number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The version entry.</returns>
    /// <exception cref="Exception">Thrown if version not found after refresh.</exception>
    Task<CachedVersionEntry> RefreshAndGetVersionEntryAsync(string branch, int version, CancellationToken ct = default);

    /// <summary>
    /// Forces a refresh of the version cache for a specific branch.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ForceRefreshCacheAsync(string branch, CancellationToken ct = default);

    /// <summary>
    /// Removes a specific version from the cache for a given mirror/source.
    /// Call this when a download fails with 404 to prevent showing unavailable versions.
    /// </summary>
    /// <param name="branch">Branch name (e.g., "release", "pre-release").</param>
    /// <param name="version">Version number to invalidate.</param>
    /// <param name="sourceId">Source ID (mirror ID or "official"). If null, removes from all sources.</param>
    void InvalidateVersionFromCache(string branch, int version, string? sourceId = null);

    /// <summary>
    /// Returns true if the specified branch uses diff-based patching on mirrors.
    /// Pre-release branch uses diffs, release uses full copies.
    /// </summary>
    /// <param name="branch">The game branch.</param>
    /// <returns>True if the branch uses diff-based patching.</returns>
    bool IsDiffBasedBranch(string branch);

    /// <summary>
    /// Gets download URL from mirror sources only.
    /// Used when official servers are down and we need explicit mirror fallback.
    /// </summary>
    /// <param name="os">OS identifier.</param>
    /// <param name="arch">Architecture.</param>
    /// <param name="branch">The game branch.</param>
    /// <param name="version">Version number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Download URL from mirror, or null if not available.</returns>
    Task<string?> GetMirrorDownloadUrlAsync(string os, string arch, string branch, int version, CancellationToken ct = default);

    /// <summary>
    /// Gets diff patch URL from mirror sources for applying incremental updates.
    /// </summary>
    /// <param name="os">OS identifier.</param>
    /// <param name="arch">Architecture.</param>
    /// <param name="branch">The game branch.</param>
    /// <param name="fromVersion">Source version.</param>
    /// <param name="toVersion">Target version.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Diff patch URL from mirror, or null if not available.</returns>
    Task<string?> GetMirrorDiffUrlAsync(string os, string arch, string branch, int fromVersion, int toVersion, CancellationToken ct = default);
    
    /// <summary>
    /// Tests the speed and availability of a mirror.
    /// </summary>
    /// <param name="mirrorId">The mirror identifier (e.g., "estrogen").</param>
    /// <param name="forceRefresh">Whether to force a new speed test ignoring cache.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Speed test result including ping, download speed, and availability.</returns>
    Task<MirrorSpeedTestResult> TestMirrorSpeedAsync(string mirrorId, bool forceRefresh = false, CancellationToken ct = default);
    
    /// <summary>
    /// Tests the speed and availability of the official Hytale CDN.
    /// </summary>
    /// <param name="forceRefresh">Whether to force a new speed test ignoring cache.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Speed test result including ping, download speed, and availability.</returns>
    Task<MirrorSpeedTestResult> TestOfficialSpeedAsync(bool forceRefresh = false, CancellationToken ct = default);
    
    /// <summary>
    /// Gets a list of all available mirrors.
    /// </summary>
    /// <returns>List of tuples with mirror ID and display name.</returns>
    List<(string Id, string Name)> GetAvailableMirrors();
    
    /// <summary>
    /// Selects the best mirror based on speed tests.
    /// Only called when official source is not available.
    /// Tests all mirrors concurrently and selects the fastest available one.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The selected version source, or null if no mirrors available.</returns>
    Task<IVersionSource?> SelectBestMirrorAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Reloads mirror sources from disk.
    /// Call this after adding, deleting, or modifying mirror configurations.
    /// </summary>
    void ReloadMirrorSources();
    
    /// <summary>
    /// Checks whether there are any download sources available
    /// (either official Hytale account or enabled mirrors).
    /// </summary>
    /// <returns>True if at least one download source is available.</returns>
    bool HasDownloadSources();
    
    /// <summary>
    /// Gets the number of currently enabled mirror sources.
    /// </summary>
    int EnabledMirrorCount { get; }
    
    /// <summary>
    /// Clears all cached version and patch data.
    /// Call this when download sources become unavailable.
    /// </summary>
    void ClearVersionCache();
}
