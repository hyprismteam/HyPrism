using System.Text.Json;
using System.Text.RegularExpressions;
using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Core.Integration;

namespace HyPrism.Services.Game.Sources;

/// <summary>
/// Universal IVersionSource implementation driven by a MirrorMeta JSON descriptor.
/// Replaces all hardcoded mirror source classes (EstroGen, CobyLobby, ShipOfYarn, etc.).
/// Supports two source types:
/// <list type="bullet">
///   <item><b>pattern</b>: URL templates + version discovery (json-api / html-autoindex / static-list)</item>
///   <item><b>json-index</b>: Single API endpoint returning a full file index</item>
/// </list>
/// </summary>
public class JsonMirrorSource : IVersionSource
{
    private readonly MirrorMeta _meta;
    private readonly HttpClient _httpClient;

    private readonly SemaphoreSlim _fetchLock = new(1, 1);
    private readonly SemaphoreSlim _speedTestLock = new(1, 1);

    // Cache for version lists: cacheKey → (timestamp, versions)
    private readonly Dictionary<string, (DateTime CachedAt, List<int> Versions)> _versionCache = new();

    // Cache for JSON index API response
    private JsonElement? _cachedJsonIndex;
    private DateTime _jsonIndexCachedAt = DateTime.MinValue;

    private MirrorSpeedTestResult? _speedTestResult;

    private TimeSpan CacheTtl => TimeSpan.FromMinutes(_meta.Cache.IndexTtlMinutes);
    private TimeSpan SpeedTestCacheTtl => TimeSpan.FromMinutes(_meta.Cache.SpeedTestTtlMinutes);

    public JsonMirrorSource(MirrorMeta meta, HttpClient httpClient)
    {
        _meta = meta ?? throw new ArgumentNullException(nameof(meta));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>
    /// Creates an HttpRequestMessage with custom headers from the mirror config.
    /// Expands {hytaleAgent} variable to the official Hytale launcher User-Agent.
    /// </summary>
    private async Task<HttpRequestMessage> CreateRequestWithHeadersAsync(
        HttpMethod method, string url, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(method, url);
        await ApplyCustomHeadersAsync(request, ct);
        return request;
    }

    /// <summary>
    /// Applies custom headers from the mirror config to an HttpRequestMessage.
    /// Expands {hytaleAgent} variable to the official Hytale launcher User-Agent.
    /// </summary>
    private async Task ApplyCustomHeadersAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        if (_meta.Headers == null || _meta.Headers.Count == 0)
            return;

        string? hytaleAgent = null;

        foreach (var (headerName, headerValue) in _meta.Headers)
        {
            var expandedValue = headerValue;

            // Expand {hytaleAgent} variable
            if (expandedValue.Contains("{hytaleAgent}", StringComparison.OrdinalIgnoreCase))
            {
                if (hytaleAgent == null)
                {
                    var launcherVersion = await HytaleLauncherHeaderHelper.GetLauncherVersionAsync(_httpClient, ct);
                    hytaleAgent = $"hytale-launcher/{launcherVersion}";
                }
                expandedValue = expandedValue.Replace("{hytaleAgent}", hytaleAgent, StringComparison.OrdinalIgnoreCase);
            }

            request.Headers.TryAddWithoutValidation(headerName, expandedValue);
        }
    }

    /// <summary>
    /// Sends a GET request with custom headers applied.
    /// </summary>
    private async Task<HttpResponseMessage> GetWithHeadersAsync(string url, CancellationToken ct = default)
    {
        using var request = await CreateRequestWithHeadersAsync(HttpMethod.Get, url, ct);
        return await _httpClient.SendAsync(request, ct);
    }

