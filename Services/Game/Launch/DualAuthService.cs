using System.Diagnostics;
using System.Text.Json;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.Game.Launch;

/// <summary>
/// Manages the DualAuth agent for server authentication.
/// DualAuth allows runtime bytecode transformation instead of static JAR patching,
/// enabling seamless dual-authentication (Official + F2P) without modifying server files.
/// </summary>
public static class DualAuthService
{
    private const string AgentUrl = "https://github.com/sanasol/hytale-auth-server/releases/latest/download/dualauth-agent.jar";
    private const string GitHubReleasesApiUrl = "https://api.github.com/repos/sanasol/hytale-auth-server/releases/latest";
    private const string AgentDirName = "DualAuth";
    private const string AgentFilename = "dualauth-agent.jar";
    private const string VersionFilename = ".agent-version";
    private const int MinAgentSizeBytes = 1024;
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>
    /// Gets the global path where the DualAuth agent is stored.
    /// The agent is shared across all instances: appDir/DualAuth/dualauth-agent.jar
    /// </summary>
    public static string GetAgentPath(string appDir)
    {
        return Path.Combine(appDir, AgentDirName, AgentFilename);
    }

    /// <summary>
    /// Gets the path to the file that stores the installed agent version tag.
    /// </summary>
    public static string GetAgentVersionPath(string appDir)
    {
        return Path.Combine(appDir, AgentDirName, VersionFilename);
    }

