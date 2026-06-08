using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.Core.Integration;

/// <summary>
/// Represents a GitHub user with basic profile information.
/// Used for displaying contributors and user details.
/// </summary>
public class GitHubUser
{
    /// <summary>
    /// Gets or sets the GitHub username (login name).
    /// </summary>
    [JsonPropertyName("login")]
    public string Login { get; set; } = "";
    
    /// <summary>
    /// Gets or sets the URL to the user's avatar image.
    /// </summary>
    [JsonPropertyName("avatar_url")]
    public string AvatarUrl { get; set; } = "";
    
    /// <summary>
    /// Gets or sets the URL to the user's GitHub profile page.
    /// </summary>
    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";
    
    /// <summary>
    /// Gets or sets the account type (e.g., "User", "Bot").
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
}

/// <summary>
/// Provides GitHub API integration for fetching contributor information and avatars.
/// Handles rate limiting gracefully and caches avatar downloads.
/// </summary>
public class GitHubService : IGitHubService
{
    private readonly HttpClient _httpClient;
    private const string RepoOwner = "yyyumeniku";
    private const string RepoName = "HyPrism";

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client for making API requests.</param>
    public GitHubService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        // Ensure User-Agent is set (required for GitHub API)
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HyPrism-Launcher");
        }
    }

    /// <inheritdoc/>
    public async Task<List<GitHubUser>> GetContributorsAsync()
    {
        try
        {
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/contributors?per_page=100";
            return await _httpClient.GetFromJsonAsync<List<GitHubUser>>(url) ?? [];
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == System.Net.HttpStatusCode.Forbidden || ex.Message.Contains("403"))
            {
                Logger.Warning("GitHub", "Failed to fetch contributors rate limit exceeded");
                return [];
            }
            Logger.Error("GitHub", $"Failed to fetch contributors: {ex}");
            return [];
        }
        catch (Exception ex)
        {
            Logger.Error("GitHub", $"Failed to fetch contributors: {ex}");
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task<GitHubUser?> GetUserAsync(string username)
    {
        try
        {
            var url = $"https://api.github.com/users/{username}";
            return await _httpClient.GetFromJsonAsync<GitHubUser>(url);
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == System.Net.HttpStatusCode.Forbidden || ex.Message.Contains("403"))
            {
                Logger.Warning("GitHub", $"Failed to fetch user {username} rate limit exceeded");
                return null;
            }
            Logger.Error("GitHub", $"Failed to fetch user {username}: {ex}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error("GitHub", $"Failed to fetch user {username}: {ex}");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]?> LoadAvatarAsync(string url, int decodeWidth = 96)
    {
        try
        {
            if (string.IsNullOrEmpty(url)) return null;
            
            // Append GitHub size parameter to request smaller image from CDN (saves bandwidth)
            var sizedUrl = url.Contains('?') ? $"{url}&s={decodeWidth}" : $"{url}?s={decodeWidth}";
            
            return await _httpClient.GetByteArrayAsync(sizedUrl);
        }
        catch (Exception ex)
        {
            Logger.Error("GitHub", $"Failed to load avatar from {url}: {ex}");
            return null;
        }
    }
}
