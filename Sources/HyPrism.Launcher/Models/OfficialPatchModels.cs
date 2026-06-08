using System.Text.Json.Serialization;

namespace HyPrism.Models;

/// <summary>
/// Response from the official Hytale patches API.
/// Endpoint: https://account-data.hytale.com/patches/{os}/{arch}/{channel}/{from_build}
/// </summary>
public class OfficialPatchesResponse
{
    /// <summary>
    /// Array of patch steps to reach the latest version.
    /// Each step represents a transition from one build to another.
    /// </summary>
    [JsonPropertyName("steps")]
    public List<OfficialPatchStep> Steps { get; set; } = new();
}

/// <summary>
/// A single patch step in the official update chain.
/// </summary>
public class OfficialPatchStep
{
    /// <summary>
    /// The build number this patch starts from.
    /// </summary>
    [JsonPropertyName("from")]
    public int From { get; set; }

    /// <summary>
    /// The build number this patch updates to.
    /// </summary>
    [JsonPropertyName("to")]
    public int To { get; set; }

    /// <summary>
    /// URL to the PWR (patcher) file for this step.
    /// Contains a signed verification token.
    /// </summary>
    [JsonPropertyName("pwr")]
    public string Pwr { get; set; } = "";

    /// <summary>
    /// URL to the PWR HEAD (metadata) for this step.
    /// Usually the same as Pwr for full downloads.
    /// </summary>
    [JsonPropertyName("pwrHead")]
    public string PwrHead { get; set; } = "";

    /// <summary>
    /// URL to the signature file for verification.
    /// </summary>
    [JsonPropertyName("sig")]
    public string Sig { get; set; } = "";
}

/// <summary>
/// Represents version information with its source.
/// </summary>
public class VersionInfo
{
    /// <summary>
    /// The version number.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// The source of this version: "official" or "mirror".
    /// </summary>
    public VersionSource Source { get; set; } = VersionSource.Mirror;

    /// <summary>
    /// Whether this is the latest available version for the branch.
    /// </summary>
    public bool IsLatest { get; set; }
}

/// <summary>
/// Indicates where a version was sourced from.
/// </summary>
public enum VersionSource
{
    /// <summary>
    /// Version obtained from the official Hytale patch server (requires authenticated account).
    /// </summary>
    Official,

    /// <summary>
    /// Version obtained from a community mirror.
    /// </summary>
    Mirror
}

/// <summary>
/// Extended version list response including source information.
/// </summary>
public class VersionListResponse
{
    /// <summary>
    /// List of available versions with their sources.
    /// </summary>
    public List<VersionInfo> Versions { get; set; } = new();

    /// <summary>
    /// Whether an official Hytale account is available.
    /// </summary>
    public bool HasOfficialAccount { get; set; }

    /// <summary>
    /// Whether versions from the official source are available.
    /// </summary>
    public bool OfficialSourceAvailable { get; set; }

    /// <summary>
    /// Whether there are any download sources available (official or mirrors).
    /// </summary>
    public bool HasDownloadSources { get; set; }

    /// <summary>
    /// Number of currently enabled mirror sources.
    /// </summary>
    public int EnabledMirrorCount { get; set; }
}

/// <summary>
/// Cache entry for a single version from a specific source.
/// Contains download URLs and metadata.
/// </summary>
public class CachedVersionEntry
{
    /// <summary>
    /// The version number.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// URL to the PWR file for full download/patch.
    /// </summary>
    public string PwrUrl { get; set; } = "";

    /// <summary>
    /// URL to the PWR HEAD file (for official sources).
    /// </summary>
    public string? PwrHeadUrl { get; set; }

    /// <summary>
    /// URL to the signature file (for official sources).
    /// </summary>
    public string? SigUrl { get; set; }

    /// <summary>
    /// The starting version this patch applies from (0 for full download).
    /// </summary>
    public int FromVersion { get; set; }
}

/// <summary>
/// Cache data for the official Hytale source.
/// </summary>
public class OfficialSourceCache
{
    /// <summary>
    /// Versions available from official Hytale servers, keyed by branch.
    /// </summary>
    public Dictionary<string, List<CachedVersionEntry>> Branches { get; set; } = new();
}

