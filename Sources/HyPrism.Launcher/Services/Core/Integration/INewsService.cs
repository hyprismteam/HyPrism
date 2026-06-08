using HyPrism.Models;

namespace HyPrism.Services.Core.Integration;

/// <summary>
/// Provides news aggregation from multiple sources including Hytale official news and HyPrism announcements.
/// </summary>
public interface INewsService
{
    /// <summary>
    /// Retrieves news items from the specified sources.
    /// </summary>
    /// <param name="count">The maximum number of news items to retrieve. Defaults to 10.</param>
    /// <param name="source">The news source filter. Use <see cref="NewsSource.All"/> for aggregated results.</param>
    /// <returns>A list of <see cref="NewsItemResponse"/> objects sorted by date descending.</returns>
    Task<List<NewsItemResponse>> GetNewsAsync(int count = 10, NewsSource source = NewsSource.All);
}
