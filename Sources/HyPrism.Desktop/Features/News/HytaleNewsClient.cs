// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using HyPrism.Core;
using HyPrism.Core.Infrastructure;
using HyPrism.Desktop.Platform;

namespace HyPrism.Desktop.Features.News;

/// <summary>
/// Fetches official news from the Hytale blog
/// Uses memory and persistent parsed-object caches to reduce API calls and avoid repeating HTML parsing
/// </summary>
public sealed class HytaleNewsClient : IHytaleNewsClient
{
    private readonly HttpClient _httpClient;
    private readonly string? _newsCacheDirectory;
    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Initializes the Hytale news client
    /// </summary>
    /// <param name="httpClient">The HTTP client for fetching news</param>
    /// <param name="appPath">Application paths used to place persistent news data under <c>Cache/News</c></param>
    public HytaleNewsClient(HttpClient httpClient, AppPathConfiguration? appPath = null)
    {
        _httpClient = httpClient;
        _newsCacheDirectory = appPath is null
            ? null
            : Path.Combine(appPath.AppDir, "Cache", "News");

        // Ensure headers are set if they aren't already
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", DesktopApplicationInfo.UserAgent);
        }
    }
    private const string HytaleNewsUrl = "https://hytale.com/news";

    // Cache for Hytale news
    private List<NewsItemResponse>? _hytaleNewsCache;
    private DateTime _hytaleCacheTime = DateTime.MinValue;
    private static readonly SemaphoreSlim _hytaleLock = new(1, 1);

    // Full articles are loaded only when opened. Keeping them separately avoids downloading
    // every article body while populating the feed.
    private readonly ConcurrentDictionary<string, (NewsArticleResponse Article, DateTime CachedAt)> _articleCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<NewsArticleResponse?>>> _articleLoads =
        new(StringComparer.OrdinalIgnoreCase);

    internal int CachedArticleCount => _articleCache.Count;

    private const int CacheExpirationMinutes = 30;
    private const int MaximumCachedArticles = 4;
    private static readonly TimeSpan ArticleDiskCacheLifetime = TimeSpan.FromDays(7);
    private const int ArticleCacheSchemaVersion = 1;

    /// <inheritdoc/>
    public async Task<List<NewsItemResponse>> GetNewsAsync(int count = 10)
    {
        try
        {
            count = Math.Clamp(count, 1, 30);
            return (await GetHytaleNewsAsync(count).ConfigureAwait(false))
                .OrderByDescending(item => ParseDate(item.PublishedAt))
                .Take(count)
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.Error("News", $"Failed to fetch news: {ex.Message}");
            return new List<NewsItemResponse>();
        }
    }

    private async Task<List<NewsItemResponse>> GetHytaleNewsAsync(int count)
    {
        if (_hytaleNewsCache != null && (DateTime.Now - _hytaleCacheTime).TotalMinutes < CacheExpirationMinutes)
            return _hytaleNewsCache.Take(count).ToList();

        await _hytaleLock.WaitAsync();
        try
        {
            if (_hytaleNewsCache != null && (DateTime.Now - _hytaleCacheTime).TotalMinutes < CacheExpirationMinutes)
                return _hytaleNewsCache.Take(count).ToList();

            var diskCached = await TryReadFeedCacheAsync().ConfigureAwait(false);
            if (diskCached is not null)
            {
                _hytaleNewsCache = diskCached;
                _hytaleCacheTime = DateTime.Now;
                return diskCached.Take(count).ToList();
            }

            return await GetHytaleNewsInternalAsync(count).ConfigureAwait(false);
        }
        finally
        {
            _hytaleLock.Release();
        }
    }

    private async Task<List<NewsItemResponse>> GetHytaleNewsInternalAsync(int count)
    {
        try
        {
            // Check cache
            if (_hytaleNewsCache != null &&
                (DateTime.Now - _hytaleCacheTime).TotalMinutes < CacheExpirationMinutes)
            {
                return _hytaleNewsCache.Take(count).ToList();
            }

            Logger.Info("News", "Scraping news from hytale.com/news...");
            var html = await _httpClient.GetStringAsync(HytaleNewsUrl);
            var document = new HtmlParser().ParseDocument(html);
            var news = document.QuerySelectorAll("main article")
                .Select(ParseHytaleFeedItem)
                .Where(item => item is not null)
                .Cast<NewsItemResponse>()
                .Take(30)
                .ToList();

            if (news.Count > 0)
            {
                _hytaleNewsCache = news;
                _hytaleCacheTime = DateTime.Now;
                await WriteFeedCacheAsync(news).ConfigureAwait(false);
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

    /// <inheritdoc />
    public async Task<NewsArticleResponse?> GetNewsArticleAsync(string url)
    {
        var articleUri = ValidateHytaleArticleUri(url);
        var cacheKey = articleUri.AbsoluteUri;

        if (_articleCache.TryGetValue(cacheKey, out var cached) &&
            (DateTime.UtcNow - cached.CachedAt).TotalMinutes < CacheExpirationMinutes)
        {
            return cached.Article;
        }

        var diskCached = await TryReadArticleCacheAsync(cacheKey).ConfigureAwait(false);
        if (diskCached is not null)
        {
            CacheArticle(cacheKey, diskCached);
            return diskCached;
        }

        var pendingLoad = _articleLoads.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<NewsArticleResponse?>>(
                () => FetchAndParseArticleAsync(articleUri, cacheKey),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await pendingLoad.Value.ConfigureAwait(false);
        }
        finally
        {
            _articleLoads.TryRemove(
                new KeyValuePair<string, Lazy<Task<NewsArticleResponse?>>>(cacheKey, pendingLoad));
        }
    }

    private async Task<NewsArticleResponse?> FetchAndParseArticleAsync(Uri articleUri, string cacheKey)
    {
        try
        {
            Logger.Info("News", $"Fetching Hytale article {articleUri.AbsolutePath}...");
            var html = await _httpClient.GetStringAsync(articleUri).ConfigureAwait(false);

            // AngleSharp parsing and normalization are CPU-bound. Keeping them off the UI
            // dispatcher prevents a visible hitch when an uncached article is selected.
            var article = await Task.Run(() => ParseHytaleArticle(html, articleUri)).ConfigureAwait(false);
            if (article is null)
            {
                Logger.Warning("News", $"Hytale article parser returned no content for {articleUri.AbsolutePath}");
                return null;
            }

            CacheArticle(cacheKey, article);
            await WriteArticleCacheAsync(cacheKey, article).ConfigureAwait(false);
            return article;
        }
        catch (Exception ex)
        {
            Logger.Warning("News", $"Failed to fetch Hytale article: {ex.Message}");
            return null;
        }
    }

    private void CacheArticle(string cacheKey, NewsArticleResponse article)
    {
        _articleCache[cacheKey] = (article, DateTime.UtcNow);
        while (_articleCache.Count > MaximumCachedArticles)
        {
            var oldest = _articleCache
                .Where(pair => !string.Equals(
                    pair.Key,
                    cacheKey,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(pair => pair.Value.CachedAt)
                .FirstOrDefault();
            if (string.IsNullOrEmpty(oldest.Key))
                return;

            _articleCache.TryRemove(oldest.Key, out _);
        }
    }

    private async Task<NewsArticleResponse?> TryReadArticleCacheAsync(string cacheKey)
    {
        var cachePath = GetArticleCachePath(cacheKey);
        if (cachePath is null || !File.Exists(cachePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(cachePath).ConfigureAwait(false);
            var entry = JsonSerializer.Deserialize<ArticleDiskCacheEntry>(json, CacheJsonOptions);
            if (entry is null ||
                entry.SchemaVersion != ArticleCacheSchemaVersion ||
                DateTimeOffset.UtcNow - entry.CachedAt > ArticleDiskCacheLifetime ||
                !string.Equals(entry.Url, cacheKey, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(entry.Article.Title))
            {
                return null;
            }

            return entry.Article;
        }
        catch (Exception ex)
        {
            Logger.Warning("News", $"Could not read cached article: {ex.Message}");
            return null;
        }
    }

    private async Task WriteArticleCacheAsync(string cacheKey, NewsArticleResponse article)
    {
        var cachePath = GetArticleCachePath(cacheKey);
        if (cachePath is null)
            return;

        try
        {
            Directory.CreateDirectory(_newsCacheDirectory!);
            var entry = new ArticleDiskCacheEntry(
                ArticleCacheSchemaVersion,
                cacheKey,
                DateTimeOffset.UtcNow,
                article);
            var json = JsonSerializer.Serialize(entry, CacheJsonOptions);
            var temporaryPath = cachePath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, json).ConfigureAwait(false);
            File.Move(temporaryPath, cachePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Warning("News", $"Could not persist article cache: {ex.Message}");
        }
    }

    private string? GetArticleCachePath(string cacheKey)
    {
        if (_newsCacheDirectory is null)
            return null;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey)));
        return ResolveCacheFilePath(
            $"Article-{hash}.json",
            $"article-{hash.ToLowerInvariant()}.json");
    }

    private sealed record ArticleDiskCacheEntry(
        int SchemaVersion,
        string Url,
        DateTimeOffset CachedAt,
        NewsArticleResponse Article);

    private async Task<List<NewsItemResponse>?> TryReadFeedCacheAsync()
    {
        var cachePath = GetFeedCachePath();
        if (cachePath is null || !File.Exists(cachePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(cachePath).ConfigureAwait(false);
            var entry = JsonSerializer.Deserialize<FeedDiskCacheEntry>(json, CacheJsonOptions);
            if (entry is null ||
                entry.SchemaVersion != ArticleCacheSchemaVersion ||
                DateTimeOffset.UtcNow - entry.CachedAt > TimeSpan.FromMinutes(CacheExpirationMinutes) ||
                entry.Items.Count == 0)
            {
                return null;
            }

            return entry.Items;
        }
        catch (Exception ex)
        {
            Logger.Warning("News", $"Could not read cached feed: {ex.Message}");
            return null;
        }
    }

    private async Task WriteFeedCacheAsync(List<NewsItemResponse> items)
    {
        var cachePath = GetFeedCachePath();
        if (cachePath is null)
            return;

        try
        {
            Directory.CreateDirectory(_newsCacheDirectory!);
            var json = JsonSerializer.Serialize(
                new FeedDiskCacheEntry(
                    ArticleCacheSchemaVersion,
                    DateTimeOffset.UtcNow,
                    items),
                CacheJsonOptions);
            var temporaryPath = cachePath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, json).ConfigureAwait(false);
            File.Move(temporaryPath, cachePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Warning("News", $"Could not persist feed cache: {ex.Message}");
        }
    }

    private string? GetFeedCachePath()
        => _newsCacheDirectory is null
            ? null
            : ResolveCacheFilePath("Feed.json", "feed.json");

    private string ResolveCacheFilePath(string canonicalFileName, string legacyFileName)
    {
        var canonicalPath = Path.Combine(_newsCacheDirectory!, canonicalFileName);
        if (!Directory.Exists(_newsCacheDirectory))
            return canonicalPath;

        var files = Directory.EnumerateFiles(_newsCacheDirectory).ToArray();
        var exactCanonicalPath = files.FirstOrDefault(path =>
            string.Equals(Path.GetFileName(path), canonicalFileName, StringComparison.Ordinal));
        if (exactCanonicalPath is not null)
            return exactCanonicalPath;

        var legacyPath = files.FirstOrDefault(path =>
            string.Equals(Path.GetFileName(path), legacyFileName, StringComparison.Ordinal));
        legacyPath ??= files.FirstOrDefault(path =>
            string.Equals(Path.GetFileName(path), canonicalFileName, StringComparison.OrdinalIgnoreCase));
        if (legacyPath is null)
            return canonicalPath;

        try
        {
            MoveWithCanonicalCasing(legacyPath, canonicalPath);
            return canonicalPath;
        }
        catch (Exception ex)
        {
            Logger.Warning("News", $"Could not migrate cache file '{legacyPath}': {ex.Message}");
            return legacyPath;
        }
    }

    private static void MoveWithCanonicalCasing(string sourcePath, string destinationPath)
    {
        if (!string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Move(sourcePath, destinationPath);
            return;
        }

        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(sourcePath)!,
            $".{Guid.NewGuid():N}.json-migration");
        File.Move(sourcePath, temporaryPath);
        try
        {
            File.Move(temporaryPath, destinationPath);
        }
        catch
        {
            File.Move(temporaryPath, sourcePath);
            throw;
        }
    }

    private sealed record FeedDiskCacheEntry(
        int SchemaVersion,
        DateTimeOffset CachedAt,
        List<NewsItemResponse> Items);

    private static NewsItemResponse? ParseHytaleFeedItem(IElement article)
    {
        var titleAnchor = article
            .QuerySelectorAll("h1 a[href], h2 a[href], h3 a[href], h4 a[href], h5 a[href], h6 a[href]")
            .FirstOrDefault(anchor => TryResolveHytaleArticleUri(anchor.GetAttribute("href"), out _));

        if (titleAnchor is null ||
            !TryResolveHytaleArticleUri(titleAnchor.GetAttribute("href"), out var articleUri))
        {
            return null;
        }

        var title = NormalizeWhitespace(titleAnchor.TextContent, preserveOuterWhitespace: false);
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var heading = titleAnchor.ParentElement;
        var excerpt = article.QuerySelector("p")?.TextContent;
        if (string.IsNullOrWhiteSpace(excerpt) && heading?.NextElementSibling is { } sibling)
        {
            var candidate = NormalizeWhitespace(sibling.TextContent, preserveOuterWhitespace: false);
            if (!candidate.StartsWith("Posted by ", StringComparison.OrdinalIgnoreCase) &&
                !TryParsePublicationDate(candidate, out _))
            {
                excerpt = candidate;
            }
        }

        var publishedAt = FindPublishedAt(article);
        var image = article.QuerySelector("img[data-src], img[src]");

        return new NewsItemResponse
        {
            Title = title,
            Excerpt = NormalizeWhitespace(excerpt ?? "", preserveOuterWhitespace: false),
            Url = articleUri.AbsoluteUri,
            Date = publishedAt,
            PublishedAt = publishedAt,
            Author = FindAuthor(article) ?? "Hytale Team",
            ImageUrl = image is null ? null : ResolveHttpUrl(
                image.GetAttribute("data-src") ?? image.GetAttribute("src"), articleUri),
            Categories = GetCategoryLabels(article, articleUri)
        };
    }

    private static NewsArticleResponse? ParseHytaleArticle(string html, Uri requestedUri)
    {
        var document = new HtmlParser().ParseDocument(html);
        var main = document.QuerySelector("main");
        var body = main?.QuerySelector(".post-body");
        var title = NormalizeWhitespace(main?.QuerySelector("h1")?.TextContent ?? "", false);

        if (main is null || body is null || string.IsNullOrWhiteSpace(title))
            return null;

        var canonicalHref = document.QuerySelector("link[rel='canonical']")?.GetAttribute("href");
        var canonicalUri = TryResolveHytaleArticleUri(canonicalHref, out var resolvedCanonical)
            ? resolvedCanonical
            : requestedUri;

        var cover = main.QuerySelectorAll("img[data-src], img[src]")
            .FirstOrDefault(image => !IsDescendantOf(image, body));
        var excerpt = document.QuerySelector("meta[property='og:description']")?.GetAttribute("content")
            ?? document.QuerySelector("meta[name='description']")?.GetAttribute("content")
            ?? body.QuerySelector("p")?.TextContent
            ?? "";

        return new NewsArticleResponse
        {
            Title = title,
            Excerpt = NormalizeWhitespace(excerpt, false),
            Url = canonicalUri.AbsoluteUri,
            PublishedAt = FindPublishedAt(main, body),
            Author = FindAuthor(main, body) ?? "Hytale Team",
            ImageUrl = cover is null ? null : ResolveHttpUrl(
                cover.GetAttribute("data-src") ?? cover.GetAttribute("src"), canonicalUri),
            Categories = GetCategoryLabels(
                main.QuerySelector("nav[aria-label='Post tags']") ?? main,
                canonicalUri),
            Content = ConvertChildren(body, canonicalUri, preserveWhitespace: false)
        };
    }

    private static Uri ValidateHytaleArticleUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var requested) ||
            requested.Scheme != Uri.UriSchemeHttps ||
            !TryResolveHytaleArticleUri(url, out var uri))
        {
            throw new ArgumentException(
                "Only absolute HTTPS URLs below https://hytale.com/news/ are allowed.",
                nameof(url));
        }

        return uri;
    }

    private static bool TryResolveHytaleArticleUri(string? value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(new Uri(HytaleNewsUrl), value, out var candidate) ||
            candidate.Scheme != Uri.UriSchemeHttps ||
            !candidate.IsDefaultPort ||
            !candidate.Host.Equals("hytale.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = candidate.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4 ||
            !segments[0].Equals("news", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
            !int.TryParse(segments[2], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        uri = candidate;
        return true;
    }

    private static string FindPublishedAt(IElement root, IElement? excludedDescendant = null)
    {
        var candidates = root.QuerySelectorAll("time, span")
            .Where(element => excludedDescendant is null || !IsDescendantOf(element, excludedDescendant))
            .Select(element => element.GetAttribute("datetime") ?? element.TextContent)
            .Select(value => NormalizeWhitespace(value, false))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderBy(value => value.Length);

        foreach (var candidate in candidates)
        {
            if (TryParsePublicationDate(candidate, out var parsed))
                return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return "";
    }

    private static bool TryParsePublicationDate(string? value, out DateTimeOffset parsed)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out parsed);
    }

    private static string? FindAuthor(IElement root, IElement? excludedDescendant = null)
    {
        const string Prefix = "Posted by ";
        var candidate = root.QuerySelectorAll("span, p, div")
            .Where(element => excludedDescendant is null || !IsDescendantOf(element, excludedDescendant))
            .Select(element => NormalizeWhitespace(element.TextContent, false))
            .Where(value => value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(value => value.Length)
            .FirstOrDefault();

        return candidate is null ? null : candidate[Prefix.Length..].Trim();
    }

    private static List<string> GetCategoryLabels(IElement root, Uri baseUri)
    {
        return root.QuerySelectorAll("a[href]")
            .Where(anchor => IsCategoryLink(anchor.GetAttribute("href"), baseUri))
            .Select(anchor => NormalizeWhitespace(anchor.TextContent, false))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsCategoryLink(string? href, Uri baseUri)
    {
        var resolved = ResolveHttpUrl(href, baseUri);
        if (resolved is null || !Uri.TryCreate(resolved, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("hytale.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 2 && segments[0].Equals("news", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDescendantOf(IElement element, IElement ancestor)
    {
        for (var current = element.ParentElement; current is not null; current = current.ParentElement)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    private static List<NewsContentNode> ConvertChildren(
        INode parent,
        Uri baseUri,
        bool preserveWhitespace)
    {
        var result = new List<NewsContentNode>();
        foreach (var child in parent.ChildNodes)
        {
            var converted = ConvertContentNode(child, baseUri, preserveWhitespace);
            if (converted is not null)
                result.Add(converted);
        }

        return result;
    }

    private static NewsContentNode? ConvertContentNode(
        INode node,
        Uri baseUri,
        bool preserveWhitespace)
    {
        if (node is IText text)
        {
            var value = NormalizeWhitespace(text.Data, preserveWhitespace);
            return value.Length == 0 ? null : new NewsContentNode { Kind = "text", Text = value };
        }

        if (node is not IElement element)
            return null;

        var tag = element.TagName.ToUpperInvariant();
        if (tag is "SCRIPT" or "STYLE" or "NOSCRIPT" or "IFRAME" or "FORM" or "BUTTON" or "SVG")
            return null;

        if (tag == "IMG")
        {
            var imageUrl = ResolveHttpUrl(
                element.GetAttribute("data-src") ?? element.GetAttribute("src"), baseUri);
            var isEmote = element.ClassList.Contains("emote") ||
                element.ClassList.Contains("emote-sticker");
            return imageUrl is null
                ? null
                : new NewsContentNode
                {
                    Kind = isEmote ? "inline-image" : "image",
                    ImageUrl = imageUrl,
                    AltText = NormalizeWhitespace(element.GetAttribute("alt") ?? "", false),
                    ImagePresentation = element.ClassList.Contains("emote-sticker")
                        ? "sticker"
                        : isEmote
                            ? "emote"
                            : null
                };
        }

        if (tag == "BR")
            return new NewsContentNode { Kind = "line-break" };
        if (tag == "HR")
            return new NewsContentNode { Kind = "divider" };
        if (tag == "PRE")
            return new NewsContentNode { Kind = "code-block", Text = element.TextContent.Trim() };
        if (tag == "CODE")
            return new NewsContentNode { Kind = "inline-code", Text = element.TextContent };

        var kind = tag switch
        {
            "P" => "paragraph",
            "H1" or "H2" or "H3" or "H4" or "H5" or "H6" => "heading",
            "A" => "link",
            "STRONG" or "B" => "bold",
            "EM" or "I" => "italic",
            "BLOCKQUOTE" => "blockquote",
            "DETAILS" => "details",
            "SUMMARY" => "summary",
            "UL" => "unordered-list",
            "OL" => "ordered-list",
            "LI" => "list-item",
            "FIGURE" => "figure",
            "FIGCAPTION" => "caption",
            "TABLE" => "table",
            "TR" => "table-row",
            "TH" => "table-header",
            "TD" => "table-cell",
            _ => "container"
        };

        var keepsInlineWhitespace = tag is "P" or "LI" or "SUMMARY" or "A" or "STRONG" or "B" or "EM" or "I" or
            "H1" or "H2" or "H3" or "H4" or "H5" or "H6" or "FIGCAPTION" or "TH" or "TD" or "SPAN";
        var children = ConvertChildren(element, baseUri, keepsInlineWhitespace);

        // Pretty-printed list markup may place whitespace-only text nodes around
        // block-level <p> children. Leaving those nodes in the inline renderer keeps
        // the paragraph's trailing line break alive and produces a blank line in
        // every item. They are formatting whitespace, not article content.
        if (tag == "LI" && children.Any(child => child.Kind == "paragraph"))
        {
            children.RemoveAll(child =>
                child.Kind == "text" && string.IsNullOrWhiteSpace(child.Text));
        }

        if (children.Count == 0)
            return null;

        // The blog editor sometimes wraps block images in inline formatting tags
        // such as <strong><img ...></strong>. Formatting has no meaning for media,
        // so unwrap it before paragraph-level block normalization.
        var mediaChildren = children
            .Where(child => child.Kind != "text" || !string.IsNullOrWhiteSpace(child.Text))
            .ToList();
        if (tag is "A" or "STRONG" or "B" or "EM" or "I" or "SPAN" &&
            mediaChildren.Count > 0 &&
            mediaChildren.All(child => child.Kind == "image"))
        {
            return mediaChildren.Count == 1
                ? mediaChildren[0]
                : new NewsContentNode { Kind = "container", Children = mediaChildren };
        }

        // Hytale wraps lazy-loaded media in <p> elements. Keep those images as
        // first-class article blocks instead of handing them to the inline text renderer.
        if (tag == "P" && children.Any(child => child.Kind == "image"))
        {
            return children.Count == 1
                ? children[0]
                : new NewsContentNode { Kind = "container", Children = children };
        }

        var result = new NewsContentNode
        {
            Kind = kind,
            Children = children
        };

        if (tag.Length == 2 && tag[0] == 'H' && char.IsDigit(tag[1]))
            result.Level = tag[1] - '0';

        if (tag == "A")
        {
            result.Url = ResolveHttpUrl(element.GetAttribute("href"), baseUri);
            if (result.Url is null)
                result.Kind = "container";
        }

        return result;
    }

    private static string? ResolveHttpUrl(string? value, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(baseUri, value, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    private static string NormalizeWhitespace(string value, bool preserveOuterWhitespace)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var builder = new StringBuilder(value.Length);
        var previousWasWhitespace = false;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                    builder.Append(' ');
                previousWasWhitespace = true;
            }
            else
            {
                builder.Append(character);
                previousWasWhitespace = false;
            }
        }

        var normalized = builder.ToString();
        return preserveOuterWhitespace ? normalized : normalized.Trim();
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
    /// Cleans news excerpt by removing HTML tags, duplicate title, and date prefixes
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
