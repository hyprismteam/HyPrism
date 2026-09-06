// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Desktop.Features.News;

/// <summary>
/// Provides official Hytale news for the Avalonia interface
/// </summary>
public interface IHytaleNewsClient
{
    /// <summary>
    /// Retrieves official Hytale news items
    /// Implementations may persist the parsed feed so it remains available between application runs
    /// </summary>
    /// <param name="count">The maximum number of news items to retrieve. Defaults to 10</param>
    /// <returns>A list of <see cref="NewsItemResponse"/> objects sorted by date descending</returns>
    Task<List<NewsItemResponse>> GetNewsAsync(int count = 10);

    /// <summary>
    /// Retrieves and sanitizes the complete body of an official Hytale news article.
    /// The article is fetched lazily, may be restored from persistent cache, and is returned as a
    /// formatting-aware content tree
    /// </summary>
    /// <param name="url">An absolute URL below <c>https://hytale.com/news/</c></param>
    /// <returns>The parsed article, or <see langword="null"/> when it cannot be downloaded or parsed</returns>
    Task<NewsArticleResponse?> GetNewsArticleAsync(string url);
}
