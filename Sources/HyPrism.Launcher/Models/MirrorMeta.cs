using System.Text.Json.Serialization;

namespace HyPrism.Models;

/// <summary>
/// Root model for a mirror meta JSON file (*.mirror.json).
/// Describes how to discover and download game versions from a community mirror.
/// </summary>
public class MirrorMeta
{
    /// <summary>
    /// Schema version for forward compatibility. Current version: 1.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Unique identifier for this mirror (used as SourceId).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>
    /// Human-readable display name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Optional description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Priority for source ordering (lower = higher priority). Official is 0, mirrors ≥ 100.
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 100;

    /// <summary>
    /// Whether this mirror is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Source type: "pattern" (URL template + version discovery) or "json-index" (single API returning full index).
    /// </summary>
    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = "pattern";

    /// <summary>
    /// Configuration for sourceType "pattern".
    /// </summary>
    [JsonPropertyName("pattern")]
    public MirrorPatternConfig? Pattern { get; set; }

    /// <summary>
    /// Configuration for sourceType "json-index".
    /// </summary>
    [JsonPropertyName("jsonIndex")]
    public MirrorJsonIndexConfig? JsonIndex { get; set; }

    /// <summary>
    /// Speed test configuration.
    /// </summary>
    [JsonPropertyName("speedTest")]
    public MirrorSpeedTestConfig SpeedTest { get; set; } = new();

    /// <summary>
    /// Cache configuration.
    /// </summary>
    [JsonPropertyName("cache")]
    public MirrorCacheConfig Cache { get; set; } = new();

    /// <summary>
    /// Custom HTTP headers to send with requests to this mirror.
    /// Format in UI: header=value or header="value with spaces"
    /// Supports variables: {hytaleAgent} - official Hytale launcher User-Agent
    /// </summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }
}

/// <summary>
/// Configuration for pattern-based mirrors where URLs are built from templates.
/// </summary>
public class MirrorPatternConfig
{
    /// <summary>
    /// URL template for full build downloads.
    /// Placeholders: {base}, {os}, {arch}, {branch}, {version}, {from}, {to}
    /// </summary>
    [JsonPropertyName("fullBuildUrl")]
    public string FullBuildUrl { get; set; } = "{base}/{os}/{arch}/{branch}/0/{version}.pwr";

    /// <summary>
    /// URL template for diff patches.
    /// </summary>
    [JsonPropertyName("diffPatchUrl")]
    public string? DiffPatchUrl { get; set; }

    /// <summary>
    /// URL template for signature files (optional).
    /// </summary>
    [JsonPropertyName("signatureUrl")]
    public string? SignatureUrl { get; set; }

    /// <summary>
    /// Base URL substituted into {base} placeholder.
    /// </summary>
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// How to discover available versions.
    /// </summary>
    [JsonPropertyName("versionDiscovery")]
    public VersionDiscoveryConfig VersionDiscovery { get; set; } = new();

    /// <summary>
    /// Maps internal OS names to URL OS names. Only include overrides.
    /// </summary>
    [JsonPropertyName("osMapping")]
    public Dictionary<string, string>? OsMapping { get; set; }

    /// <summary>
    /// Maps internal arch names to URL arch names. Only include overrides.
    /// e.g. { "x64": "amd64" } to convert x64 to amd64 in URLs.
    /// </summary>
    [JsonPropertyName("archMapping")]
    public Dictionary<string, string>? ArchMapping { get; set; }

    /// <summary>
    /// Maps internal branch names to URL branch names. Only include overrides.
    /// </summary>
    [JsonPropertyName("branchMapping")]
    public Dictionary<string, string>? BranchMapping { get; set; }

    /// <summary>
    /// List of branches that use diff-based patching (e.g. ["pre-release"]).
    /// </summary>
    [JsonPropertyName("diffBasedBranches")]
    public List<string> DiffBasedBranches { get; set; } = new();
}

/// <summary>
/// How to discover available versions for a pattern-based mirror.
/// </summary>
public class VersionDiscoveryConfig
{
    /// <summary>
    /// Discovery method: "json-api", "html-autoindex", or "static-list".
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = "json-api";

