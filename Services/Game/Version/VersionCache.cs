using System.Text.Json;
using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.Game.Version;

/// <summary>
/// Manages persistent (disk) and in-memory caching of version and patch data
/// for all registered sources (official + mirrors).
/// </summary>
public class VersionCache
{
    private readonly string _appDir;

    /// <summary>
    /// Factory that returns the set of currently registered mirror IDs.
    /// Used when sanitizing cache snapshots to remove stale mirror entries.
    /// </summary>
    private readonly Func<IEnumerable<string>> _getAllowedMirrorIds;

    /// <summary>
    /// In-memory copy of the most recently read or written versions snapshot.
    /// </summary>
    public VersionsCacheSnapshot? Current { get; private set; }

    /// <summary>
    /// Initializes a new instance of <see cref="VersionCache"/>.
    /// </summary>
    /// <param name="appDir">The application data root directory.</param>
    /// <param name="getAllowedMirrorIds">
    /// Delegate that returns the source IDs of currently registered mirrors.
    /// Called each time a snapshot is sanitized.
    /// </param>
    public VersionCache(string appDir, Func<IEnumerable<string>> getAllowedMirrorIds)
    {
        _appDir = appDir;
        _getAllowedMirrorIds = getAllowedMirrorIds;
    }

    /// <summary>Stores a snapshot in the in-memory cache.</summary>
    public void Set(VersionsCacheSnapshot snapshot) => Current = snapshot;

    /// <summary>Clears the in-memory cache, forcing the next read from disk.</summary>
    public void Invalidate() => Current = null;

    #region Path helpers

    /// <summary>Returns the path to the on-disk versions cache file.</summary>
    public string GetSnapshotPath()
        => Path.Combine(_appDir, "Cache", "Game", "versions.json");

    /// <summary>Returns the path to the on-disk patches cache file.</summary>
    public string GetPatchSnapshotPath()
        => Path.Combine(_appDir, "Cache", "Game", "patches.json");

    #endregion

    #region Versions snapshot

    /// <summary>
    /// Checks if a specific branch's data in the cache is fresh (within <paramref name="maxAge"/>).
    /// </summary>
    public bool IsBranchFresh(VersionsCacheSnapshot snapshot, string branch, TimeSpan maxAge)
    {
        // Check per-branch timestamp first (new format)
        if (snapshot.BranchFetchedAt.TryGetValue(branch, out var branchFetchedAt))
        {
            var branchAge = DateTime.UtcNow - branchFetchedAt;
            return branchAge <= maxAge;
        }

        // Fallback to global FetchedAtUtc for old cache format
        // But only if this branch actually has data
        var hasData = (snapshot.Data.Hytale?.Branches.ContainsKey(branch) == true) ||
                      snapshot.Data.Mirrors.Any(m => m.Branches.ContainsKey(branch));
        if (!hasData) return false;

        var globalAge = DateTime.UtcNow - snapshot.FetchedAtUtc;
        return globalAge <= maxAge;
    }

