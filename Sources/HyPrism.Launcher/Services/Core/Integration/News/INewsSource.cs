using HyPrism.Models;

namespace HyPrism.Services.Core.Integration.News;

/// <summary>
/// Type of news source.
/// </summary>
public enum NewsSourceType
{
    /// <summary>Official Hytale blog.</summary>
    Hytale,

    /// <summary>HyPrism GitHub releases.</summary>
    HyPrism
}

/// <summary>
/// Unified interface for news data sources.
/// Both Hytale blog and HyPrism releases implement this interface,
/// allowing the NewsService to treat them uniformly.
/// </summary>
public interface INewsSource
{
    /// <summary>
    /// Unique identifier for this source.
    /// </summary>
    string SourceId { get; }

    /// <summary>
    /// Type of news source.
    /// </summary>
    NewsSourceType Type { get; }

    /// <summary>
    /// Priority for merging news (lower = higher priority).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Fetches news items from this source.
    /// </summary>
    /// <param name="maxItems">Maximum number of items to return.</param>
    /// <param name="forceRefresh">Whether to bypass cache.</param>
    /// <returns>List of news items.</returns>
    Task<List<NewsItemResponse>> FetchNewsAsync(int maxItems = 20, bool forceRefresh = false);

    /// <summary>
    /// Clears the cached news data.
    /// </summary>
    void ClearCache();
}
