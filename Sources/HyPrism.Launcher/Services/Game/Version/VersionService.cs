using System.Text.Json;
using System.Runtime.InteropServices;
using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Game.Sources;

namespace HyPrism.Services.Game.Version;

/// <summary>
/// Manages game version detection, update checking, and version caching.
/// Queries all registered version sources (official + mirrors) and merges results.
/// </summary>
/// <remarks>
/// Version information is cached to avoid excessive network requests.
/// Fetches from ALL sources and stores them separately, merging for queries.
/// Sources are queried by priority (official first, then mirrors).
/// </remarks>
public class VersionService : IVersionService
{
    private readonly string _appDir;
    private readonly IConfigService _configService;
    private readonly HttpClient _httpClient;
    private readonly List<IVersionSource> _sources;
    private readonly SemaphoreSlim _versionFetchLock = new(1, 1);

    // Keep direct references for source-specific operations
    private readonly HytaleVersionSource? _hytaleSource;
    private readonly List<IVersionSource> _mirrorSources = new();
    
    // Selected mirror for downloads (set after speed test)
    private IVersionSource? _selectedMirror;

    /// <summary>
    /// In-memory cache of versions and patch data, backed by on-disk files.
    /// </summary>
    private readonly VersionCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionService"/> class.
    /// </summary>
    public VersionService(
        string appDir,
        IConfigService configService,
        HttpClient httpClient,
        HytaleVersionSource? hytaleSource = null,
        IEnumerable<IVersionSource>? mirrorSources = null)
    {
        _appDir = appDir;
        _configService = configService;
        _httpClient = httpClient;
        
        // Build source list
        _sources = new List<IVersionSource>();
        
        if (hytaleSource != null)
        {
            _hytaleSource = hytaleSource;
            _sources.Add(hytaleSource);
        }
        
        if (mirrorSources != null)
        {
            foreach (var mirror in mirrorSources.Where(m => m.Type == VersionSourceType.Mirror))
            {
                _mirrorSources.Add(mirror);
                _sources.Add(mirror);
            }
        }
        
        // Sort by priority
        _sources.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        // Initialise cache (must be done after _mirrorSources is populated)
        _cache = new VersionCache(appDir, () => _mirrorSources.Select(m => m.SourceId));

        foreach (var source in _sources)
        {
            Logger.Debug("Version", $"Source {source.SourceId}: full={source.LayoutInfo.FullBuildLocation}; patch={source.LayoutInfo.PatchLocation}; cache={source.LayoutInfo.CachePolicy}");
        }
    }

    /// <summary>
    /// Whether an official Hytale account is available for authenticated requests.
    /// </summary>
    public bool HasOfficialAccount => _hytaleSource?.IsAvailable ?? false;

    /// <inheritdoc/>
    public async Task<List<int>> GetVersionListAsync(string branch, CancellationToken ct = default)
    {
        var normalizedBranch = NormalizeBranch(branch);
        string osName = UtilityService.GetOS();
        string arch = UtilityService.GetArch();
        var maxAge = TimeSpan.FromMinutes(15);

        // Fast path: return from cache if this specific branch is fresh
        var cachedSnapshot = _cache.TryGet(osName, arch);
        if (cachedSnapshot != null && _cache.IsBranchFresh(cachedSnapshot, normalizedBranch, maxAge))
        {
            var versions = GetMergedVersionList(cachedSnapshot, normalizedBranch);
            if (versions.Count > 0)
            {
                Logger.Info("Version", $"Using cached versions for {branch}: [{string.Join(", ", versions)}]");
                return versions;
            }
        }

        // Serialize network fetches so parallel callers don't duplicate work
        await _versionFetchLock.WaitAsync(ct);
        try
        {
            // Re-check cache: another caller may have populated it while we waited
            cachedSnapshot = _cache.TryGet(osName, arch);
            if (cachedSnapshot != null && _cache.IsBranchFresh(cachedSnapshot, normalizedBranch, maxAge))
            {
                var versions = GetMergedVersionList(cachedSnapshot, normalizedBranch);
                if (versions.Count > 0)
                {
                    return versions;
                }
            }

            return await FetchVersionListCoreAsync(normalizedBranch, osName, arch, ct);
        }
        finally
        {
            _versionFetchLock.Release();
        }
    }