    /// <summary>
    /// Sends a GET request with custom headers and ResponseHeadersRead option.
    /// </summary>
    private async Task<HttpResponseMessage> GetWithHeadersStreamAsync(string url, CancellationToken ct = default)
    {
        using var request = await CreateRequestWithHeadersAsync(HttpMethod.Get, url, ct);
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    #region IVersionSource Implementation

    /// <inheritdoc/>
    public string SourceId => _meta.Id;

    /// <inheritdoc/>
    public VersionSourceType Type => VersionSourceType.Mirror;

    /// <inheritdoc/>
    public bool IsAvailable => _meta.Enabled;

    /// <inheritdoc/>
    public int Priority => _meta.Priority;

    /// <inheritdoc/>
    public VersionSourceLayoutInfo LayoutInfo => BuildLayoutInfo();

    /// <inheritdoc/>
    public bool IsDiffBasedBranch(string branch)
    {
        var diffBranches = _meta.SourceType == "json-index"
            ? _meta.JsonIndex?.DiffBasedBranches
            : _meta.Pattern?.DiffBasedBranches;

        return diffBranches?.Any(b =>
            b.Equals(branch, StringComparison.OrdinalIgnoreCase)) ?? false;
    }

    /// <inheritdoc/>
    public async Task<List<CachedVersionEntry>> GetVersionsAsync(
        string os, string arch, string branch, CancellationToken ct = default)
    {
        return _meta.SourceType == "json-index"
            ? await GetVersionsFromJsonIndexAsync(os, arch, branch, ct)
            : await GetVersionsFromPatternAsync(os, arch, branch, ct);
    }

    /// <inheritdoc/>
    public async Task<string?> GetDownloadUrlAsync(
        string os, string arch, string branch, int version, CancellationToken ct = default)
    {
        if (_meta.SourceType == "json-index")
        {
            return await GetDownloadUrlFromJsonIndexAsync(os, arch, branch, version, ct);
        }

        // Pattern-based: build URL directly from template
        if (IsDiffBasedBranch(branch))
        {
            // For diff-based branches, full download is only v0~1
            if (version == 1 && _meta.Pattern?.DiffPatchUrl != null)
            {
                return BuildPatternUrl(_meta.Pattern.DiffPatchUrl, os, arch, branch, version, 0, 1);
            }
            return null;
        }

        return BuildPatternUrl(_meta.Pattern!.FullBuildUrl, os, arch, branch, version, 0, version);
    }

    /// <inheritdoc/>
    public async Task<string?> GetDiffUrlAsync(
        string os, string arch, string branch, int fromVersion, int toVersion, CancellationToken ct = default)
    {
        if (_meta.SourceType == "json-index")
        {
            return await GetDiffUrlFromJsonIndexAsync(os, arch, branch, fromVersion, toVersion, ct);
        }

        // Pattern-based: build URL from diff template
        if (_meta.Pattern?.DiffPatchUrl == null) return null;
        return BuildPatternUrl(_meta.Pattern.DiffPatchUrl, os, arch, branch, 0, fromVersion, toVersion);
    }

    /// <inheritdoc/>
    public async Task PreloadAsync(CancellationToken ct = default)
    {
        if (_meta.SourceType == "json-index")
        {
            await FetchJsonIndexAsync(ct);
        }
        else
        {
            var os = UtilityService.GetOS();
            var arch = UtilityService.GetArch();
            await DiscoverVersionsAsync(os, arch, "release", ct);
        }
    }

    /// <inheritdoc/>
    public async Task<List<CachedPatchStep>> GetPatchChainAsync(
        string os, string arch, string branch, CancellationToken ct = default)
    {
        var steps = new List<CachedPatchStep>();

        try
        {
            // Get known versions for this branch
            var versions = await GetVersionsAsync(os, arch, branch, ct);
            if (versions.Count == 0) return steps;

            var sortedVersions = versions.Select(v => v.Version).OrderBy(v => v).ToList();

            // Determine whether this source can produce diff patches for this branch.
            // Pattern-based: diffs available when DiffPatchUrl template is set.
            // Json-index:    diffs always attempted (files may or may not exist in index).
            bool hasDiffSupport = _meta.SourceType == "json-index"
                || _meta.Pattern?.DiffPatchUrl != null;

            if (hasDiffSupport)
            {
                // Build diff chain: v0→v1, v1→v2, ..., v(N-1)→vN
                int prev = 0;
                foreach (var ver in sortedVersions)
                {
                    var url = await GetDiffUrlAsync(os, arch, branch, prev, ver, ct);
                    if (!string.IsNullOrEmpty(url))
                    {
                        steps.Add(new CachedPatchStep { From = prev, To = ver, PwrUrl = url });
                    }
                    prev = ver;
                }
            }

            // If no diff steps were produced (source doesn't support diffs for this branch,
            // or json-index had no patch entries), fall back to full-build entries.
            if (steps.Count == 0)
            {
                foreach (var ver in sortedVersions)
                {
                    var url = await GetDownloadUrlAsync(os, arch, branch, ver, ct);
                    if (!string.IsNullOrEmpty(url))
                    {
                        steps.Add(new CachedPatchStep { From = 0, To = ver, PwrUrl = url });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("JsonMirrorSource", $"GetPatchChainAsync failed for {branch}: {ex.Message}");
        }

        return steps;
    }

    /// <inheritdoc/>
    public MirrorSpeedTestResult? GetCachedSpeedTest()
    {
        if (_speedTestResult == null) return null;
        return DateTime.UtcNow - _speedTestResult.TestedAt <= SpeedTestCacheTtl
            ? _speedTestResult
            : null;
    }

    /// <inheritdoc/>
    public async Task<MirrorSpeedTestResult> TestSpeedAsync(CancellationToken ct = default)
    {
        var cached = GetCachedSpeedTest();
        if (cached != null) return cached;

        await _speedTestLock.WaitAsync(ct);
        try
        {
            cached = GetCachedSpeedTest();
            if (cached != null) return cached;

            var pingUrl = _meta.SpeedTest.PingUrl
                ?? (_meta.SourceType == "json-index" ? _meta.JsonIndex?.ApiUrl : _meta.Pattern?.BaseUrl)
                ?? "";

            var result = new MirrorSpeedTestResult
            {
                MirrorId = SourceId,
                MirrorUrl = pingUrl,
                MirrorName = _meta.Name,
                TestedAt = DateTime.UtcNow
            };

            try
            {
                // Ping test - try HEAD first, fall back to GET if HEAD not supported
                var pingStart = DateTime.UtcNow;
                using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                pingCts.CancelAfter(TimeSpan.FromSeconds(_meta.SpeedTest.PingTimeoutSeconds));

                using var pingReq = await CreateRequestWithHeadersAsync(HttpMethod.Head, pingUrl, pingCts.Token);
                using var pingResp = await _httpClient.SendAsync(pingReq, pingCts.Token);

                result.PingMs = (long)(DateTime.UtcNow - pingStart).TotalMilliseconds;
                
                // Check if HEAD succeeded, or if server returns codes that indicate "API exists but HEAD not supported"
                var headStatusCode = (int)pingResp.StatusCode;
                var headSucceeded = pingResp.IsSuccessStatusCode || 
                    headStatusCode == 405 || // Method Not Allowed - API exists, HEAD not supported  
                    headStatusCode == 400 || // Bad Request - API exists, needs different method
                    headStatusCode == 422;   // Unprocessable Entity - API exists, needs body/params
                
                if (!headSucceeded)
                {
                    // Try GET as fallback
                    var getStart = DateTime.UtcNow;
                    using var getCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    getCts.CancelAfter(TimeSpan.FromSeconds(_meta.SpeedTest.PingTimeoutSeconds));
                    
                    using var getReq = await CreateRequestWithHeadersAsync(HttpMethod.Get, pingUrl, getCts.Token);
                    using var getResp = await _httpClient.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead, getCts.Token);
                    
                    result.PingMs = (long)(DateTime.UtcNow - getStart).TotalMilliseconds;
                    result.IsAvailable = getResp.IsSuccessStatusCode;
                }
                else
                {
                    result.IsAvailable = true;
                }

                if (!result.IsAvailable)
                {
                    Logger.Warning($"Mirror:{SourceId}", $"Speed test ping failed, server not available");
                    _speedTestResult = result;
                    return result;
                }

                Logger.Debug($"Mirror:{SourceId}", $"Ping successful: {result.PingMs}ms, proceeding to speed test");
                
                // Speed test: download real file data
                var os = UtilityService.GetOS();
                var arch = UtilityService.GetArch();
                var testUrl = await GetSpeedTestUrlAsync(os, arch, ct);

                if (!string.IsNullOrEmpty(testUrl))
                {
                    int testSize = _meta.SpeedTest.SpeedTestSizeBytes;
                    var speedStart = DateTime.UtcNow;
                    using var speedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    speedCts.CancelAfter(TimeSpan.FromSeconds(30));

                    using var req = await CreateRequestWithHeadersAsync(HttpMethod.Get, testUrl, speedCts.Token);
                    req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, testSize - 1);

                    using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, speedCts.Token);
                    if (resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.PartialContent)
                    {
                        await using var stream = await resp.Content.ReadAsStreamAsync(speedCts.Token);
                        var buffer = new byte[81920];
                        long totalRead = 0;
                        int bytesRead;
                        while ((bytesRead = await stream.ReadAsync(buffer, speedCts.Token)) > 0)
                        {
                            totalRead += bytesRead;
                            if (totalRead >= testSize) break;
                        }

                        var elapsed = (DateTime.UtcNow - speedStart).TotalSeconds;
                        if (elapsed > 0 && totalRead > 0)
                        {
                            result.SpeedMBps = (totalRead / 1_048_576.0) / elapsed;
                        }
                    }
                }

                Logger.Success($"Mirror:{SourceId}", $"Speed test: {result.PingMs}ms ping, {result.SpeedMBps:F2} MB/s");
            }
            catch (OperationCanceledException)
            {
                result.IsAvailable = false;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Mirror:{SourceId}", $"Speed test failed: {ex.Message}");
                result.IsAvailable = false;
            }

            _speedTestResult = result;
            return result;
        }
        finally
        {
            _speedTestLock.Release();
        }
    }

    #endregion

    #region Pattern-based source methods

    private async Task<List<CachedVersionEntry>> GetVersionsFromPatternAsync(
        string os, string arch, string branch, CancellationToken ct)
    {
        var versions = await DiscoverVersionsAsync(os, arch, branch, ct);
        var config = _meta.Pattern!;

        if (IsDiffBasedBranch(branch) && config.DiffPatchUrl != null)
        {
            // For diff branches, we don't know from~to pairs from version discovery.
            // Return as full downloads from version 0 — caller can then request diffs.
            return versions.Select(v => new CachedVersionEntry
            {
                Version = v,
                FromVersion = 0,
                PwrUrl = BuildPatternUrl(config.FullBuildUrl, os, arch, branch, v, 0, v),
                SigUrl = config.SignatureUrl != null
                    ? BuildPatternUrl(config.SignatureUrl, os, arch, branch, v, 0, v)
                    : null
            }).OrderByDescending(e => e.Version).ToList();
        }

        return versions.Select(v => new CachedVersionEntry
        {
            Version = v,
            FromVersion = 0,
            PwrUrl = BuildPatternUrl(config.FullBuildUrl, os, arch, branch, v, 0, v),
            SigUrl = config.SignatureUrl != null
                ? BuildPatternUrl(config.SignatureUrl, os, arch, branch, v, 0, v)
                : null
        }).OrderByDescending(e => e.Version).ToList();
    }

    /// <summary>
    /// Discovers available versions using the configured method.
    /// </summary>
    private async Task<List<int>> DiscoverVersionsAsync(
        string os, string arch, string branch, CancellationToken ct)
    {
        var config = _meta.Pattern!;
        var discovery = config.VersionDiscovery;

        string cacheKey = $"{os}:{arch}:{branch}";
        if (_versionCache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.CachedAt < CacheTtl)
        {
            return cached.Versions;
        }

        await _fetchLock.WaitAsync(ct);
        try
        {
            if (_versionCache.TryGetValue(cacheKey, out cached) && DateTime.UtcNow - cached.CachedAt < CacheTtl)
            {
                return cached.Versions;
            }

            List<int> versions;

            switch (discovery.Method)
            {
                case "json-api":
                    versions = await DiscoverVersionsJsonApiAsync(os, arch, branch, ct);
                    break;
                case "html-autoindex":
                    versions = await DiscoverVersionsHtmlAsync(os, arch, branch, ct);
                    break;
                case "manifest":
                    versions = await DiscoverVersionsManifestAsync(os, arch, branch, ct);
                    break;
                case "static-list":
                    versions = discovery.StaticVersions?.OrderByDescending(v => v).ToList() ?? new List<int>();
                    break;
                default:
                    Logger.Warning($"Mirror:{SourceId}", $"Unknown discovery method: {discovery.Method}");
                    versions = new List<int>();
                    break;
            }

            if (versions.Count > 0)
            {
                _versionCache[cacheKey] = (DateTime.UtcNow, versions);
                Logger.Success($"Mirror:{SourceId}", $"Discovered {versions.Count} versions for {branch}");
            }

            return versions;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Logger.Warning($"Mirror:{SourceId}", "Version discovery timed out");
            return _versionCache.TryGetValue(cacheKey, out var fb) ? fb.Versions : new List<int>();
        }
        catch (Exception ex)
        {
            Logger.Warning($"Mirror:{SourceId}", $"Version discovery failed: {ex.Message}");
            return _versionCache.TryGetValue(cacheKey, out var fb) ? fb.Versions : new List<int>();
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    private async Task<List<int>> DiscoverVersionsJsonApiAsync(
        string os, string arch, string branch, CancellationToken ct)
    {
        var discovery = _meta.Pattern!.VersionDiscovery;
        if (string.IsNullOrEmpty(discovery.Url)) return new List<int>();

        var url = ApplyPlaceholders(discovery.Url, os, arch, branch, 0, 0, 0);
        Logger.Info($"Mirror:{SourceId}", $"Fetching versions from {url}...");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        var response = await GetWithHeadersAsync(url, cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            Logger.Warning($"Mirror:{SourceId}", $"API returned {response.StatusCode}");
            return new List<int>();
        }

        var json = await response.Content.ReadAsStringAsync(cts.Token);
        
        // Apply placeholders to jsonPath (for paths like "{os}-{arch}.{branch}.newest")
        var resolvedJsonPath = discovery.JsonPath != null 
            ? ApplyPlaceholders(discovery.JsonPath, os, arch, branch, 0, 0, 0)
            : null;
        
        return ParseVersionsFromJson(json, resolvedJsonPath);
    }

    private async Task<List<int>> DiscoverVersionsHtmlAsync(
        string os, string arch, string branch, CancellationToken ct)
    {
        var discovery = _meta.Pattern!.VersionDiscovery;
        if (string.IsNullOrEmpty(discovery.Url)) return new List<int>();

        var url = ApplyPlaceholders(discovery.Url, os, arch, branch, 0, 0, 0);
        Logger.Info($"Mirror:{SourceId}", $"Fetching HTML index from {url}...");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        var response = await GetWithHeadersAsync(url, cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            Logger.Warning($"Mirror:{SourceId}", $"HTML index returned {response.StatusCode}");
            return new List<int>();
        }

        var html = await response.Content.ReadAsStringAsync(cts.Token);
        return ParseVersionsFromHtml(html, discovery.HtmlPattern, discovery.MinFileSizeBytes);
    }

    /// <summary>
    /// Discovers versions from a manifest.json file.
    /// Expects: { "files": { "{os}/{arch}/{branch}/{from}_to_{to}.pwr": { "size": N }, ... } }
    /// </summary>
    private async Task<List<int>> DiscoverVersionsManifestAsync(
        string os, string arch, string branch, CancellationToken ct)
    {
        var discovery = _meta.Pattern!.VersionDiscovery;
        if (string.IsNullOrEmpty(discovery.Url)) return new List<int>();

        var url = discovery.Url; // Manifest URL is absolute, no placeholders
        Logger.Info($"Mirror:{SourceId}", $"Fetching manifest from {url}...");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        var response = await GetWithHeadersAsync(url, cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            Logger.Warning($"Mirror:{SourceId}", $"Manifest returned {response.StatusCode}");
            return new List<int>();
        }

        var json = await response.Content.ReadAsStringAsync(cts.Token);
        return ParseVersionsFromManifest(json, os, arch, branch);
    }

    /// <summary>
    /// Parses version numbers from manifest.json.
    /// File paths: {os}/{arch}/{branch}/{from}_to_{to}.pwr
    /// Returns list of available 'to' versions (targets) for the given os/arch/branch.
    /// </summary>
    private List<int> ParseVersionsFromManifest(string json, string os, string arch, string branch)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("files", out var filesNode) ||
                filesNode.ValueKind != JsonValueKind.Object)
            {
                Logger.Warning($"Mirror:{SourceId}", "Manifest missing 'files' object");
                return new List<int>();
            }

            // Apply OS/arch mappings to match manifest paths
            var mappedOs = ApplyMapping(_meta.Pattern?.OsMapping, os);
            var mappedArch = ApplyMapping(_meta.Pattern?.ArchMapping, arch);

            // Pattern: {os}/{arch}/{branch}/{from}_to_{to}.pwr
            var prefix = $"{mappedOs}/{mappedArch}/{branch}/";
            var patchPattern = new System.Text.RegularExpressions.Regex(
                @"(\d+)_to_(\d+)\.pwr$", 
                System.Text.RegularExpressions.RegexOptions.Compiled);

            var versions = new HashSet<int>();

            foreach (var file in filesNode.EnumerateObject())
            {
                if (!file.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var match = patchPattern.Match(file.Name);
                if (match.Success)
                {
                    // Add target version (to)
                    if (int.TryParse(match.Groups[2].Value, out var toVersion))
                    {
                        versions.Add(toVersion);
                    }
                }
            }

            Logger.Debug($"Mirror:{SourceId}", $"Manifest: found {versions.Count} versions for {mappedOs}/{mappedArch}/{branch}");
            return versions.OrderByDescending(v => v).ToList();
        }
        catch (Exception ex)
        {
            Logger.Warning($"Mirror:{SourceId}", $"Failed to parse manifest: {ex.Message}");
            return new List<int>();
        }
    }

    private static string ApplyMapping(Dictionary<string, string>? mapping, string value)
    {
        if (mapping != null && mapping.TryGetValue(value, out var mapped))
            return mapped;
        return value;
    }

    /// <summary>
    /// Parses version numbers from a JSON response using the configured jsonPath.
    /// Supports:
    /// - "items[].version" - array of objects with a version field
    /// - "versions" - simple property name pointing to an array
    /// - "platform.branch.newest" - dot-notation nested path to a single value
    /// - "$root" or null - root is an array
    /// </summary>
    private List<int> ParseVersionsFromJson(string json, string? jsonPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var versions = new List<int>();

            // "items[].version" — array of objects with a version field
            if (jsonPath != null && jsonPath.Contains("[]."))
            {
                var parts = jsonPath.Split("[].");
                var arrayName = parts[0];
                var fieldName = parts[1];

                JsonElement array;
                if (string.IsNullOrEmpty(arrayName) || arrayName == "$root")
                    array = root;
                else if (!root.TryGetProperty(arrayName, out array) || array.ValueKind != JsonValueKind.Array)
                    return versions;

                foreach (var item in array.EnumerateArray())
                {
                    if (item.TryGetProperty(fieldName, out var val))
                    {
                        if (val.TryGetInt32(out int v)) versions.Add(v);
                        else if (val.ValueKind == JsonValueKind.String && int.TryParse(val.GetString(), out v)) versions.Add(v);
                    }
                }

                return versions.Distinct().OrderByDescending(v => v).ToList();
            }

            // "$root" or null — root is an array
            if (jsonPath == null || jsonPath == "$root")
            {
                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in root.EnumerateArray())
                    {
                        if (el.TryGetInt32(out int v)) versions.Add(v);
                        else if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out v)) versions.Add(v);
                    }
                }
                return versions.Distinct().OrderByDescending(v => v).ToList();
            }

            // Dot-notation nested path: "linux-amd64.release.newest" -> navigate to single value
            if (jsonPath.Contains('.'))
            {
                var pathParts = jsonPath.Split('.');
                JsonElement current = root;
                
                foreach (var part in pathParts)
                {
                    if (current.ValueKind != JsonValueKind.Object)
                    {
                        Logger.Debug($"Mirror:{SourceId}", $"JsonPath '{jsonPath}': expected object at '{part}', got {current.ValueKind}");
                        return versions;
                    }
                    
                    if (!current.TryGetProperty(part, out current))
                    {
                        Logger.Debug($"Mirror:{SourceId}", $"JsonPath '{jsonPath}': property '{part}' not found");
                        return versions;
                    }
                }
                
                // Final element should be a number (single version) or array of numbers
                if (current.ValueKind == JsonValueKind.Number)
                {
                    if (current.TryGetInt32(out int v))
                    {
                        versions.Add(v);
                        Logger.Debug($"Mirror:{SourceId}", $"JsonPath '{jsonPath}': found version {v}");
                    }
                }
                else if (current.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in current.EnumerateArray())
                    {
                        if (el.TryGetInt32(out int v)) versions.Add(v);
                        else if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out v)) versions.Add(v);
                    }
                }
                else if (current.ValueKind == JsonValueKind.String && int.TryParse(current.GetString(), out int sv))
                {
                    versions.Add(sv);
                }
                
                return versions.Distinct().OrderByDescending(v => v).ToList();
            }

            // Simple property name like "versions" or "targets"
            if (!root.TryGetProperty(jsonPath, out var versionsArray) || versionsArray.ValueKind != JsonValueKind.Array)
                return versions;

            foreach (var el in versionsArray.EnumerateArray())
            {
                if (el.TryGetInt32(out int v)) versions.Add(v);
                else if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out v)) versions.Add(v);
            }

            return versions.Distinct().OrderByDescending(v => v).ToList();
        }
        catch (JsonException ex)
        {
            Logger.Warning($"Mirror:{SourceId}", $"Failed to parse JSON: {ex.Message}");
            return new List<int>();
        }
    }

    /// <summary>
    /// Parses version numbers from HTML using the configured regex pattern.
    /// </summary>
    private static List<int> ParseVersionsFromHtml(string html, string? pattern, long minFileSize)
    {
        if (string.IsNullOrEmpty(pattern)) return new List<int>();

        var versions = new List<int>();
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);
        var matches = regex.Matches(html);

        foreach (Match match in matches)
        {
            if (match.Groups.Count < 2) continue;
            if (!int.TryParse(match.Groups[1].Value, out int version)) continue;

            // If the regex has a second capture group (file size), check minimum
            if (minFileSize > 0 && match.Groups.Count > 2
                && long.TryParse(match.Groups[2].Value, out long fileSize)
                && fileSize < minFileSize)
            {
                continue;
            }

            versions.Add(version);
        }

        return versions.Distinct().OrderByDescending(v => v).ToList();
    }

    /// <summary>
    /// Applies placeholder substitution to a URL template.
    /// </summary>
    private string ApplyPlaceholders(string template, string os, string arch, string branch,
        int version, int from, int to)
    {
        var config = _meta.Pattern;
        var mappedOs = config?.OsMapping != null && config.OsMapping.TryGetValue(os, out var mo) ? mo : os;
        var mappedArch = config?.ArchMapping != null && config.ArchMapping.TryGetValue(arch, out var ma) ? ma : arch;
        var mappedBranch = config?.BranchMapping != null && config.BranchMapping.TryGetValue(branch, out var mb) ? mb : branch;

        return template
            .Replace("{base}", config?.BaseUrl ?? "")
            .Replace("{os}", mappedOs)
            .Replace("{arch}", mappedArch)
            .Replace("{branch}", mappedBranch)
            .Replace("{version}", version.ToString())
            .Replace("{from}", from.ToString())
            .Replace("{to}", to.ToString());
    }

    private string BuildPatternUrl(string template, string os, string arch, string branch,
        int version, int from, int to)
    {
        return ApplyPlaceholders(template, os, arch, branch, version, from, to);
    }

    #endregion

    #region JSON-index-based source methods

    private async Task<List<CachedVersionEntry>> GetVersionsFromJsonIndexAsync(
        string os, string arch, string branch, CancellationToken ct)
    {
        var config = _meta.JsonIndex!;
        var entries = new List<CachedVersionEntry>();

        if (IsDiffBasedBranch(branch))
        {
            var patchFiles = await GetIndexFilesAsync(os, branch, "patch", ct);
            foreach (var (fileName, url) in patchFiles)
            {
                if (TryParseDiffFileName(fileName, os, arch, out int fromVer, out int toVer))
                {
                    entries.Add(new CachedVersionEntry
                    {
                        Version = toVer,
                        FromVersion = fromVer,
                        PwrUrl = url
                    });
                }
            }

            return entries.OrderByDescending(e => e.Version).ThenBy(e => e.FromVersion).ToList();
        }

        // Release: use base files (full builds)
        var baseFiles = config.Structure == "grouped"
            ? await GetIndexFilesAsync(os, branch, "base", ct)
            : await GetIndexFilesAsync(os, branch, null, ct);

        foreach (var (fileName, url) in baseFiles)
        {
            if (TryParseBaseFileName(fileName, os, arch, out int version))
            {
                entries.Add(new CachedVersionEntry
                {
                    Version = version,
                    FromVersion = 0,
                    PwrUrl = url
                });
            }
        }

        return entries
            .GroupBy(e => e.Version)
            .Select(g => g.OrderBy(e => e.FromVersion).First())
            .OrderByDescending(e => e.Version)
            .ToList();
    }

    private async Task<string?> GetDownloadUrlFromJsonIndexAsync(
        string os, string arch, string branch, int version, CancellationToken ct)
    {
        var config = _meta.JsonIndex!;

        if (IsDiffBasedBranch(branch))
        {
            if (version != 1) return null;
            var patchFiles = await GetIndexFilesAsync(os, branch, "patch", ct);
            var key = BuildDiffFileNameFromPattern(0, 1, os, arch);
            return patchFiles.TryGetValue(key, out var patchUrl) ? patchUrl : null;
        }

        var baseFiles = config.Structure == "grouped"
            ? await GetIndexFilesAsync(os, branch, "base", ct)
            : await GetIndexFilesAsync(os, branch, null, ct);

        var baseKey = BuildBaseFileNameFromPattern(version, os, arch);
        return baseFiles.TryGetValue(baseKey, out var url) ? url : null;
    }

    private async Task<string?> GetDiffUrlFromJsonIndexAsync(
        string os, string arch, string branch, int fromVersion, int toVersion, CancellationToken ct)
    {
        var files = await GetIndexFilesAsync(os, branch, "patch", ct);
        var key = BuildDiffFileNameFromPattern(fromVersion, toVersion, os, arch);
        return files.TryGetValue(key, out var url) ? url : null;
    }

    /// <summary>
    /// Extracts file entries from the cached JSON index.
    /// </summary>
    private async Task<Dictionary<string, string>> GetIndexFilesAsync(
        string os, string branch, string? group, CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var root = await GetOrFetchJsonIndexAsync(ct);
        if (root == null) return result;

        var config = _meta.JsonIndex!;
        var rootEl = root.Value;

        // Navigate: root → rootPath → branch → platform [→ group]
        if (!rootEl.TryGetProperty(config.RootPath, out var gameNode) || gameNode.ValueKind != JsonValueKind.Object)
            return result;
        if (!gameNode.TryGetProperty(branch, out var branchNode) || branchNode.ValueKind != JsonValueKind.Object)
            return result;

        var platformKey = config.PlatformMapping != null && config.PlatformMapping.TryGetValue(os, out var pk) ? pk : os;
        if (!branchNode.TryGetProperty(platformKey, out var platformNode) || platformNode.ValueKind != JsonValueKind.Object)
            return result;

        JsonElement fileMap;
        if (group != null)
        {
            if (!platformNode.TryGetProperty(group, out fileMap) || fileMap.ValueKind != JsonValueKind.Object)
                return result;
        }
        else
        {
            fileMap = platformNode;
        }

        foreach (var prop in fileMap.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                var url = prop.Value.GetString();
                if (!string.IsNullOrWhiteSpace(url))
                    result[prop.Name] = url;
            }
        }

        return result;
    }

    private async Task<JsonElement?> GetOrFetchJsonIndexAsync(CancellationToken ct)
    {
        if (_cachedJsonIndex.HasValue && DateTime.UtcNow - _jsonIndexCachedAt < CacheTtl)
            return _cachedJsonIndex;
        return await FetchJsonIndexAsync(ct);
    }

    private async Task<JsonElement?> FetchJsonIndexAsync(CancellationToken ct)
    {
        await _fetchLock.WaitAsync(ct);
        try
        {
            if (_cachedJsonIndex.HasValue && DateTime.UtcNow - _jsonIndexCachedAt < CacheTtl)
                return _cachedJsonIndex;

            var apiUrl = _meta.JsonIndex!.ApiUrl;
            Logger.Info($"Mirror:{SourceId}", $"Fetching JSON index from {apiUrl}...");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            var response = await GetWithHeadersAsync(apiUrl, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning($"Mirror:{SourceId}", $"API returned {response.StatusCode}");
                return _cachedJsonIndex;
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(json);
            _cachedJsonIndex = doc.RootElement.Clone();
            _jsonIndexCachedAt = DateTime.UtcNow;

            Logger.Success($"Mirror:{SourceId}", "JSON index loaded");
            return _cachedJsonIndex;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Logger.Warning($"Mirror:{SourceId}", "JSON index request timed out");
            return _cachedJsonIndex;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Mirror:{SourceId}", $"Failed to fetch JSON index: {ex.Message}");
            return _cachedJsonIndex;
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    private bool TryParseBaseFileName(string fileName, string os, string arch, out int version)
    {
        version = 0;
        var fileOs = NormalizeFileOs(os);
        var suffix = $"-{fileOs}-{arch}.pwr";
        if (!fileName.StartsWith("v", StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;

        var numPart = fileName[1..^suffix.Length];
        return int.TryParse(numPart, out version);
    }

    private bool TryParseDiffFileName(string fileName, string os, string arch, out int fromVer, out int toVer)
    {
        fromVer = 0;
        toVer = 0;
        var fileOs = NormalizeFileOs(os);
        var suffix = $"-{fileOs}-{arch}.pwr";
        if (!fileName.StartsWith("v", StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;

        var core = fileName[1..^suffix.Length];
        var tilde = core.IndexOf('~');
        if (tilde <= 0) return false;

        return int.TryParse(core[..tilde], out fromVer) && int.TryParse(core[(tilde + 1)..], out toVer);
    }

    private string BuildBaseFileNameFromPattern(int version, string os, string arch)
        => $"v{version}-{NormalizeFileOs(os)}-{arch}.pwr";

    private string BuildDiffFileNameFromPattern(int from, int to, string os, string arch)
        => $"v{from}~{to}-{NormalizeFileOs(os)}-{arch}.pwr";

    private static string NormalizeFileOs(string os)
        => os.Equals("darwin", StringComparison.OrdinalIgnoreCase) ? "darwin" : os.ToLowerInvariant();

    #endregion

    #region Helpers

    private async Task<string?> GetSpeedTestUrlAsync(string os, string arch, CancellationToken ct)
    {
        try
        {
            var entries = await GetVersionsAsync(os, arch, "pre-release", ct);
            if (entries.Count > 0) return entries[0].PwrUrl;

            entries = await GetVersionsAsync(os, arch, "release", ct);
            if (entries.Count > 0) return entries[0].PwrUrl;
        }
        catch (Exception ex)
        {
            Logger.Debug($"Mirror:{SourceId}", $"Failed to get speed test URL: {ex.Message}");
        }
        return null;
    }

    private VersionSourceLayoutInfo BuildLayoutInfo()
    {
        if (_meta.SourceType == "json-index")
        {
            return new VersionSourceLayoutInfo
            {
                FullBuildLocation = $"JSON Index API: {_meta.JsonIndex?.ApiUrl}",
                PatchLocation = $"JSON Index API groups: base/patch",
                CachePolicy = $"In-memory JSON index cache TTL {_meta.Cache.IndexTtlMinutes}m; speed test cache TTL {_meta.Cache.SpeedTestTtlMinutes}m"
            };
        }

        return new VersionSourceLayoutInfo
        {
            FullBuildLocation = $"Pattern: {_meta.Pattern?.FullBuildUrl}",
            PatchLocation = $"Pattern: {_meta.Pattern?.DiffPatchUrl ?? "N/A"}",
            CachePolicy = $"In-memory version cache TTL {_meta.Cache.IndexTtlMinutes}m; speed test cache TTL {_meta.Cache.SpeedTestTtlMinutes}m"
        };
    }

    #endregion
}