    /// <summary>
    /// URL for version listing. Supports {base}, {os}, {arch}, {branch} placeholders.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// For json-api: path to the versions array in the JSON response.
    /// Supported formats: "items[].version", "versions", "$root"
    /// </summary>
    [JsonPropertyName("jsonPath")]
    public string? JsonPath { get; set; }

    /// <summary>
    /// For html-autoindex: regex pattern for extracting versions.
    /// Must have capture group 1 = version number. Group 2 = file size (optional).
    /// </summary>
    [JsonPropertyName("htmlPattern")]
    public string? HtmlPattern { get; set; }

    /// <summary>
    /// Minimum file size in bytes for html-autoindex filtering.
    /// </summary>
    [JsonPropertyName("minFileSizeBytes")]
    public long MinFileSizeBytes { get; set; } = 0;

    /// <summary>
    /// For static-list: explicit list of version numbers.
    /// </summary>
    [JsonPropertyName("staticVersions")]
    public List<int>? StaticVersions { get; set; }
}

/// <summary>
/// Configuration for JSON-index-based mirrors (single API returning full file index).
/// </summary>
public class MirrorJsonIndexConfig
{
    /// <summary>
    /// URL of the API that returns the full index.
    /// </summary>
    [JsonPropertyName("apiUrl")]
    public string ApiUrl { get; set; } = "";

    /// <summary>
    /// Root property name in the JSON response (e.g. "hytale").
    /// </summary>
    [JsonPropertyName("rootPath")]
    public string RootPath { get; set; } = "hytale";

    /// <summary>
    /// Index structure: "flat" (branch → platform → file→url) or "grouped" (branch → platform → base/patch → file→url).
    /// </summary>
    [JsonPropertyName("structure")]
    public string Structure { get; set; } = "flat";

    /// <summary>
    /// Maps internal OS names to JSON platform keys. Only include overrides (e.g. {"darwin": "mac"}).
    /// </summary>
    [JsonPropertyName("platformMapping")]
    public Dictionary<string, string>? PlatformMapping { get; set; }

    /// <summary>
    /// Patterns for parsing file names in the index.
    /// </summary>
    [JsonPropertyName("fileNamePattern")]
    public FileNamePatternConfig FileNamePattern { get; set; } = new();

    /// <summary>
    /// List of branches that use diff-based patching.
    /// </summary>
    [JsonPropertyName("diffBasedBranches")]
    public List<string> DiffBasedBranches { get; set; } = new();
}

/// <summary>
/// File name pattern configuration for JSON index mirrors.
/// </summary>
public class FileNamePatternConfig
{
    /// <summary>
    /// Pattern for full build file names. Default: "v{version}-{os}-{arch}.pwr"
    /// </summary>
    [JsonPropertyName("full")]
    public string Full { get; set; } = "v{version}-{os}-{arch}.pwr";

    /// <summary>
    /// Pattern for diff patch file names. Default: "v{from}~{to}-{os}-{arch}.pwr"
    /// </summary>
    [JsonPropertyName("diff")]
    public string Diff { get; set; } = "v{from}~{to}-{os}-{arch}.pwr";
}

/// <summary>
/// Speed test configuration for a mirror.
/// </summary>
public class MirrorSpeedTestConfig
{
    /// <summary>
    /// URL for ping/availability check (HEAD request).
    /// </summary>
    [JsonPropertyName("pingUrl")]
    public string? PingUrl { get; set; }

    /// <summary>
    /// Ping timeout in seconds.
    /// </summary>
    [JsonPropertyName("pingTimeoutSeconds")]
    public int PingTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Size of data to download for speed test (bytes). Default: 10 MB.
    /// </summary>
    [JsonPropertyName("speedTestSizeBytes")]
    public int SpeedTestSizeBytes { get; set; } = 10 * 1024 * 1024;
}

/// <summary>
/// Cache TTL configuration for a mirror.
/// </summary>
public class MirrorCacheConfig
{
    /// <summary>
    /// TTL for version index cache in minutes.
    /// </summary>
    [JsonPropertyName("indexTtlMinutes")]
    public int IndexTtlMinutes { get; set; } = 30;

    /// <summary>
    /// TTL for speed test cache in minutes.
    /// </summary>
    [JsonPropertyName("speedTestTtlMinutes")]
    public int SpeedTestTtlMinutes { get; set; } = 60;
}
