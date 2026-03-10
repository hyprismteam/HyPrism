using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.Core.Integration;

/// <summary>
/// Defines the source of news items.
/// </summary>
public enum NewsSource
{
    /// <summary>Fetch news from all sources.</summary>
    All,
    /// <summary>Fetch news from Hytale official blog only.</summary>
    Hytale,
    /// <summary>Fetch news from HyPrism GitHub releases only.</summary>
    HyPrism
}

/// <summary>
/// Fetches and aggregates news from Hytale's official blog API and HyPrism GitHub Releases.
/// Implements caching to reduce API calls and handle rate limits.
/// </summary>
public class NewsService : INewsService
{
    private readonly HttpClient _httpClient;
    private readonly string _appIconPath = "";

    /// <summary>
    /// Initializes a new instance of the <see cref="NewsService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client for fetching news.</param>
    public NewsService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        
        // Ensure headers are set if they aren't already
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HyPrism/1.0");
        }
    }
    private const string HytaleNewsUrl = "https://hytale.com/news";
    private const string HyPrismReleasesUrl = "https://api.github.com/repos/yyyumeniku/HyPrism/releases";
    
    // Cache for HyPrism news to avoid GitHub API rate limits
    private List<NewsItemResponse>? _hyprismNewsCache;
    private DateTime _hyprismCacheTime = DateTime.MinValue;
    private static readonly SemaphoreSlim _hyprismLock = new(1, 1);
    
    // Cache for Hytale news
    private List<NewsItemResponse>? _hytaleNewsCache;
    private DateTime _hytaleCacheTime = DateTime.MinValue;
    private static readonly SemaphoreSlim _hytaleLock = new(1, 1);
    
    private const int CacheExpirationMinutes = 30;
    
    // Legacy constructor removed in favor of DI
    
    public async Task<List<NewsItemResponse>> GetNewsAsync(int count = 10, NewsSource source = NewsSource.All)
    {
        try
        {
            var allNews = new List<(NewsItemResponse item, DateTime dateTime)>();

            // Fetch each source independently at its own cap so one source can't crowd out the other.
            // The merged list is returned sorted by date without a global Take(count).
            const int perSourceCap = 30;

            Task<List<NewsItemResponse>>? hytaleTask = null;
            Task<List<NewsItemResponse>>? hyprismTask = null;

            if (source == NewsSource.All || source == NewsSource.Hytale)
                hytaleTask = GetHytaleNewsAsync(perSourceCap);

            if (source == NewsSource.All || source == NewsSource.HyPrism)
                hyprismTask = GetHyPrismNewsAsync(perSourceCap);

            // Await both concurrently
            var awaitList = new List<Task>();
            if (hytaleTask != null) awaitList.Add(hytaleTask);
            if (hyprismTask != null) awaitList.Add(hyprismTask);
            await Task.WhenAll(awaitList);

            if (hytaleTask != null)
                allNews.AddRange((await hytaleTask).Select(n => (n, ParseDate(n.Date))));

            if (hyprismTask != null)
                allNews.AddRange((await hyprismTask).Select(n => (n, ParseDate(n.Date))));

            // Return all items from all sources sorted by date — no cross-source count cap.
            var sortedNews = allNews
                .OrderByDescending(x => x.dateTime)
                .Select(x => x.item)
                .ToList();

            return sortedNews;
        }
        catch (Exception ex)
        {
            Logger.Error("News", $"Failed to fetch news: {ex.Message}");
            return new List<NewsItemResponse>();
        }
    }

    private async Task<List<NewsItemResponse>> GetHytaleNewsAsync(int count)
    {
        if (_hytaleNewsCache != null && _hytaleNewsCache.Count >= count && (DateTime.Now - _hytaleCacheTime).TotalMinutes < CacheExpirationMinutes)
            return _hytaleNewsCache.Take(count).ToList();

        await _hytaleLock.WaitAsync();
        try
        {
            if (_hytaleNewsCache != null && _hytaleNewsCache.Count >= count && (DateTime.Now - _hytaleCacheTime).TotalMinutes < CacheExpirationMinutes)
                return _hytaleNewsCache.Take(count).ToList();

            return await GetHytaleNewsInternalAsync(count);
        }
        finally
        {
            _hytaleLock.Release();
        }
    }

    private static readonly Regex _hytaleCardRegex = new(
        @"<a\s[^>]*href=""(/news/(\d{4})/(\d+)/([^""]+))""[^>]*>(.*?)</a>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _hytaleThumbnailRegex = new(
        @"img\s+src=""(https://cdn\.hytale\.com/[^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _hytaleTitleRegex = new(
        @"class=""[^""]*font-[^""]*""[^>]*>([^<]{5,150})<",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _hytaleDateTextRegex = new(
        @"\b(January|February|March|April|May|June|July|August|September|October|November|December)\s+(\d{1,2}),?\s+(\d{4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private async Task<List<NewsItemResponse>> GetHytaleNewsInternalAsync(int count)
    {
        try
        {
            // Check cache
            if (_hytaleNewsCache != null && 
                _hytaleNewsCache.Count >= count && 
                (DateTime.Now - _hytaleCacheTime).TotalMinutes < CacheExpirationMinutes)
            {
                return _hytaleNewsCache.Take(count).ToList();
            }

            Logger.Info("News", "Scraping news from hytale.com/news...");
            var html = await _httpClient.GetStringAsync(HytaleNewsUrl);

            var news = new List<NewsItemResponse>();

            foreach (Match m in _hytaleCardRegex.Matches(html))
            {
                if (news.Count >= 30) break; // cap to avoid runaway scraping

                var relPath = m.Groups[1].Value;
                var year    = m.Groups[2].Value;
                var month   = m.Groups[3].Value;
                var slug    = m.Groups[4].Value;
                var inner   = m.Groups[5].Value;

                // thumbnail — prefer blog_thumb variant embedded in the card
                string? imageUrl = null;
                var imgMatch = _hytaleThumbnailRegex.Match(inner);
                if (imgMatch.Success) imageUrl = imgMatch.Groups[1].Value;

                // title — from the first font-class span; fall back to human-readable slug
                string title;
                var titleMatch = _hytaleTitleRegex.Match(inner);
                if (titleMatch.Success)
                {
                    title = HttpUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim());
                }
                else
                {
                    // Convert slug to title case: "hytale-patch-notes-update-3" → "Hytale Patch Notes Update 3"
                    title = System.Globalization.CultureInfo.InvariantCulture.TextInfo
                        .ToTitleCase(slug.Replace("-", " "));
                }

                // Try to parse actual date from card text; fall back to URL-derived first-of-month
                string dateStr;
                var dateTextMatch = _hytaleDateTextRegex.Match(inner);
                if (dateTextMatch.Success &&
                    DateTime.TryParse(dateTextMatch.Value, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var parsedDate))
                {
                    dateStr = parsedDate.ToString("yyyy-MM-dd");
                }
                else
                {
                    dateStr = $"{year}-{month.PadLeft(2, '0')}-01";
                }

                news.Add(new NewsItemResponse
                {
                    Title    = title,
                    Excerpt  = "",
                    Url      = $"https://hytale.com{relPath}",
                    Date     = dateStr,
                    Author   = "Hytale Team",
                    ImageUrl = imageUrl,
                    Source   = "hytale"
                });
            }

            if (news.Count > 0)
            {
                _hytaleNewsCache = news;
                _hytaleCacheTime = DateTime.Now;
                Logger.Success("News", $"Scraped {news.Count} Hytale news posts");
            }
            else
            {
                Logger.Warning("News", "Hytale news scraper returned 0 posts");
            }

            return news.Take(count).ToList();
        }
        catch (Exception ex)
        {
            Logger.Warning("News", $"Failed to scrape Hytale news: {ex.Message}");
            return new List<NewsItemResponse>();
        }
    }

    private async Task<List<NewsItemResponse>> GetHyPrismNewsAsync(int count)
    {
        if (_hyprismNewsCache != null && (DateTime.Now - _hyprismCacheTime).TotalMinutes < CacheExpirationMinutes)
            return _hyprismNewsCache.Take(count).ToList();
            
        await _hyprismLock.WaitAsync();
        try
        {
            if (_hyprismNewsCache != null && (DateTime.Now - _hyprismCacheTime).TotalMinutes < CacheExpirationMinutes)
                return _hyprismNewsCache.Take(count).ToList();
                
            return await GetHyPrismNewsInternalAsync(count);
        }
        finally
        {
            _hyprismLock.Release();
        }
    }

    private async Task<List<NewsItemResponse>> GetHyPrismNewsInternalAsync(int count)
    {
        // Check cache first
        if (_hyprismNewsCache != null && (DateTime.Now - _hyprismCacheTime).TotalMinutes < CacheExpirationMinutes)
        {
            Logger.Info("News", "Using cached HyPrism news");
            return _hyprismNewsCache.Take(count).ToList();
        }
        
        try
        {
            Logger.Info("News", "Fetching news from HyPrism GitHub...");
            var response = await _httpClient.GetStringAsync(HyPrismReleasesUrl);
            
            using var jsonDoc = JsonDocument.Parse(response);
            var releases = jsonDoc.RootElement;
            var news = new List<NewsItemResponse>();
            
            var itemCount = 0;
            foreach (var release in releases.EnumerateArray())
            {
                if (itemCount >= count) break;
                
                try
                {
                    var name = release.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    var tagName = release.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
                    var body = release.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : null;
                    var htmlUrl = release.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() : null;
                    var publishedAt = release.TryGetProperty("published_at", out var pubProp) ? pubProp.GetString() : null;
                    
                    var title = !string.IsNullOrEmpty(name) ? name : tagName ?? "HyPrism Release";
                    title = title.Replace("(", "").Replace(")", "").Trim();
                    
                    var excerpt = !string.IsNullOrEmpty(body) 
                        ? body.Split('\n').FirstOrDefault()?.Trim() ?? "Click to see changelog."
                        : "Click to see changelog.";
                    
                    // Remove markdown formatting from excerpt
                    excerpt = Regex.Replace(excerpt, @"[#*_`\[\]]", "");
                    if (excerpt.Length > 100)
                    {
                        excerpt = excerpt.Substring(0, 97) + "...";
                    }
                    
                    news.Add(new NewsItemResponse
                    {
                        Title = $"HyPrism {title} release",
                        Excerpt = excerpt,
                        Url = htmlUrl ?? "https://github.com/yyyumeniku/HyPrism/releases",
                        Date = publishedAt ?? DateTime.Now.ToString("o"),
                        Author = "HyPrism",
                        ImageUrl = _appIconPath,
                        Source = "hyprism"
                    });
                    
                    itemCount++;
                }
                catch (Exception ex)
                {
                    Logger.Warning("News", $"Failed to parse HyPrism release: {ex.Message}");
                    continue;
                }
            }
            
            // Update cache
            _hyprismNewsCache = news;
            _hyprismCacheTime = DateTime.Now;
            Logger.Success("News", "Successfully fetched HyPrism news");
            
            return news;
        }
        catch (HttpRequestException ex)
        {
             if (ex.StatusCode == System.Net.HttpStatusCode.Forbidden || ex.Message.Contains("403"))
             {
                 Logger.Warning("News", "Failed to fetch HyPrism news: Code 403 (rate limit exceeded)");
             }
             else
             {
                 Logger.Warning("News", $"Failed to fetch HyPrism news: {ex.Message}");
             }
             return new List<NewsItemResponse>();
        }
        catch (Exception ex)
        {
            Logger.Warning("News", $"Failed to fetch HyPrism news: {ex.Message}");
            return new List<NewsItemResponse>();
        }
    }
    
    private static DateTime ParseDate(string? dateString)
    {
        if (string.IsNullOrEmpty(dateString))
            return DateTime.MinValue;
            
        if (DateTime.TryParse(dateString, out var date))
            return date;
            
        return DateTime.MinValue;
    }
    
    /// <summary>
    /// Cleans news excerpt by removing HTML tags, duplicate title, and date prefixes.
    /// </summary>
    public static string CleanNewsExcerpt(string? rawExcerpt, string? title)
    {
        var excerpt = HttpUtility.HtmlDecode(rawExcerpt ?? "");
        if (string.IsNullOrWhiteSpace(excerpt))
        {
            return "";
        }

        excerpt = Regex.Replace(excerpt, @"<[^>]+>", " ");
        excerpt = Regex.Replace(excerpt, @"\s+", " ").Trim();

        if (!string.IsNullOrWhiteSpace(title))
        {
            var normalizedTitle = Regex.Replace(title.Trim(), @"\s+", " ");
            var escapedTitle = Regex.Escape(normalizedTitle);
            excerpt = Regex.Replace(excerpt, $@"^\s*{escapedTitle}\s*[:\-–—]?\s*", "", RegexOptions.IgnoreCase);
        }

        excerpt = Regex.Replace(excerpt, @"^\s*\p{L}+\s+\d{1,2},\s*\d{4}\s*[–—\-:]?\s*", "", RegexOptions.IgnoreCase);
        excerpt = Regex.Replace(excerpt, @"^\s*\d{1,2}\s+\p{L}+\s+\d{4}\s*[–—\-:]?\s*", "", RegexOptions.IgnoreCase);
        excerpt = Regex.Replace(excerpt, @"^[\-–—:\s]+", "");
        excerpt = Regex.Replace(excerpt, @"(\p{Ll})(\p{Lu})", "$1: $2");

        return excerpt.Trim();
    }
}
