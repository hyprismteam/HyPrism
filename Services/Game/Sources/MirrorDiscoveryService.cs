using System.Text.Json;
using System.Text.RegularExpressions;
using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Core.Integration;

namespace HyPrism.Services.Game.Sources;

/// <summary>
/// Service for automatically discovering mirror configuration from a URL.
/// Attempts to detect the mirror type (pattern/json-index) and build a MirrorMeta schema.
/// </summary>
public class MirrorDiscoveryService : IMirrorDiscoveryService
{
    private readonly HttpClient _httpClient;
    private const int TimeoutSeconds = 10;
    
    // Custom headers to use for all discovery requests
    private Dictionary<string, string>? _customHeaders;
    private string? _hytaleAgent;

    public MirrorDiscoveryService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>
    /// Sends a GET request with custom headers applied.
    /// Expands {hytaleAgent} variable to the official Hytale launcher User-Agent.
    /// </summary>
    private async Task<HttpResponseMessage> GetWithHeadersAsync(string url, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        
        if (_customHeaders != null && _customHeaders.Count > 0)
        {
            foreach (var (headerName, headerValue) in _customHeaders)
            {
                var expandedValue = headerValue;
                
                // Expand {hytaleAgent} variable
                if (expandedValue.Contains("{hytaleAgent}", StringComparison.OrdinalIgnoreCase))
                {
                    if (_hytaleAgent == null)
                    {
                        var launcherVersion = await HytaleLauncherHeaderHelper.GetLauncherVersionAsync(_httpClient, ct);
                        _hytaleAgent = $"hytale-launcher/{launcherVersion}";
                    }
                    expandedValue = expandedValue.Replace("{hytaleAgent}", _hytaleAgent, StringComparison.OrdinalIgnoreCase);
                }
                
                request.Headers.TryAddWithoutValidation(headerName, expandedValue);
            }
        }
        
        return await _httpClient.SendAsync(request, ct);
    }

    /// <summary>
    /// Attempts to discover mirror configuration from a URL.
    /// Tries multiple detection strategies with extensive endpoint probing.
    /// </summary>
    /// <param name="url">The mirror URL to discover</param>
    /// <param name="headers">Optional custom headers to use for discovery requests (supports {hytaleAgent} variable)</param>
    /// <param name="ct">Cancellation token</param>
    public async Task<DiscoveryResult> DiscoverMirrorAsync(string url, Dictionary<string, string>? headers = null, CancellationToken ct = default)
    {
        // Store headers for use in all discovery requests
        _customHeaders = headers;
        _hytaleAgent = null; // Reset cached agent for fresh expansion
        
        if (string.IsNullOrWhiteSpace(url))
        {
            return new DiscoveryResult { Success = false, Error = "URL is required" };
        }

        // Normalize URL
        url = url.Trim().TrimEnd('/');
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return new DiscoveryResult { Success = false, Error = "Invalid URL format" };
        }

        Logger.Info("MirrorDiscovery", $"Starting discovery for: {url}");

        // Generate possible base URLs to test
        var baseUrls = GeneratePossibleBaseUrls(uri);
        
        foreach (var baseUrl in baseUrls)
        {
            Logger.Debug("MirrorDiscovery", $"Testing base URL: {baseUrl}");
            
            // Try all strategies for this base URL
            var result = await TryAllStrategiesAsync(new Uri(baseUrl), ct);
            if (result.Success && result.Mirror != null)
            {
                Logger.Success("MirrorDiscovery", $"Discovery succeeded: {result.Mirror.Name} ({result.DetectedType})");
                return result;
            }
        }