    /// <summary>
    /// Reads the installed agent version tag from disk.
    /// Returns null if the file does not exist or cannot be read.
    /// </summary>
    public static string? GetInstalledAgentVersion(string appDir)
    {
        var versionPath = GetAgentVersionPath(appDir);
        try
        {
            if (File.Exists(versionPath))
                return File.ReadAllText(versionPath).Trim();
        }
        catch (Exception ex)
        {
            Logger.Warning("DualAuth", $"Failed to read agent version file: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Saves the installed agent version tag to disk.
    /// </summary>
    private static void SaveAgentVersion(string appDir, string tag)
    {
        try
        {
            File.WriteAllText(GetAgentVersionPath(appDir), tag);
        }
        catch (Exception ex)
        {
            Logger.Warning("DualAuth", $"Failed to write agent version file: {ex.Message}");
        }
    }

    /// <summary>
    /// Queries the GitHub Releases API to get the latest release tag and download URL.
    /// Returns null on failure (network error, rate limit, etc.).
    /// </summary>
    public static async Task<(string tag, string downloadUrl)?> FetchLatestReleaseInfoAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubReleasesApiUrl);
            request.Headers.Add("User-Agent", "HyPrism/1.0");
            request.Headers.Add("Accept", "application/vnd.github+json");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            using var response = await _httpClient.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("DualAuth", $"GitHub API returned {(int)response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagProp))
                return null;

            var tag = tagProp.GetString();
            if (string.IsNullOrEmpty(tag))
                return null;

            // Try to find the agent JAR in release assets
            string downloadUrl = AgentUrl; // fall back to the known direct URL
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var nameProp) &&
                        nameProp.GetString()?.Equals(AgentFilename, StringComparison.OrdinalIgnoreCase) == true &&
                        asset.TryGetProperty("browser_download_url", out var urlProp))
                    {
                        downloadUrl = urlProp.GetString() ?? downloadUrl;
                        break;
                    }
                }
            }

            return (tag, downloadUrl);
        }
        catch (OperationCanceledException)
        {
            Logger.Warning("DualAuth", "GitHub API request timed out");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warning("DualAuth", $"Failed to fetch release info from GitHub: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Ensures the DualAuth agent is present and up-to-date before each launch.
    /// Checks GitHub for a newer release; if found, downloads the new agent and removes the old one.
    /// Falls back to <see cref="EnsureAgentAvailableAsync"/> if the GitHub API is unreachable.
    /// </summary>
    public static async Task<DualAuthResult> EnsureAgentUpToDateAsync(
        string appDir,
        Action<string, int?>? progressCallback = null,
        CancellationToken ct = default)
    {
        var agentPath = GetAgentPath(appDir);
        var agentDir = Path.GetDirectoryName(agentPath)!;

        if (!Directory.Exists(agentDir))
            Directory.CreateDirectory(agentDir);

        Logger.Info("DualAuth", "Checking for DualAuth agent updates...");
        progressCallback?.Invoke("Checking for agent updates...", null);

        var releaseInfo = await FetchLatestReleaseInfoAsync(ct);

        if (releaseInfo == null)
        {
            // GitHub API unreachable — fall back to ensuring agent exists
            Logger.Warning("DualAuth", "Cannot check for updates (GitHub unreachable), falling back to local check");
            return await EnsureAgentAvailableAsync(appDir, progressCallback, ct);
        }

        var (latestTag, downloadUrl) = releaseInfo.Value;
        var installedTag = GetInstalledAgentVersion(appDir);

        bool agentExists = IsAgentAvailable(appDir);
        bool isUpToDate = agentExists && installedTag == latestTag;

        if (isUpToDate)
        {
            Logger.Info("DualAuth", $"Agent is up-to-date (version {latestTag})");
            progressCallback?.Invoke($"DualAuth Agent {latestTag} ready", 100);
            return new DualAuthResult { Success = true, AgentPath = agentPath, AlreadyExists = true };
        }

        if (agentExists && installedTag != null)
        {
            Logger.Info("DualAuth", $"New agent version available: {installedTag} → {latestTag}. Updating...");
            progressCallback?.Invoke($"Updating agent {installedTag} → {latestTag}...", 0);
        }
        else
        {
            Logger.Info("DualAuth", $"Downloading DualAuth agent {latestTag}...");
            progressCallback?.Invoke($"Downloading DualAuth Agent {latestTag}...", 0);
        }

        // Delete old agent before downloading new one
        if (File.Exists(agentPath))
        {
            try { File.Delete(agentPath); }
            catch (Exception ex) { Logger.Warning("DualAuth", $"Could not delete old agent: {ex.Message}"); }
        }

        var tempPath = agentPath + ".tmp";
        try
        {
            using var dlRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            dlRequest.Headers.Add("User-Agent", "HyPrism/1.0");
            using var response = await _httpClient.SendAsync(dlRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var downloadedBytes = 0L;

            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                downloadedBytes += bytesRead;
                if (totalBytes > 0)
                {
                    var percent = (int)((downloadedBytes * 100) / totalBytes);
                    progressCallback?.Invoke($"Downloading agent {latestTag}... {downloadedBytes / 1024} KB", percent);
                }
            }
            await fileStream.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            Logger.Error("DualAuth", $"Failed to download updated agent: {ex.Message}");
            if (File.Exists(tempPath)) File.Delete(tempPath);

            // If agent still exists (deletion failed earlier), return success
            if (IsAgentAvailable(appDir))
                return new DualAuthResult { Success = true, AgentPath = agentPath, AlreadyExists = true };

            return new DualAuthResult { Success = false, Error = ex.Message };
        }

        // Validate downloaded file
        var tempInfo = new FileInfo(tempPath);
        if (tempInfo.Length < MinAgentSizeBytes)
        {
            File.Delete(tempPath);
            var error = "Downloaded agent too small (corrupt or failed download)";
            Logger.Error("DualAuth", error);
            return new DualAuthResult { Success = false, Error = error };
        }

        File.Move(tempPath, agentPath, overwrite: true);
        SaveAgentVersion(appDir, latestTag);

        progressCallback?.Invoke($"DualAuth Agent {latestTag} ready", 100);
        Logger.Success("DualAuth", $"Agent updated to {latestTag}: {agentPath}");
        return new DualAuthResult { Success = true, AgentPath = agentPath };
    }

    /// <summary>
    /// Checks if the DualAuth agent exists and appears valid.
    /// </summary>
    public static bool IsAgentAvailable(string appDir)
    {
        var agentPath = GetAgentPath(appDir);
        if (!File.Exists(agentPath)) return false;

        try
        {
            var info = new FileInfo(agentPath);
            return info.Length >= MinAgentSizeBytes;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Downloads the DualAuth agent JAR if not already present or invalid.
    /// The agent is stored globally in appDir/DualAuth/ and shared across all instances.
    /// </summary>
    public static async Task<DualAuthResult> EnsureAgentAvailableAsync(
        string appDir,
        Action<string, int?>? progressCallback = null,
        CancellationToken ct = default)
    {
        var agentPath = GetAgentPath(appDir);
        var agentDir = Path.GetDirectoryName(agentPath)!;

        // Check if already exists and valid
        if (IsAgentAvailable(appDir))
        {
            Logger.Info("DualAuth", "Agent already available");
            return new DualAuthResult { Success = true, AgentPath = agentPath, AlreadyExists = true };
        }

        // Ensure DualAuth directory exists
        if (!Directory.Exists(agentDir))
        {
            Directory.CreateDirectory(agentDir);
        }

        progressCallback?.Invoke("Downloading DualAuth Agent...", 0);
        Logger.Info("DualAuth", $"Downloading agent from {AgentUrl}");

        var tempPath = agentPath + ".tmp";

        try
        {
            using var response = await _httpClient.GetAsync(AgentUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var downloadedBytes = 0L;

            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    var percent = (int)((downloadedBytes * 100) / totalBytes);
                    progressCallback?.Invoke($"Downloading agent... {downloadedBytes / 1024} KB", percent);
                }
            }

            await fileStream.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            Logger.Error("DualAuth", $"Failed to download agent: {ex.Message}");
            if (File.Exists(tempPath)) File.Delete(tempPath);
            return new DualAuthResult { Success = false, Error = ex.Message };
        }

        // Validate the downloaded file
        var tempInfo = new FileInfo(tempPath);
        if (tempInfo.Length < MinAgentSizeBytes)
        {
            File.Delete(tempPath);
            var error = "Downloaded agent too small (corrupt or failed download)";
            Logger.Error("DualAuth", error);
            return new DualAuthResult { Success = false, Error = error };
        }

        // Move to final location
        if (File.Exists(agentPath)) File.Delete(agentPath);
        File.Move(tempPath, agentPath);

        progressCallback?.Invoke("DualAuth Agent ready", 100);
        Logger.Success("DualAuth", $"Agent downloaded successfully: {agentPath}");

        return new DualAuthResult { Success = true, AgentPath = agentPath };
    }

    /// <summary>
    /// Builds environment variables for DualAuth agent.
    /// </summary>
    /// <param name="agentPath">Full path to dualauth-agent.jar</param>
    /// <param name="authDomain">Custom auth domain (e.g., "sanasol.ws")</param>
    /// <param name="trustOfficialIssuers">Whether to also trust official Hytale issuers</param>
    /// <returns>Dictionary of environment variables to set</returns>
    public static Dictionary<string, string> BuildDualAuthEnvironment(
        string agentPath,
        string authDomain,
        bool trustOfficialIssuers = true)
    {
        var env = new Dictionary<string, string>
        {
            // Java agent flag - this tells the JVM to use the DualAuth agent
            ["JAVA_TOOL_OPTIONS"] = $"-javaagent:\"{agentPath}\"",
            
            // DualAuth configuration
            ["HYTALE_AUTH_DOMAIN"] = authDomain,
            ["HYTALE_TRUST_ALL_ISSUERS"] = "true",
            ["HYTALE_TRUST_OFFICIAL"] = trustOfficialIssuers ? "true" : "false",
        };

        Logger.Info("DualAuth", $"Environment configured: HYTALE_AUTH_DOMAIN={authDomain}");
        return env;
    }

    /// <summary>
    /// Applies DualAuth environment variables to a ProcessStartInfo.
    /// </summary>
    public static void ApplyToProcess(ProcessStartInfo startInfo, string agentPath, string authDomain, bool trustOfficialIssuers = true)
    {
        var env = BuildDualAuthEnvironment(agentPath, authDomain, trustOfficialIssuers);
        foreach (var (key, value) in env)
        {
            startInfo.Environment[key] = value;
        }
    }

    /// <summary>
    /// Builds environment variable lines for Unix launch scripts.
    /// </summary>
    public static string BuildUnixEnvLines(string agentPath, string authDomain, bool trustOfficialIssuers = true)
    {
        return $@"# DualAuth Agent Configuration
export JAVA_TOOL_OPTIONS=""-javaagent:{agentPath}""
export HYTALE_AUTH_DOMAIN=""{authDomain}""
export HYTALE_TRUST_ALL_ISSUERS=""true""
export HYTALE_TRUST_OFFICIAL=""{(trustOfficialIssuers ? "true" : "false")}""
";
    }
}

/// <summary>
/// Result of DualAuth agent operations.
/// </summary>
public class DualAuthResult
{
    public bool Success { get; init; }
    public string? AgentPath { get; init; }
    public bool AlreadyExists { get; init; }
    public string? Error { get; init; }
}