/// <summary>
/// Cache data for a single mirror source.
/// </summary>
public class MirrorSourceCache
{
    /// <summary>
    /// Mirror identifier/name.
    /// </summary>
    public string MirrorId { get; set; } = "default";

    /// <summary>
    /// Versions available from this mirror, keyed by branch.
    /// </summary>
    public Dictionary<string, List<CachedVersionEntry>> Branches { get; set; } = new();
}

/// <summary>
/// Combined version cache with data from all sources.
/// </summary>
public class VersionsCacheSnapshot
{
    /// <summary>
    /// When the cache was last updated (legacy, kept for backwards compatibility).
    /// </summary>
    public DateTime FetchedAtUtc { get; set; }

    /// <summary>
    /// Per-branch fetch timestamps. Key is normalized branch name (release, pre-release, alpha).
    /// </summary>
    public Dictionary<string, DateTime> BranchFetchedAt { get; set; } = new();

    /// <summary>
    /// Operating system this cache is for.
    /// </summary>
    public string Os { get; set; } = "";

    /// <summary>
    /// Architecture this cache is for.
    /// </summary>
    public string Arch { get; set; } = "";

    /// <summary>
    /// Combined version cache data.
    /// </summary>
    public VersionsCacheData Data { get; set; } = new();
}

/// <summary>
/// Container for version data from different sources.
/// </summary>
public class VersionsCacheData
{
    /// <summary>
    /// Versions from official Hytale servers (requires authenticated account).
    /// </summary>
    public OfficialSourceCache? Hytale { get; set; }

    /// <summary>
    /// Versions from community mirrors.
    /// </summary>
    public List<MirrorSourceCache> Mirrors { get; set; } = new();
}

/// <summary>
/// Combined patch cache with data from all sources (official + mirrors).
/// Mirrors the multi-source architecture of <see cref="VersionsCacheSnapshot"/>.
/// </summary>
public class PatchesCacheSnapshot
{
    /// <summary>
    /// When the cache was last updated.
    /// </summary>
    public DateTime FetchedAtUtc { get; set; }

    /// <summary>
    /// Operating system this cache is for.
    /// </summary>
    public string Os { get; set; } = "";

    /// <summary>
    /// Architecture this cache is for.
    /// </summary>
    public string Arch { get; set; } = "";

    /// <summary>
    /// Combined patch cache data from all sources.
    /// </summary>
    public PatchesCacheData Data { get; set; } = new();
}

/// <summary>
/// Container for patch data from different sources.
/// </summary>
public class PatchesCacheData
{
    /// <summary>
    /// Patch chains from official Hytale servers (signed, expiring URLs).
    /// Keyed by branch.
    /// </summary>
    public Dictionary<string, List<CachedPatchStep>>? Hytale { get; set; }

    /// <summary>
    /// Patch chains from community mirrors.
    /// </summary>
    public List<MirrorPatchCache> Mirrors { get; set; } = new();
}

/// <summary>
/// Patch cache for a single mirror source.
/// </summary>
public class MirrorPatchCache
{
    /// <summary>
    /// Mirror identifier matching the source ID.
    /// </summary>
    public string MirrorId { get; set; } = "";

    /// <summary>
    /// Patch chains from this mirror, keyed by branch.
    /// </summary>
    public Dictionary<string, List<CachedPatchStep>> Branches { get; set; } = new();
}

/// <summary>
/// A cached patch step for updating between versions.
/// </summary>
public class CachedPatchStep
{
    /// <summary>
    /// The version this patch updates FROM.
    /// </summary>
    public int From { get; set; }

    /// <summary>
    /// The version this patch updates TO.
    /// </summary>
    public int To { get; set; }

    /// <summary>
    /// URL to the PWR file (may contain expiring signature for official source).
    /// </summary>
    public string PwrUrl { get; set; } = "";

    /// <summary>
    /// URL to the PWR HEAD file.
    /// </summary>
    public string? PwrHeadUrl { get; set; }

    /// <summary>
    /// URL to the signature file.
    /// </summary>
    public string? SigUrl { get; set; }
}