        Logger.Warning("MirrorDiscovery", "All discovery strategies failed");
        return new DiscoveryResult 
        { 
            Success = false, 
            Error = "Could not automatically detect mirror configuration. Please add a .mirror.json file manually." 
        };
    }

    /// <summary>
    /// Generate possible base URLs from the input URL.
    /// For example, if user enters "https://example.com/hytale", we also try "https://example.com".
    /// </summary>
    private static List<string> GeneratePossibleBaseUrls(Uri uri)
    {
        var urls = new List<string> { uri.ToString().TrimEnd('/') };
        
        // Add authority (root domain) as fallback
        var authority = uri.GetLeftPart(UriPartial.Authority);
        if (!urls.Contains(authority))
        {
            urls.Add(authority);
        }
        
        // If path has multiple segments, try parent paths
        var pathParts = uri.AbsolutePath.Trim('/').Split('/');
        for (int i = pathParts.Length - 1; i > 0; i--)
        {
            var parentPath = string.Join("/", pathParts.Take(i));
            var parentUrl = $"{authority}/{parentPath}";
            if (!urls.Contains(parentUrl))
            {
                urls.Add(parentUrl);
            }
        }
        
        return urls;
    }

    private async Task<DiscoveryResult> TryAllStrategiesAsync(Uri baseUri, CancellationToken ct)
    {
        // Try strategies in order of specificity
        // HTML autoindex and static files checked FIRST as they look for actual .pwr files
        // JSON APIs checked after as they may return false positives on error pages
        var strategies = new (string Name, Func<Uri, CancellationToken, Task<DiscoveryResult>> Strategy)[]
        {
            ("Pattern: Infos API", TryInfosApiPatternAsync),
            ("Pattern: Manifest JSON", TryManifestDiscoveryAsync),
            ("HTML Autoindex", TryHtmlAutoindexDiscoveryAsync),
            ("Pattern: Static Files", TryStaticFilesPatternAsync),
            ("JSON Index API", TryJsonIndexDiscoveryAsync),
            ("JSON Version API", TryJsonApiDiscoveryAsync),
            ("Pattern: Launcher API", TryLauncherApiPatternAsync),
            ("Directory Pattern", TryKnownPatternDiscoveryAsync)
        };

        foreach (var (name, strategy) in strategies)
        {
            try
            {
                Logger.Debug("MirrorDiscovery", $"Trying strategy: {name}");
                var result = await strategy(baseUri, ct);
                if (result.Success && result.Mirror != null)
                {
                    Logger.Debug("MirrorDiscovery", $"Strategy '{name}' succeeded");
                    return result;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Logger.Debug("MirrorDiscovery", $"Strategy '{name}' failed: {ex.Message}");
            }
        }

        return new DiscoveryResult { Success = false };
    }

    /// <summary>
    /// Try detection for mirrors using /infos endpoint pattern.
    /// Pattern: /infos for version info, /latest for patch steps, /dl/:os/:arch/:version.pwr for downloads.
    /// </summary>
    private async Task<DiscoveryResult> TryInfosApiPatternAsync(Uri baseUri, CancellationToken ct)
    {
        // Test /infos endpoint - this is the main version discovery endpoint
        var infosUrl = new Uri(baseUri, "/infos").ToString();
        Logger.Debug("MirrorDiscovery", $"Testing /infos endpoint: {infosUrl}");
        
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
            
            using var response = await GetWithHeadersAsync(infosUrl, linkedCts.Token);
            
            if (!response.IsSuccessStatusCode)
            {
                Logger.Debug("MirrorDiscovery", $"/infos returned {response.StatusCode}");
                return new DiscoveryResult { Success = false };
            }

            // Check Content-Type - must be JSON
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Debug("MirrorDiscovery", $"/infos returned non-JSON Content-Type: {contentType}");
                return new DiscoveryResult { Success = false };
            }

            var content = await response.Content.ReadAsStringAsync(linkedCts.Token);
            if (string.IsNullOrWhiteSpace(content))
            {
                Logger.Debug("MirrorDiscovery", "/infos returned empty content");
                return new DiscoveryResult { Success = false };
            }

            // Try to parse as JSON
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            // Infos API format: { "platform": { "branch": { "buildVersion": "...", "newest": N } } }
            // e.g. { "linux-amd64": { "release": { "buildVersion": "2026.02.17-xxx", "newest": 9 } } }
            var validPlatforms = new[] { "windows-amd64", "linux-amd64", "darwin-arm64" };
            var detectedPlatforms = new List<string>();
            
            foreach (var platform in validPlatforms)
            {
                if (root.TryGetProperty(platform, out var platformData) && 
                    platformData.ValueKind == JsonValueKind.Object)
                {
                    // Check for release or pre-release branch
                    if (platformData.TryGetProperty("release", out var releaseData) ||
                        platformData.TryGetProperty("pre-release", out _))
                    {
                        // Verify it has buildVersion and newest fields
                        if (releaseData.TryGetProperty("buildVersion", out _) ||
                            releaseData.TryGetProperty("newest", out _))
                        {
                            detectedPlatforms.Add(platform);
                        }
                    }
                }
            }

            if (detectedPlatforms.Count == 0)
            {
                Logger.Debug("MirrorDiscovery", "/infos response doesn't match expected format");
                return new DiscoveryResult { Success = false };
            }

            Logger.Debug("MirrorDiscovery", $"Infos API detected! Platforms: {string.Join(", ", detectedPlatforms)}");

            // Optionally verify /latest endpoint exists
            var latestUrl = new Uri(baseUri, "/latest?branch=release&version=0").ToString();
            try
            {
                using var latestCts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
                using var latestLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, latestCts.Token);
                using var latestResponse = await GetWithHeadersAsync(latestUrl, latestLinkedCts.Token);
                Logger.Debug("MirrorDiscovery", $"/latest endpoint: {latestResponse.StatusCode}");
            }
            catch (Exception ex)
            {
                Logger.Debug("MirrorDiscovery", $"/latest check failed (non-critical): {ex.Message}");
            }

            var mirrorId = MirrorSchemaInferrer.GenerateMirrorId(baseUri);
            var mirror = MirrorSchemaInferrer.CreateInfosApiPatternMirror(baseUri, mirrorId);

            return new DiscoveryResult
            {
                Success = true,
                Mirror = mirror,
                DetectedType = "Pattern: Infos API"
            };
        }
        catch (JsonException je)
        {
            Logger.Debug("MirrorDiscovery", $"/infos JSON parse failed: {je.Message}");
        }
        catch (Exception ex)
        {
            Logger.Debug("MirrorDiscovery", $"Infos API detection failed: {ex.Message}");
        }

        return new DiscoveryResult { Success = false };
    }

    /// <summary>
    /// Try detection for mirrors using manifest.json format.
    /// Manifest contains files object with paths like: {os}/{arch}/{branch}/{from}_to_{to}.pwr
    /// </summary>
    private async Task<DiscoveryResult> TryManifestDiscoveryAsync(Uri baseUri, CancellationToken ct)
    {
        // Build manifest URLs relative to the provided baseUri
        var inputUrl = baseUri.ToString().TrimEnd('/');
        
        // Try manifest.json at various locations relative to the provided URL
        var manifestUrls = new List<string>
        {
            // If URL ends with manifest.json, use it directly
            inputUrl.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase) 
                ? inputUrl 
                : null!,
            // Try manifest.json in current directory
            $"{inputUrl}/manifest.json",
            // Try in patches subdirectory
            $"{inputUrl}/patches/manifest.json",
            // Try in hytale/patches subdirectory  
            $"{inputUrl}/hytale/patches/manifest.json"
        };
        
        // Remove nulls and duplicates
        manifestUrls = manifestUrls.Where(u => u != null).Distinct().ToList();

        foreach (var manifestUrl in manifestUrls)
        {
            Logger.Debug("MirrorDiscovery", $"Testing manifest endpoint: {manifestUrl}");

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);

                using var response = await GetWithHeadersAsync(manifestUrl, linkedCts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    Logger.Debug("MirrorDiscovery", $"Manifest returned {response.StatusCode}");
                    continue;
                }

                // Check Content-Type
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Debug("MirrorDiscovery", $"Manifest returned non-JSON: {contentType}");
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync(linkedCts.Token);
                if (string.IsNullOrWhiteSpace(content)) continue;

                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                // Must have "files" property
                if (!root.TryGetProperty("files", out var filesNode) ||
                    filesNode.ValueKind != JsonValueKind.Object)
                {
                    Logger.Debug("MirrorDiscovery", "Manifest missing 'files' object");
                    continue;
                }

                // Parse files to detect structure: {os}/{arch}/{branch}/{from}_to_{to}.pwr
                var patchPattern = new Regex(@"^([^/]+)/([^/]+)/([^/]+)/(\d+)_to_(\d+)\.pwr$", RegexOptions.Compiled);
                var detectedPlatforms = new HashSet<string>();
                var detectedBranches = new HashSet<string>();
                var maxVersions = new Dictionary<string, int>();

                foreach (var file in filesNode.EnumerateObject())
                {
                    var match = patchPattern.Match(file.Name);
                    if (match.Success)
                    {
                        var os = match.Groups[1].Value;
                        var arch = match.Groups[2].Value;
                        var branch = match.Groups[3].Value;
                        var toVersion = int.Parse(match.Groups[5].Value);

                        detectedPlatforms.Add($"{os}/{arch}");
                        detectedBranches.Add(branch);

                        var key = $"{os}/{arch}/{branch}";
                        if (!maxVersions.TryGetValue(key, out var current) || toVersion > current)
                        {
                            maxVersions[key] = toVersion;
                        }
                    }
                }

                if (detectedPlatforms.Count == 0)
                {
                    Logger.Debug("MirrorDiscovery", "No valid patch files found in manifest");
                    continue;
                }

                Logger.Debug("MirrorDiscovery", $"Manifest detected! Platforms: {string.Join(", ", detectedPlatforms)}, Branches: {string.Join(", ", detectedBranches)}");

                // Build base URL (directory containing manifest.json)
                var baseUrl = manifestUrl.Substring(0, manifestUrl.LastIndexOf('/'));

                var mirrorId = MirrorSchemaInferrer.GenerateMirrorId(baseUri);
                var mirror = MirrorSchemaInferrer.CreateManifestPatternMirror(baseUri, mirrorId, baseUrl, manifestUrl, detectedBranches);

                return new DiscoveryResult
                {
                    Success = true,
                    Mirror = mirror,
                    DetectedType = "Pattern: Manifest"
                };
            }
            catch (JsonException je)
            {
                Logger.Debug("MirrorDiscovery", $"Manifest JSON parse error: {je.Message}");
            }
            catch (Exception ex)
            {
                Logger.Debug("MirrorDiscovery", $"Manifest check failed: {ex.Message}");
            }
        }

        return new DiscoveryResult { Success = false };
    }

    /// <summary>
    /// Try detection for mirrors using /launcher/patches API pattern.
    /// Pattern: /launcher/patches/{branch}/versions?os_name={os}&arch={arch}
    /// Note: The API may return 422 UnprocessableEntity if parameters are wrong,
    /// but this still means the endpoint exists!
    /// </summary>
    private async Task<DiscoveryResult> TryLauncherApiPatternAsync(Uri baseUri, CancellationToken ct)
    {
        // First check if /health endpoint works
        var healthUrl = new Uri(baseUri, "/health").ToString();
        bool hasHealthEndpoint = false;
        
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
            using var healthResponse = await GetWithHeadersAsync(healthUrl, linkedCts.Token);
            hasHealthEndpoint = healthResponse.IsSuccessStatusCode;
            Logger.Debug("MirrorDiscovery", $"/health endpoint: {(hasHealthEndpoint ? "OK" : healthResponse.StatusCode.ToString())}");
        }
        catch (Exception ex)
        {
            Logger.Debug("MirrorDiscovery", $"/health check failed: {ex.Message}");
        }

        // Test launcher API version endpoints
        // 422 UnprocessableEntity means the endpoint EXISTS but params are wrong - still a valid detection!
        var versionEndpoints = new[]
        {
            "/launcher/patches/release/versions?os_name=linux&arch=x64",
            "/launcher/patches/release/versions?os_name=linux&arch=amd64",
            "/launcher/patches/prerelease/versions?os_name=linux&arch=x64",
            "/launcher/patches/release/versions",
        };

        foreach (var endpoint in versionEndpoints)
        {
            var testUrl = new Uri(baseUri, endpoint).ToString();
            Logger.Debug("MirrorDiscovery", $"Testing launcher API endpoint: {testUrl}");
            
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
                
                using var response = await GetWithHeadersAsync(testUrl, linkedCts.Token);
                var statusCode = (int)response.StatusCode;
                
                // Check Content-Type - must be JSON, not HTML error pages
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                var isJsonResponse = contentType.Contains("json", StringComparison.OrdinalIgnoreCase);
                
                // For 422/400 we need JSON response to confirm it's a real API
                // For 200 OK we also need JSON to avoid HTML error pages
                if (statusCode == 422 || statusCode == 400 || response.IsSuccessStatusCode)
                {
                    if (!isJsonResponse)
                    {
                        Logger.Debug("MirrorDiscovery", $"Endpoint returned {response.StatusCode} but Content-Type is not JSON: {contentType}");
                        continue;
                    }
                    
                    Logger.Debug("MirrorDiscovery", $"Launcher API detected! Status: {response.StatusCode}");
                    
                    var mirrorId = MirrorSchemaInferrer.GenerateMirrorId(baseUri);
                    var mirror = MirrorSchemaInferrer.CreateLauncherApiPatternMirror(baseUri, mirrorId);
                    
                    return new DiscoveryResult
                    {
                        Success = true,
                        Mirror = mirror,
                        DetectedType = "Pattern: Launcher API"
                    };
                }
                
                Logger.Debug("MirrorDiscovery", $"Endpoint returned {response.StatusCode}");
            }
            catch (Exception ex)
            {
                Logger.Debug("MirrorDiscovery", $"Request failed: {ex.Message}");
            }
        }

        // Fallback: if /health works, may be a valid launcher API mirror
        if (hasHealthEndpoint)
        {
            Logger.Debug("MirrorDiscovery", "Has /health endpoint but no version API found");
        }

        return new DiscoveryResult { Success = false };
    }

    /// <summary>
    /// Try detection for mirrors with static file structure.
    /// Pattern: /{patches}/{os}/{arch}/{branch}/0/ with HTML autoindex listing .pwr files
    /// </summary>
    private async Task<DiscoveryResult> TryStaticFilesPatternAsync(Uri baseUri, CancellationToken ct)
    {
        // Common static file paths
        var pathVariants = new[]
        {
            "/hytale/patches",
            "/patches",
            ""  // maybe the base URL is already the patches root
        };
        
        var osArchBranch = new[]
        {
            "linux/x64/release/0/",
            "linux/amd64/release/0/",
            "windows/x64/release/0/",
        };

        foreach (var pathPrefix in pathVariants)
        {
            foreach (var suffix in osArchBranch)
            {
                var testPath = $"{pathPrefix}/{suffix}".Replace("//", "/");
                var testUrl = new Uri(baseUri, testPath).ToString();
                Logger.Debug("MirrorDiscovery", $"Testing static files path: {testUrl}");
                
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
                    
                    using var response = await GetWithHeadersAsync(testUrl, linkedCts.Token);
                    if (!response.IsSuccessStatusCode) continue;

                    var content = await response.Content.ReadAsStringAsync(linkedCts.Token);
                    if (string.IsNullOrWhiteSpace(content)) continue;

                    // Look for .pwr file links in HTML
                    var pwrPattern = new Regex(@"href=""(\d+)\.pwr""", RegexOptions.IgnoreCase);
                    var matches = pwrPattern.Matches(content);

                    if (matches.Count > 0)
                    {
                        Logger.Debug("MirrorDiscovery", $"Found {matches.Count} .pwr files in HTML listing");
                        
                        // Detected HTML autoindex with .pwr files
                        var basePath = pathPrefix.TrimEnd('/');
                        var mirrorId = MirrorSchemaInferrer.GenerateMirrorId(baseUri);
                        
                        var mirror = MirrorSchemaInferrer.CreateStaticFilesPatternMirror(baseUri, mirrorId, basePath);
                        
                        return new DiscoveryResult
                        {
                            Success = true,
                            Mirror = mirror,
                            DetectedType = "Pattern: Static Files"
                        };
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug("MirrorDiscovery", $"Request failed: {ex.Message}");
                }
            }
        }

        return new DiscoveryResult { Success = false };
    }

    /// <summary>
    /// Try to detect a JSON index API (like ShipOfYarn).
    /// Expects JSON response with "hytale" root containing branch/platform structure.
    /// </summary>
    private async Task<DiscoveryResult> TryJsonIndexDiscoveryAsync(Uri baseUri, CancellationToken ct)
    {
        // Common API endpoints to try
        var endpoints = new[]
        {
            "/api.php",
            "/api",
            "/api.json",
            "/index.json",
            "/hytale.json",
            "/files.json"
        };

        foreach (var endpoint in endpoints)
        {
            var apiUrl = new Uri(baseUri, endpoint).ToString();
            Logger.Debug("MirrorDiscovery", $"Testing JSON Index endpoint: {apiUrl}");
            
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
                
                using var response = await GetWithHeadersAsync(apiUrl, linkedCts.Token);
                
                if (!response.IsSuccessStatusCode)
                {
                    Logger.Debug("MirrorDiscovery", $"Endpoint returned {response.StatusCode}");
                    continue;
                }

                // Check Content-Type - must be JSON, not HTML error pages
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase) &&
                    !contentType.Contains("php", StringComparison.OrdinalIgnoreCase))  // Allow PHP for api.php
                {
                    Logger.Debug("MirrorDiscovery", $"Endpoint returned non-JSON Content-Type: {contentType}");
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync(linkedCts.Token);
                if (string.IsNullOrWhiteSpace(content)) continue;

                Logger.Debug("MirrorDiscovery", $"Got response, length: {content.Length} chars");

                // Try to parse as JSON
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                // Look for "hytale" root property (common pattern)
                if (root.TryGetProperty("hytale", out var hytaleNode))
                {
                    Logger.Debug("MirrorDiscovery", "Found 'hytale' root property - JSON index format detected");
                    
                    // Detected JSON index format
                    var mirrorId = MirrorSchemaInferrer.GenerateMirrorId(baseUri);
                    var mirror = new MirrorMeta
                    {
                        SchemaVersion = 1,
                        Id = mirrorId,
                        Name = MirrorSchemaInferrer.ExtractMirrorName(baseUri),
                        Description = $"Auto-discovered mirror from {baseUri.Host}",
                        Priority = 100,
                        Enabled = true,
                        SourceType = "json-index",
                        JsonIndex = new MirrorJsonIndexConfig
                        {
                            ApiUrl = apiUrl,
                            RootPath = "hytale",
                            Structure = MirrorSchemaInferrer.DetectJsonStructure(hytaleNode),
                            PlatformMapping = new Dictionary<string, string>
                            {
                                ["darwin"] = "mac"
                            },
                            FileNamePattern = new FileNamePatternConfig
                            {
                                Full = "v{version}-{os}-{arch}.pwr",
                                Diff = "v{from}~{to}-{os}-{arch}.pwr"
                            },
                            DiffBasedBranches = new List<string> { "pre-release" }
                        },
                        SpeedTest = new MirrorSpeedTestConfig
                        {
                            PingUrl = apiUrl
                        },
                        Cache = new MirrorCacheConfig
                        {
                            IndexTtlMinutes = 30,
                            SpeedTestTtlMinutes = 60
                        }
                    };

                    return new DiscoveryResult
                    {
                        Success = true,
                        Mirror = mirror,
                        DetectedType = "json-index"
                    };
                }
            }
            catch (JsonException je)
            {
                Logger.Debug("MirrorDiscovery", $"JSON parse error: {je.Message}");
            }
            catch (Exception ex)
            {
                Logger.Debug("MirrorDiscovery", $"Request failed: {ex.Message}");
            }
        }

        return new DiscoveryResult { Success = false };
    }

    /// <summary>
    /// Try to detect a JSON API that returns version list.
    /// </summary>
    private async Task<DiscoveryResult> TryJsonApiDiscoveryAsync(Uri baseUri, CancellationToken ct)
    {
        // Common version API endpoints with query parameters
        var versionEndpoints = new[]
        {
            "/launcher/patches/release/versions?os_name=linux&arch=x64",
            "/launcher/patches/prerelease/versions?os_name=linux&arch=x64",
            "/launcher/patches/release/versions",
            "/launcher/patches/pre-release/versions",
            "/versions",
            "/api/versions"
        };

        foreach (var endpoint in versionEndpoints)
        {
            var apiUrl = new Uri(baseUri, endpoint).ToString();
            Logger.Debug("MirrorDiscovery", $"Testing JSON API endpoint: {apiUrl}");
            
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
                
                using var response = await GetWithHeadersAsync(apiUrl, linkedCts.Token);
                
                if (!response.IsSuccessStatusCode)
                {
                    Logger.Debug("MirrorDiscovery", $"Endpoint returned {response.StatusCode}");
                    continue;
                }

                // Check Content-Type - must be JSON, not HTML error pages
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Debug("MirrorDiscovery", $"Endpoint returned non-JSON Content-Type: {contentType}");
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync(linkedCts.Token);
                if (string.IsNullOrWhiteSpace(content)) continue;

                Logger.Debug("MirrorDiscovery", $"Got JSON response, length: {content.Length}");

                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                // Look for items[] or versions[] array
                string? jsonPath = null;
                if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    jsonPath = "items[].version";
                    Logger.Debug("MirrorDiscovery", $"Found 'items' array with {items.GetArrayLength()} elements");
                }
                else if (root.TryGetProperty("versions", out var versions) && versions.ValueKind == JsonValueKind.Array)
                {
                    jsonPath = "versions";
                    Logger.Debug("MirrorDiscovery", $"Found 'versions' array with {versions.GetArrayLength()} elements");
                }
                else if (root.ValueKind == JsonValueKind.Array)
                {
                    jsonPath = "$root";
                    Logger.Debug("MirrorDiscovery", $"Found root array with {root.GetArrayLength()} elements");
                }

                if (jsonPath != null)
                {
                    var mirrorId = MirrorSchemaInferrer.GenerateMirrorId(baseUri);
                    var mirror = new MirrorMeta
                    {
                        SchemaVersion = 1,
                        Id = mirrorId,
                        Name = MirrorSchemaInferrer.ExtractMirrorName(baseUri),
                        Description = $"Auto-discovered mirror from {baseUri.Host}",
                        Priority = 100,
                        Enabled = true,
                        SourceType = "pattern",
                        Pattern = new MirrorPatternConfig
                        {
                            FullBuildUrl = "{base}/launcher/patches/{os}/{arch}/{branch}/0/{version}.pwr",
                            DiffPatchUrl = "{base}/launcher/patches/{os}/{arch}/{branch}/{from}/{to}.pwr",
                            BaseUrl = baseUri.GetLeftPart(UriPartial.Authority),
                            VersionDiscovery = new VersionDiscoveryConfig
                            {
                                Method = "json-api",
                                Url = "{base}/launcher/patches/{branch}/versions?os_name={os}&arch={arch}",
                                JsonPath = jsonPath
                            },
                            BranchMapping = new Dictionary<string, string>
                            {
                                ["pre-release"] = "prerelease"
                            },
                            DiffBasedBranches = new List<string>()
                        },
                        SpeedTest = new MirrorSpeedTestConfig
                        {
                            PingUrl = baseUri.GetLeftPart(UriPartial.Authority) + "/health"
                        },
                        Cache = new MirrorCacheConfig
                        {
                            IndexTtlMinutes = 30,
                            SpeedTestTtlMinutes = 60
                        }
                    };

                    return new DiscoveryResult
                    {
                        Success = true,
                        Mirror = mirror,
                        DetectedType = "pattern (json-api)"
                    };
                }
            }
            catch (JsonException je)
            {
                Logger.Debug("MirrorDiscovery", $"JSON parse error: {je.Message}");
            }
            catch (Exception ex)
            {
                Logger.Debug("MirrorDiscovery", $"Request failed: {ex.Message}");
            }
        }

        return new DiscoveryResult { Success = false };
    }

    /// <summary>
    /// Try to detect HTML autoindex (Apache/Nginx directory listing).
    /// </summary>
    private async Task<DiscoveryResult> TryHtmlAutoindexDiscoveryAsync(Uri baseUri, CancellationToken ct)
    {
        // Common paths for Hytale patches
        var patchPaths = new[]
        {
            "/hytale/patches/linux/x64/release/0/",
            "/hytale/patches/linux/amd64/release/0/",
            "/patches/linux/x64/release/0/",
            "/patches/linux/amd64/release/0/",
            "/linux/x64/release/0/",
            "/linux/amd64/release/0/"
        };

        foreach (var path in patchPaths)
        {
            var testUrl = new Uri(baseUri, path).ToString();
            Logger.Debug("MirrorDiscovery", $"Testing HTML autoindex path: {testUrl}");
            
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
                
                using var response = await GetWithHeadersAsync(testUrl, linkedCts.Token);
                
                if (!response.IsSuccessStatusCode)
                {
                    Logger.Debug("MirrorDiscovery", $"Path returned {response.StatusCode}");
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync(linkedCts.Token);
                if (string.IsNullOrWhiteSpace(content)) continue;

                // Look for .pwr file links in HTML
                var pwrPattern = new Regex(@"href=""(\d+)\.pwr""", RegexOptions.IgnoreCase);
                var matches = pwrPattern.Matches(content);

                if (matches.Count > 0)
                {
                    Logger.Debug("MirrorDiscovery", $"Found {matches.Count} .pwr files in HTML listing");
                    
                    // Detected HTML autoindex with .pwr files
                    // Determine which path structure we found
                    var basePath = path.Contains("/x64/") 
                        ? path.Replace("/linux/x64/release/0/", "").TrimEnd('/') 
                        : path.Replace("/linux/amd64/release/0/", "").TrimEnd('/');
                    
                    var mirrorId = MirrorSchemaInferrer.GenerateMirrorId(baseUri);
                    var mirror = new MirrorMeta
                    {
                        SchemaVersion = 1,
                        Id = mirrorId,
                        Name = MirrorSchemaInferrer.ExtractMirrorName(baseUri),
                        Description = $"Auto-discovered mirror from {baseUri.Host}",
                        Priority = 100,
                        Enabled = true,
                        SourceType = "pattern",
                        Pattern = new MirrorPatternConfig
                        {
                            FullBuildUrl = "{base}" + basePath + "/{os}/{arch}/{branch}/0/{version}.pwr",
                            DiffPatchUrl = "{base}" + basePath + "/{os}/{arch}/{branch}/{from}/{to}.pwr",
                            SignatureUrl = "{base}" + basePath + "/{os}/{arch}/{branch}/0/{version}.pwr.sig",
                            BaseUrl = baseUri.GetLeftPart(UriPartial.Authority),
                            VersionDiscovery = new VersionDiscoveryConfig
                            {
                                Method = "html-autoindex",
                                Url = "{base}" + basePath + "/{os}/{arch}/{branch}/0/",
                                HtmlPattern = @"<a\s+href=""(\d+)\.pwr"">\d+\.pwr</a>\s+\S+\s+\S+\s+(\d+)",
                                MinFileSizeBytes = 1_048_576
                            },
                            DiffBasedBranches = new List<string>()
                        },
                        SpeedTest = new MirrorSpeedTestConfig
                        {
                            PingUrl = baseUri.GetLeftPart(UriPartial.Authority) + basePath
                        },
                        Cache = new MirrorCacheConfig
                        {
                            IndexTtlMinutes = 30,
                            SpeedTestTtlMinutes = 60
                        }
                    };

                    return new DiscoveryResult
                    {
                        Success = true,
                        Mirror = mirror,
                        DetectedType = "pattern (html-autoindex)"
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Debug("MirrorDiscovery", $"Request failed: {ex.Message}");
            }
        }

        return new DiscoveryResult { Success = false };
    }

    /// <summary>
    /// Try to match known mirror patterns based on hostname and directory structure.
    /// </summary>
    private async Task<DiscoveryResult> TryKnownPatternDiscoveryAsync(Uri baseUri, CancellationToken ct)
    {
        var url = baseUri.ToString().TrimEnd('/');
        Logger.Debug("MirrorDiscovery", $"Testing directory pattern at: {url}");
        
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
            
            using var response = await GetWithHeadersAsync(url, linkedCts.Token);
            
            if (!response.IsSuccessStatusCode)
            {
                Logger.Debug("MirrorDiscovery", $"URL returned {response.StatusCode}");
                return new DiscoveryResult { Success = false };
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var content = await response.Content.ReadAsStringAsync(linkedCts.Token);
            
            Logger.Debug("MirrorDiscovery", $"Content-Type: {contentType}, length: {content.Length}");

            // If it's HTML with directory listing
            if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                // Check for subdirectories like linux/, windows/, darwin/
                var hasOsDirectories = content.Contains("linux/") || content.Contains("windows/") || content.Contains("darwin/");
                
                if (hasOsDirectories)
                {
                    Logger.Debug("MirrorDiscovery", "Found OS directories in listing");
                    
                    var mirrorId = MirrorSchemaInferrer.GenerateMirrorId(baseUri);
                    var mirror = new MirrorMeta
                    {
                        SchemaVersion = 1,
                        Id = mirrorId,
                        Name = MirrorSchemaInferrer.ExtractMirrorName(baseUri),
                        Description = $"Auto-discovered mirror from {baseUri.Host}",
                        Priority = 100,
                        Enabled = true,
                        SourceType = "pattern",
                        Pattern = new MirrorPatternConfig
                        {
                            FullBuildUrl = "{base}/{os}/{arch}/{branch}/0/{version}.pwr",
                            DiffPatchUrl = "{base}/{os}/{arch}/{branch}/{from}/{to}.pwr",
                            BaseUrl = url,
                            VersionDiscovery = new VersionDiscoveryConfig
                            {
                                Method = "html-autoindex",
                                Url = "{base}/{os}/{arch}/{branch}/0/",
                                HtmlPattern = @"<a\s+href=""(\d+)\.pwr"">\d+\.pwr</a>\s+\S+\s+\S+\s+(\d+)",
                                MinFileSizeBytes = 1_048_576
                            },
                            DiffBasedBranches = new List<string>()
                        },
                        SpeedTest = new MirrorSpeedTestConfig
                        {
                            PingUrl = url
                        },
                        Cache = new MirrorCacheConfig
                        {
                            IndexTtlMinutes = 30,
                            SpeedTestTtlMinutes = 60
                        }
                    };

                    return new DiscoveryResult
                    {
                        Success = true,
                        Mirror = mirror,
                        DetectedType = "pattern (directory structure)"
                    };
                }
            }
        }
        catch { /* Discovery failed */ }

        return new DiscoveryResult { Success = false };
    }
}