    /// <summary>
    /// Returns the in-memory or on-disk snapshot if it matches <paramref name="osName"/>/<paramref name="arch"/>,
    /// without checking freshness.
    /// </summary>
    public VersionsCacheSnapshot? TryGet(string osName, string arch)
    {
        try
        {
            // Check memory cache first
            if (Current != null)
            {
                if (string.Equals(Current.Os, osName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Current.Arch, arch, StringComparison.OrdinalIgnoreCase))
                {
                    return Current;
                }
            }

            // Load from disk
            var snapshot = Load();
            if (snapshot == null) return null;

            if (!string.Equals(snapshot.Os, osName, StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.Equals(snapshot.Arch, arch, StringComparison.OrdinalIgnoreCase)) return null;

            Current = snapshot;
            return snapshot;
        }
        catch (Exception ex)
        {
            Logger.Warning("Version", $"Failed to load versions cache: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Returns the in-memory or on-disk snapshot only if it matches
    /// <paramref name="osName"/>/<paramref name="arch"/> AND is younger than <paramref name="maxAge"/>.
    /// </summary>
    public VersionsCacheSnapshot? TryGetFresh(string osName, string arch, TimeSpan maxAge)
    {
        try
        {
            // Check memory cache first
            if (Current != null)
            {
                if (!string.Equals(Current.Os, osName, StringComparison.OrdinalIgnoreCase)) return null;
                if (!string.Equals(Current.Arch, arch, StringComparison.OrdinalIgnoreCase)) return null;

                var age = DateTime.UtcNow - Current.FetchedAtUtc;
                if (age <= maxAge)
                    return Current;
            }

            // Load from disk
            var snapshot = Load();
            if (snapshot == null) return null;

            if (!string.Equals(snapshot.Os, osName, StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.Equals(snapshot.Arch, arch, StringComparison.OrdinalIgnoreCase)) return null;

            var diskAge = DateTime.UtcNow - snapshot.FetchedAtUtc;
            if (diskAge > maxAge) return null;

            Current = snapshot;
            return snapshot;
        }
        catch (Exception ex)
        {
            Logger.Warning("Version", $"Failed to load versions cache: {ex.Message}");
        }

        return null;
    }

    /// <summary>Loads the versions snapshot from disk, or returns <c>null</c> if absent or corrupt.</summary>
    public VersionsCacheSnapshot? Load()
    {
        try
        {
            var path = GetSnapshotPath();
            if (!File.Exists(path)) return null;

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return null;

            var snapshot = JsonSerializer.Deserialize<VersionsCacheSnapshot>(json);
            if (snapshot == null) return null;

            return Sanitize(snapshot);
        }
        catch (Exception ex)
        {
            Logger.Warning("Version", $"Failed to deserialize versions cache: {ex.Message}");
            return null;
        }
    }

    /// <summary>Writes the versions snapshot to disk and updates the in-memory copy.</summary>
    public void Save(VersionsCacheSnapshot snapshot)
    {
        try
        {
            snapshot = Sanitize(snapshot);

            var path = GetSnapshotPath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);

            Logger.Debug("Version", $"Saved versions cache to {path}");
        }
        catch (Exception ex)
        {
            Logger.Warning("Version", $"Failed to save versions cache: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes mirror entries from a snapshot whose IDs are no longer registered,
    /// and ensures all nullable collections are initialised.
    /// </summary>
    public VersionsCacheSnapshot Sanitize(VersionsCacheSnapshot snapshot)
    {
        snapshot.Data ??= new VersionsCacheData();
        snapshot.Data.Mirrors ??= new List<MirrorSourceCache>();

        var allowedMirrorIds = _getAllowedMirrorIds()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filtered = snapshot.Data.Mirrors
            .Where(m => !string.IsNullOrWhiteSpace(m.MirrorId) && allowedMirrorIds.Contains(m.MirrorId))
            .GroupBy(m => m.MirrorId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        if (filtered.Count != snapshot.Data.Mirrors.Count)
            Logger.Debug("Version", $"Sanitized mirror cache list: {snapshot.Data.Mirrors.Count} -> {filtered.Count}");

        snapshot.Data.Mirrors = filtered;
        return snapshot;
    }

    #endregion

    #region Patches snapshot

    /// <summary>Loads the patches snapshot from disk, or returns <c>null</c> if absent or corrupt.</summary>
    public PatchesCacheSnapshot? LoadPatches()
    {
        try
        {
            var path = GetPatchSnapshotPath();
            if (!File.Exists(path)) return null;

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return null;

            var snapshot = JsonSerializer.Deserialize<PatchesCacheSnapshot>(json);
            if (snapshot == null) return null;

            // Sanitize mirrors list: remove mirrors that are no longer registered
            snapshot.Data ??= new PatchesCacheData();
            snapshot.Data.Mirrors ??= new List<MirrorPatchCache>();

            var allowedMirrorIds = _getAllowedMirrorIds()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            snapshot.Data.Mirrors = snapshot.Data.Mirrors
                .Where(m => !string.IsNullOrWhiteSpace(m.MirrorId) && allowedMirrorIds.Contains(m.MirrorId))
                .GroupBy(m => m.MirrorId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .ToList();

            return snapshot;
        }
        catch (Exception ex)
        {
            Logger.Warning("Version", $"Failed to deserialize patches cache: {ex.Message}");
            return null;
        }
    }

    /// <summary>Writes the patches snapshot to disk.</summary>
    public void SavePatches(PatchesCacheSnapshot snapshot)
    {
        try
        {
            var path = GetPatchSnapshotPath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);

            var totalSteps = (snapshot.Data.Hytale?.Values.Sum(v => v.Count) ?? 0)
                + snapshot.Data.Mirrors.Sum(m => m.Branches.Values.Sum(v => v.Count));
            Logger.Debug("Version", $"Saved patches cache ({totalSteps} total steps) to {path}");
        }
        catch (Exception ex)
        {
            Logger.Warning("Version", $"Failed to save patches cache: {ex.Message}");
        }
    }

    #endregion
}