    private async Task<List<int>> FetchVersionListCoreAsync(string normalizedBranch, string osName, string arch, CancellationToken ct)
    {
        // Load existing cache or create new
        var snapshot = _cache.Load() ?? new VersionsCacheSnapshot
        {
            Os = osName,
            Arch = arch,
            FetchedAtUtc = DateTime.UtcNow,
            Data = new VersionsCacheData()
        };

        snapshot = _cache.Sanitize(snapshot);
        snapshot.Os = osName;
        snapshot.Arch = arch;
        snapshot.FetchedAtUtc = DateTime.UtcNow;

        // Load existing patch cache (same structure, saved alongside versions)
        var patchSnapshot = _cache.LoadPatches() ?? new PatchesCacheSnapshot
        {
            Os = osName,
            Arch = arch,
            FetchedAtUtc = DateTime.UtcNow,
            Data = new PatchesCacheData()
        };
        patchSnapshot.Os = osName;
        patchSnapshot.Arch = arch;
        patchSnapshot.FetchedAtUtc = DateTime.UtcNow;

        // Fetch versions AND patches from ALL sources
        foreach (var source in _sources)
        {
            if (!source.IsAvailable)
            {
                Logger.Debug("Version", $"Source {source.SourceId} not available, skipping");
                continue;
            }

            Logger.Info("Version", $"Fetching from {source.SourceId} for {normalizedBranch}...");
            try
            {
                var versions = await source.GetVersionsAsync(osName, arch, normalizedBranch, ct);
                if (versions.Count > 0)
                {
                    // Store in appropriate cache based on source type
                    if (source.Type == VersionSourceType.Official)
                    {
                        snapshot.Data.Hytale ??= new OfficialSourceCache();
                        snapshot.Data.Hytale.Branches[normalizedBranch] = versions;
                    }
                    else
                    {
                        // Mirror source
                        var mirrorCache = snapshot.Data.Mirrors.FirstOrDefault(m => m.MirrorId == source.SourceId);
                        if (mirrorCache == null)
                        {
                            mirrorCache = new MirrorSourceCache { MirrorId = source.SourceId };
                            snapshot.Data.Mirrors.Add(mirrorCache);
                        }
                        mirrorCache.Branches[normalizedBranch] = versions;
                    }
                    
                    Logger.Success("Version", $"{source.SourceId} returned {versions.Count} versions for {normalizedBranch}: [{string.Join(", ", versions.Select(v => v.Version))}]");
                }
                else
                {
                    Logger.Warning("Version", $"{source.SourceId} returned no versions for {normalizedBranch}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Version", $"{source.SourceId} fetch failed for {normalizedBranch}: {ex.Message}");
            }

            // Fetch patch chain from the same source (runs in sync, no fire-and-forget)
            try
            {
                var patchSteps = await source.GetPatchChainAsync(osName, arch, normalizedBranch, ct);
                if (patchSteps.Count > 0)
                {
                    if (source.Type == VersionSourceType.Official)
                    {
                        patchSnapshot.Data.Hytale ??= new Dictionary<string, List<CachedPatchStep>>();
                        patchSnapshot.Data.Hytale[normalizedBranch] = patchSteps;
                    }
                    else
                    {
                        var mirrorPatch = patchSnapshot.Data.Mirrors.FirstOrDefault(m => m.MirrorId == source.SourceId);
                        if (mirrorPatch == null)
                        {
                            mirrorPatch = new MirrorPatchCache { MirrorId = source.SourceId };
                            patchSnapshot.Data.Mirrors.Add(mirrorPatch);
                        }
                        mirrorPatch.Branches[normalizedBranch] = patchSteps;
                    }

                    Logger.Success("Version", $"{source.SourceId} returned {patchSteps.Count} patch steps for {normalizedBranch}");
                }
            }
            catch (Exception ex)
            {
                Logger.Debug("Version", $"{source.SourceId} patch chain fetch failed for {normalizedBranch}: {ex.Message}");
            }
        }

        // Update per-branch fetch timestamp
        snapshot.BranchFetchedAt[normalizedBranch] = DateTime.UtcNow;

        // Save both caches together
        _cache.Save(snapshot);
        _cache.SavePatches(patchSnapshot);
        _cache.Set(snapshot);

        // Return merged version list
        var result = GetMergedVersionList(snapshot, normalizedBranch);
        Logger.Info("Version", $"Total versions for {normalizedBranch}: [{string.Join(", ", result)}]");
        return result;
    }

    /// <summary>
    /// Gets merged version list from cache, preferring official source for duplicates.
    /// </summary>
    private List<int> GetMergedVersionList(VersionsCacheSnapshot snapshot, string branch)
    {
        var allVersions = new Dictionary<int, VersionSource>();

        // Add mirror versions first
        foreach (var mirror in snapshot.Data.Mirrors)
        {
            if (mirror.Branches.TryGetValue(branch, out var mirrorVersions))
            {
                foreach (var v in mirrorVersions)
                {
                    allVersions[v.Version] = VersionSource.Mirror;
                }
            }
        }

        // Add/override with official versions (they take priority)
        if (snapshot.Data.Hytale?.Branches.TryGetValue(branch, out var officialVersions) == true)
        {
            foreach (var v in officialVersions)
            {
                allVersions[v.Version] = VersionSource.Official;
            }
        }

        return allVersions.Keys.OrderByDescending(v => v).ToList();
    }

    public bool TryGetCachedVersions(string branch, TimeSpan maxAge, out List<int> versions)
    {
        versions = new List<int>();
        var normalizedBranch = NormalizeBranch(branch);
        string osName = UtilityService.GetOS();
        string arch = UtilityService.GetArch();

        var cached = _cache.TryGet(osName, arch);
        if (cached == null || !_cache.IsBranchFresh(cached, normalizedBranch, maxAge))
        {
            return false;
        }

        versions = GetMergedVersionList(cached, normalizedBranch);
        return versions.Count > 0;
    }

    /// <summary>
    /// Gets version list with source information (official vs mirror).
    /// </summary>
    public async Task<VersionListResponse> GetVersionListWithSourcesAsync(string branch, CancellationToken ct = default)
    {
        var normalizedBranch = NormalizeBranch(branch);
        
        // Ensure we have fetched the versions
        await GetVersionListAsync(normalizedBranch, ct);

        var snapshot = _cache.Current ?? _cache.Load();
        
        var response = new VersionListResponse
        {
            HasOfficialAccount = HasOfficialAccount,
            OfficialSourceAvailable = snapshot?.Data.Hytale?.Branches.ContainsKey(normalizedBranch) == true 
                && snapshot.Data.Hytale.Branches[normalizedBranch].Count > 0,
            HasDownloadSources = HasDownloadSources(),
            EnabledMirrorCount = EnabledMirrorCount
        };

        if (snapshot == null)
        {
            return response;
        }

        // Build version info list with proper sources
        var versionMap = new Dictionary<int, VersionInfo>();

        // Add mirror versions first
        foreach (var mirror in snapshot.Data.Mirrors)
        {
            if (mirror.Branches.TryGetValue(normalizedBranch, out var mirrorVersions))
            {
                foreach (var v in mirrorVersions)
                {
                    versionMap[v.Version] = new VersionInfo
                    {
                        Version = v.Version,
                        Source = VersionSource.Mirror,
                        IsLatest = false
                    };
                }
            }
        }

        // Override with official versions (they take priority)
        if (snapshot.Data.Hytale?.Branches.TryGetValue(normalizedBranch, out var officialVersions) == true)
        {
            foreach (var v in officialVersions)
            {
                versionMap[v.Version] = new VersionInfo
                {
                    Version = v.Version,
                    Source = VersionSource.Official,
                    IsLatest = false
                };
            }
        }

        // Sort and mark latest
        var sortedVersions = versionMap.Values.OrderByDescending(v => v.Version).ToList();
        if (sortedVersions.Count > 0)
        {
            sortedVersions[0].IsLatest = true;
        }

        response.Versions = sortedVersions;
        return response;
    }

    /// <summary>
    /// Gets the source of versions for a branch.
    /// </summary>
    public VersionSource GetVersionSource(string branch)
    {
        var normalizedBranch = NormalizeBranch(branch);
        var snapshot = _cache.Current ?? _cache.Load();
        
        if (snapshot?.Data.Hytale?.Branches.TryGetValue(normalizedBranch, out var versions) == true && versions.Count > 0)
        {
            return VersionSource.Official;
        }
        
        return VersionSource.Mirror;
    }

    /// <summary>
    /// Gets the download URL for a specific version.
    /// Prefers official source if available.
    /// </summary>
    public string? GetVersionDownloadUrl(string branch, int version)
    {
        var normalizedBranch = NormalizeBranch(branch);
        var snapshot = _cache.Current ?? _cache.Load();
        
        if (snapshot == null) return null;

        // Check official source first
        if (snapshot.Data.Hytale?.Branches.TryGetValue(normalizedBranch, out var officialVersions) == true)
        {
            var entry = officialVersions.FirstOrDefault(v => v.Version == version);
            if (entry != null && !string.IsNullOrEmpty(entry.PwrUrl))
            {
                return entry.PwrUrl;
            }
        }

        // Fallback to mirror
        foreach (var mirror in snapshot.Data.Mirrors)
        {
            if (mirror.Branches.TryGetValue(normalizedBranch, out var mirrorVersions))
            {
                var entry = mirrorVersions.FirstOrDefault(v => v.Version == version);
                if (entry != null && !string.IsNullOrEmpty(entry.PwrUrl))
                {
                    return entry.PwrUrl;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the cached version entry for a specific version.
    /// </summary>
    public CachedVersionEntry? GetVersionEntry(string branch, int version)
    {
        var normalizedBranch = NormalizeBranch(branch);
        var snapshot = _cache.Current ?? _cache.Load();
        
        if (snapshot == null) return null;

        // Check official source first
        if (snapshot.Data.Hytale?.Branches.TryGetValue(normalizedBranch, out var officialVersions) == true)
        {
            var entry = officialVersions.FirstOrDefault(v => v.Version == version);
            if (entry != null)
            {
                return entry;
            }
        }

        // Fallback to mirror
        foreach (var mirror in snapshot.Data.Mirrors)
        {
            if (mirror.Branches.TryGetValue(normalizedBranch, out var mirrorVersions))
            {
                var entry = mirrorVersions.FirstOrDefault(v => v.Version == version);
                if (entry != null)
                {
                    return entry;
                }
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public async Task<string> RefreshAndGetDownloadUrlAsync(string branch, int version, CancellationToken ct = default)
    {
        var normalizedBranch = NormalizeBranch(branch);
        
        // 1. Check cache first
        var url = GetVersionDownloadUrl(normalizedBranch, version);
        if (!string.IsNullOrEmpty(url))
        {
            Logger.Debug("Version", $"Using cached URL for {normalizedBranch} v{version}");
            return url;
        }
        
        // 2. Cache miss - refresh from all sources
        Logger.Info("Version", $"No cached URL for {normalizedBranch} v{version}, refreshing cache...");
        await ForceRefreshCacheAsync(normalizedBranch, ct);
        
        // 3. Try again after refresh
        url = GetVersionDownloadUrl(normalizedBranch, version);
        if (!string.IsNullOrEmpty(url))
        {
            Logger.Success("Version", $"Got URL for {normalizedBranch} v{version} after cache refresh");
            return url;
        }
        
        // 4. Still no URL - version doesn't exist in any source
        throw new Exception($"No download URL available for {normalizedBranch} v{version}. " +
            "The version may not exist or all sources are unavailable.");
    }

    /// <inheritdoc/>
    public async Task<CachedVersionEntry> RefreshAndGetVersionEntryAsync(string branch, int version, CancellationToken ct = default)
    {
        var normalizedBranch = NormalizeBranch(branch);
        
        // 1. Check cache first
        var entry = GetVersionEntry(normalizedBranch, version);
        if (entry != null && !string.IsNullOrEmpty(entry.PwrUrl))
        {
            Logger.Debug("Version", $"Using cached entry for {normalizedBranch} v{version}");
            return entry;
        }
        
        // 2. Cache miss - refresh from all sources
        Logger.Info("Version", $"No cached entry for {normalizedBranch} v{version}, refreshing cache...");
        await ForceRefreshCacheAsync(normalizedBranch, ct);
        
        // 3. Try again after refresh
        entry = GetVersionEntry(normalizedBranch, version);
        if (entry != null && !string.IsNullOrEmpty(entry.PwrUrl))
        {
            Logger.Success("Version", $"Got entry for {normalizedBranch} v{version} after cache refresh");
            return entry;
        }
        
        // 4. Still no entry - version doesn't exist in any source
        throw new Exception($"Version {normalizedBranch} v{version} not found in any source. " +
            "The version may not exist or all sources are unavailable.");
    }

    /// <inheritdoc/>
    public async Task ForceRefreshCacheAsync(string branch, CancellationToken ct = default)
    {
        var normalizedBranch = NormalizeBranch(branch);
        string osName = UtilityService.GetOS();
        string arch = UtilityService.GetArch();
        
        // Clear memory cache to force re-fetch
        _cache.Invalidate();
        
        await _versionFetchLock.WaitAsync(ct);
        try
        {
            await FetchVersionListCoreAsync(normalizedBranch, osName, arch, ct);
        }
        finally
        {
            _versionFetchLock.Release();
        }
    }

    /// <summary>
    /// Removes a specific version from the cache for a given mirror/source.
    /// Call this when a download fails with 404 to prevent showing unavailable versions.
    /// </summary>
    /// <param name="branch">Branch name (e.g., "release", "pre-release").</param>
    /// <param name="version">Version number to invalidate.</param>
    /// <param name="sourceId">Source ID (mirror ID or "official"). If null, removes from all sources.</param>
    public void InvalidateVersionFromCache(string branch, int version, string? sourceId = null)
    {
        var normalizedBranch = NormalizeBranch(branch);
        var snapshot = _cache.Current ?? _cache.Load();
        if (snapshot == null) return;

        bool modified = false;

        // Remove from official source if applicable
        if (sourceId == null || sourceId.Equals("official", StringComparison.OrdinalIgnoreCase))
        {
            if (snapshot.Data.Hytale?.Branches.TryGetValue(normalizedBranch, out var officialVersions) == true)
            {
                var removed = officialVersions.RemoveAll(v => v.Version == version);
                if (removed > 0)
                {
                    Logger.Info("Version", $"Invalidated v{version} from official cache for {normalizedBranch}");
                    modified = true;
                }
            }
        }

        // Remove from mirror sources
        foreach (var mirror in snapshot.Data.Mirrors)
        {
            if (sourceId != null && !mirror.MirrorId.Equals(sourceId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (mirror.Branches.TryGetValue(normalizedBranch, out var mirrorVersions))
            {
                var removed = mirrorVersions.RemoveAll(v => v.Version == version);
                if (removed > 0)
                {
                    Logger.Info("Version", $"Invalidated v{version} from {mirror.MirrorId} cache for {normalizedBranch}");
                    modified = true;
                }
            }
        }

        // Also invalidate from patch cache
        var patchSnapshot = _cache.LoadPatches();
        if (patchSnapshot != null)
        {
            bool patchModified = false;

            if (sourceId == null || sourceId.Equals("official", StringComparison.OrdinalIgnoreCase))
            {
                if (patchSnapshot.Data.Hytale?.TryGetValue(normalizedBranch, out var officialPatches) == true)
                {
                    var removed = officialPatches.RemoveAll(p => p.To == version);
                    if (removed > 0) patchModified = true;
                }
            }

            foreach (var mirror in patchSnapshot.Data.Mirrors)
            {
                if (sourceId != null && !mirror.MirrorId.Equals(sourceId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (mirror.Branches.TryGetValue(normalizedBranch, out var patches))
                {
                    var removed = patches.RemoveAll(p => p.To == version);
                    if (removed > 0) patchModified = true;
                }
            }

            if (patchModified)
            {
                _cache.SavePatches(patchSnapshot);
            }
        }

        if (modified)
        {
            _cache.Set(snapshot);
            _cache.Save(snapshot);
        }
    }

    /// <summary>
    /// Returns true if the specified branch uses diff-based patching (mirrors only).
    /// Pre-release branch uses diffs, release uses full copies.
    /// </summary>
    public bool IsDiffBasedBranch(string branch)
    {
        var normalizedBranch = NormalizeBranch(branch);
        return _selectedMirror?.IsDiffBasedBranch(normalizedBranch) ?? 
               _mirrorSources.FirstOrDefault()?.IsDiffBasedBranch(normalizedBranch) ?? false;
    }

    /// <summary>
    /// Gets download URL from mirror sources only.
    /// Used when official servers are down and we need explicit mirror fallback.
    /// </summary>
    public async Task<string?> GetMirrorDownloadUrlAsync(
        string os, string arch, string branch, int version, CancellationToken ct = default)
    {
        var normalizedBranch = NormalizeBranch(branch);

        var candidates = await GetMirrorCandidatesAsync(ct);
        foreach (var mirror in candidates)
        {
            try
            {
                var url = await mirror.GetDownloadUrlAsync(os, arch, normalizedBranch, version, ct);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    if (!ReferenceEquals(_selectedMirror, mirror))
                    {
                        _selectedMirror = mirror;
                        Logger.Info("Version", $"Switched active mirror to {mirror.SourceId} for {normalizedBranch} v{version}");
                    }

                    return url;
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Version", $"Mirror {mirror.SourceId} failed for {normalizedBranch} v{version}: {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// Gets diff patch URL from mirror sources for applying incremental updates.
    /// </summary>
    public async Task<string?> GetMirrorDiffUrlAsync(
        string os, string arch, string branch, int fromVersion, int toVersion, CancellationToken ct = default)
    {
        var normalizedBranch = NormalizeBranch(branch);

        var candidates = await GetMirrorCandidatesAsync(ct);
        foreach (var mirror in candidates)
        {
            try
            {
                var url = await mirror.GetDiffUrlAsync(os, arch, normalizedBranch, fromVersion, toVersion, ct);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    if (!ReferenceEquals(_selectedMirror, mirror))
                    {
                        _selectedMirror = mirror;
                        Logger.Info("Version", $"Switched active mirror to {mirror.SourceId} for {normalizedBranch} v{fromVersion}~{toVersion}");
                    }

                    return url;
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Version", $"Mirror {mirror.SourceId} failed for {normalizedBranch} v{fromVersion}~{toVersion}: {ex.Message}");
            }
        }

        return null;
    }

    private async Task<List<IVersionSource>> GetMirrorCandidatesAsync(CancellationToken ct)
    {
        var candidates = new List<IVersionSource>();

        var preferred = _selectedMirror ?? await SelectBestMirrorAsync(ct);
        if (preferred != null)
        {
            candidates.Add(preferred);
        }

        foreach (var mirror in _mirrorSources)
        {
            if (!candidates.Contains(mirror))
            {
                candidates.Add(mirror);
            }
        }

        return candidates;
    }

    /// <summary>
    /// Check if latest instance needs an update.
    /// </summary>
    public async Task<bool> CheckLatestNeedsUpdateAsync(string branch, Func<string, bool> isClientPresent, Func<string> getLatestInstancePath, Func<string, LatestVersionInfo?> loadLatestInfo)
    {
        var normalizedBranch = NormalizeBranch(branch);
        var versions = await GetVersionListAsync(normalizedBranch);
        if (versions.Count == 0) return false;

        var latest = versions[0];
        var latestPath = getLatestInstancePath();
        var info = loadLatestInfo(normalizedBranch);
        var baseOk = isClientPresent(latestPath);
        if (!baseOk) return true;
        if (info == null)
        {
            Logger.Info("Update", $"No latest.json found for {normalizedBranch}, assuming update may be needed");
            return true;
        }
        return info.Version != latest;
    }
    
    /// <summary>
    /// Gets the version status for the latest instance.
    /// </summary>
    public async Task<VersionStatus> GetLatestVersionStatusAsync(string branch, Func<string, bool> isClientPresent, Func<string> getLatestInstancePath, Func<string, LatestVersionInfo?> loadLatestInfo)
    {
        try
        {
            var normalizedBranch = NormalizeBranch(branch);
            var versions = await GetVersionListAsync(normalizedBranch);
            
            if (versions.Count == 0)
            {
                return new VersionStatus { Status = "none", InstalledVersion = 0, LatestVersion = 0 };
            }
            
            var latestAvailable = versions[0];
            var latestPath = getLatestInstancePath();
            var info = loadLatestInfo(normalizedBranch);
            var baseOk = isClientPresent(latestPath);
            
            if (!baseOk)
            {
                return new VersionStatus 
                { 
                    Status = "not_installed", 
                    InstalledVersion = 0, 
                    LatestVersion = latestAvailable 
                };
            }
            
            if (info == null)
            {
                return new VersionStatus 
                { 
                    Status = "update_available", 
                    InstalledVersion = 0, 
                    LatestVersion = latestAvailable 
                };
            }
            
            if (info.Version < latestAvailable)
            {
                return new VersionStatus 
                { 
                    Status = "update_available", 
                    InstalledVersion = info.Version, 
                    LatestVersion = latestAvailable 
                };
            }
            
            return new VersionStatus 
            { 
                Status = "current", 
                InstalledVersion = info.Version, 
                LatestVersion = latestAvailable 
            };
        }
        catch (Exception ex)
        {
            Logger.Error("Version", $"Failed to get latest version status: {ex.Message}");
            return new VersionStatus { Status = "error", InstalledVersion = 0, LatestVersion = 0 };
        }
    }

    /// <summary>
    /// Get pending update information.
    /// </summary>
    public async Task<UpdateInfo?> GetPendingUpdateInfoAsync(string branch, Func<string> getLatestInstancePath, Func<string, LatestVersionInfo?> loadLatestInfo)
    {
        try
        {
            var normalizedBranch = NormalizeBranch(branch);
            var versions = await GetVersionListAsync(normalizedBranch);
            if (versions.Count == 0) return null;

            var latestVersion = versions[0];
            var latestPath = getLatestInstancePath();
            var info = loadLatestInfo(normalizedBranch);
            
            if (info == null || info.Version == latestVersion) return null;
            
            var oldUserDataPath = Path.Combine(latestPath, "UserData");
            var hasOldUserData = Directory.Exists(oldUserDataPath) && 
                                 Directory.GetFileSystemEntries(oldUserDataPath).Length > 0;
            
            return new UpdateInfo
            {
                OldVersion = info.Version,
                NewVersion = latestVersion,
                HasOldUserData = hasOldUserData,
                Branch = normalizedBranch
            };
        }
        catch (Exception ex)
        {
            Logger.Warning("Update", $"Failed to get pending update info: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get sequence of patches to apply for differential update.
    /// </summary>
    public List<int> GetPatchSequence(int fromVersion, int toVersion)
    {
        var patches = new List<int>();
        for (int v = fromVersion + 1; v <= toVersion; v++)
        {
            patches.Add(v);
        }
        return patches;
    }

    /// <inheritdoc/>
    public bool IsOfficialServerDown(string branch)
    {
        var normalizedBranch = NormalizeBranch(branch);
        var snapshot = _cache.Current ?? _cache.Load();
        
        // Official is "down" if we don't have official data for this branch
        return snapshot?.Data.Hytale?.Branches.ContainsKey(normalizedBranch) != true 
            || snapshot.Data.Hytale.Branches[normalizedBranch].Count == 0;
    }

    // Utility methods
    private string NormalizeBranch(string branch)
    {
        return branch.ToLowerInvariant() switch
        {
            "release" => "release",
            "pre-release" => "pre-release",
            "prerelease" => "pre-release",
            "pre_release" => "pre-release",
            "beta" => "beta",
            "alpha" => "alpha",
            _ => "release"
        };
    }

    /// <summary>
    /// Tests the speed and availability of a mirror by ID.
    /// </summary>
    public async Task<MirrorSpeedTestResult> TestMirrorSpeedAsync(string mirrorId, bool forceRefresh = false, CancellationToken ct = default)
    {
        var mirror = _mirrorSources.FirstOrDefault(m => m.SourceId.Equals(mirrorId, StringComparison.OrdinalIgnoreCase));
        
        if (mirror == null)
        {
            Logger.Warning("Version", $"TestMirrorSpeedAsync: mirror '{mirrorId}' not found in {_mirrorSources.Count} loaded sources");
            return new MirrorSpeedTestResult
            {
                MirrorId = mirrorId,
                MirrorName = mirrorId,
                PingMs = -1,
                SpeedMBps = 0,
                IsAvailable = false,
                TestedAt = DateTime.UtcNow
            };
        }
        
        Logger.Debug("Version", $"TestMirrorSpeedAsync: testing mirror '{mirrorId}' (forceRefresh={forceRefresh})");
        
        if (!forceRefresh)
        {
            var cached = mirror.GetCachedSpeedTest();
            if (cached != null)
            {
                Logger.Debug("Version", $"TestMirrorSpeedAsync: returning cached result for '{mirrorId}'");
                return cached;
            }
        }
        
        return await mirror.TestSpeedAsync(ct);
    }
    
    /// <summary>
    /// Tests the speed and availability of the official Hytale CDN.
    /// </summary>
    public async Task<MirrorSpeedTestResult> TestOfficialSpeedAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        if (_hytaleSource == null || !_hytaleSource.IsAvailable)
        {
            return new MirrorSpeedTestResult
            {
                MirrorId = "official",
                MirrorName = "Hytale",
                MirrorUrl = "https://cdn.hytale.com",
                PingMs = -1,
                SpeedMBps = 0,
                IsAvailable = false,
                TestedAt = DateTime.UtcNow
            };
        }
        
        if (!forceRefresh)
        {
            var cached = _hytaleSource.GetCachedSpeedTest();
            if (cached != null)
            {
                return cached;
            }
        }
        
        return await _hytaleSource.TestSpeedAsync(ct);
    }
    
    /// <summary>
    /// Gets all available mirrors.
    /// </summary>
    public List<(string Id, string Name)> GetAvailableMirrors()
    {
        return _mirrorSources.Select(m => (m.SourceId, m.GetCachedSpeedTest()?.MirrorName ?? m.SourceId)).ToList();
    }
    
    /// <summary>
    /// Selects the best mirror based on speed tests.
    /// Only called when official source is not available.
    /// </summary>
    public async Task<IVersionSource?> SelectBestMirrorAsync(CancellationToken ct = default)
    {
        if (_mirrorSources.Count == 0)
        {
            Logger.Warning("Version", "No mirrors available for selection");
            return null;
        }
        
        // If only one mirror, use it
        if (_mirrorSources.Count == 1)
        {
            _selectedMirror = _mirrorSources[0];
            Logger.Info("Version", $"Only one mirror available: {_selectedMirror.SourceId}");
            return _selectedMirror;
        }
        
        Logger.Info("Version", $"Testing {_mirrorSources.Count} mirrors to select the best one...");
        
        var results = new List<(IVersionSource Source, MirrorSpeedTestResult Result)>();
        
        // Test all mirrors concurrently
        var tasks = _mirrorSources.Select(async mirror =>
        {
            try
            {
                var result = await mirror.TestSpeedAsync(ct);
                return (Source: mirror, Result: result);
            }
            catch (Exception ex)
            {
                Logger.Warning("Version", $"Mirror {mirror.SourceId} speed test failed: {ex.Message}");
                return (Source: mirror, Result: new MirrorSpeedTestResult
                {
                    MirrorId = mirror.SourceId,
                    MirrorName = mirror.SourceId,
                    IsAvailable = false,
                    PingMs = -1,
                    SpeedMBps = 0
                });
            }
        });
        
        var testResults = await Task.WhenAll(tasks);
        results.AddRange(testResults);
        
        // Filter available mirrors and sort by speed (descending)
        var availableMirrors = results
            .Where(r => r.Result.IsAvailable && r.Result.SpeedMBps > 0)
            .OrderByDescending(r => r.Result.SpeedMBps)
            .ThenBy(r => r.Result.PingMs)
            .ToList();
        
        if (availableMirrors.Count == 0)
        {
            Logger.Warning("Version", "No mirrors passed speed test, using first available");
            _selectedMirror = _mirrorSources[0];
            return _selectedMirror;
        }
        
        var best = availableMirrors[0];
        _selectedMirror = best.Source;
        Logger.Success("Version", $"Selected mirror: {best.Source.SourceId} ({best.Result.SpeedMBps:F2} MB/s, {best.Result.PingMs}ms ping)");
        
        return _selectedMirror;
    }
    
    /// <summary>
    /// Gets the currently selected mirror, or selects one if not yet selected.
    /// </summary>
    public async Task<IVersionSource?> GetSelectedMirrorAsync(CancellationToken ct = default)
    {
        if (_selectedMirror != null)
            return _selectedMirror;
        
        return await SelectBestMirrorAsync(ct);
    }
    
    /// <inheritdoc/>
    public void ReloadMirrorSources()
    {
        Logger.Info("Version", "Reloading mirror sources from disk...");
        
        // Remove old mirrors from sources
        foreach (var oldMirror in _mirrorSources)
        {
            _sources.Remove(oldMirror);
        }
        _mirrorSources.Clear();
        
        // Reset selected mirror
        _selectedMirror = null;
        
        // Load fresh mirrors from disk
        var freshMirrors = MirrorLoaderService.LoadAll(_appDir, _httpClient);
        
        foreach (var mirror in freshMirrors.Where(m => m.Type == VersionSourceType.Mirror))
        {
            _mirrorSources.Add(mirror);
            _sources.Add(mirror);
        }
        
        // Re-sort by priority
        _sources.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        
        Logger.Success("Version", $"Reloaded {_mirrorSources.Count} mirror sources");
        
        foreach (var source in _mirrorSources)
        {
            Logger.Debug("Version", $"Mirror {source.SourceId}: priority={source.Priority}");
        }
        
        // If no download sources remain, clear the version cache
        if (!HasDownloadSources())
        {
            Logger.Info("Version", "No download sources available after reload, clearing version cache");
            ClearVersionCache();
        }
    }
    
    /// <inheritdoc/>
    public bool HasDownloadSources()
    {
        return HasOfficialAccount || EnabledMirrorCount > 0;
    }
    
    /// <inheritdoc/>
    public int EnabledMirrorCount => _mirrorSources.Count;
    
    /// <inheritdoc/>
    public void ClearVersionCache()
    {
        try
        {
            // Clear in-memory cache
            _cache.Invalidate();
            
            // Delete versions cache file
            var versionsPath = _cache.GetSnapshotPath();
            if (File.Exists(versionsPath))
            {
                File.Delete(versionsPath);
                Logger.Info("Version", "Deleted versions cache file");
            }
            
            // Delete patches cache file
            var patchesPath = _cache.GetPatchSnapshotPath();
            if (File.Exists(patchesPath))
            {
                File.Delete(patchesPath);
                Logger.Info("Version", "Deleted patches cache file");
            }
            
            Logger.Success("Version", "Version cache cleared");
        }
        catch (Exception ex)
        {
            Logger.Warning("Version", $"Failed to clear version cache: {ex.Message}");
        }
    }
}
