// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Serialization;

namespace HyPrism.Models;

/// <summary>A news item returned from the launcher news API or Hytale blog feed.</summary>
public class NewsItemResponse
{
    /// <summary>Headline/title of the news article.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";
    
    /// <summary>Short excerpt or summary text.</summary>
    [JsonPropertyName("excerpt")]
    public string Excerpt { get; set; } = "";
    
    /// <summary>Full URL to the article page.</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
    
    /// <summary>Publication date (ISO 8601 or display string).</summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    /// <summary>ISO 8601 publication timestamp.</summary>
    [JsonPropertyName("publishedAt")]
    public string PublishedAt { get; set; } = "";
    
    /// <summary>Display name of the article author.</summary>
    [JsonPropertyName("author")]
    public string Author { get; set; } = "";
    
    /// <summary>URL of the article cover image; may be null.</summary>
    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; set; }

    /// <summary>Category labels assigned by the news publisher.</summary>
    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = [];
    
    /// <summary>News source identifier: "hytale" for official blog posts, "hyprism" for launcher announcements.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "hytale"; // "hytale" or "hyprism"
}

/// <summary>A complete, sanitized news article ready for a native renderer.</summary>
public sealed class NewsArticleResponse
{
    /// <summary>Headline/title of the article.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    /// <summary>Short summary shown in the news feed.</summary>
    [JsonPropertyName("excerpt")]
    public string Excerpt { get; set; } = "";

    /// <summary>Canonical URL of the article.</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    /// <summary>Publication date in ISO 8601 format.</summary>
    [JsonPropertyName("publishedAt")]
    public string PublishedAt { get; set; } = "";

    /// <summary>Display name of the article author.</summary>
    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    /// <summary>URL of the article cover image; may be null.</summary>
    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; set; }

    /// <summary>Category labels assigned by the publisher.</summary>
    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = [];

    /// <summary>Sanitized article body represented as formatting-aware content nodes.</summary>
    [JsonPropertyName("content")]
    public List<NewsContentNode> Content { get; set; } = [];
}

/// <summary>
/// A safe node in a news article content tree. Supported kinds include text, paragraph,
/// heading, image, inline-image, link, bold, italic, blockquote, details, summary,
/// list, list-item, code and divider.
/// </summary>
public sealed class NewsContentNode
{
    /// <summary>Stable renderer-facing node kind.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    /// <summary>Text payload for text and code nodes.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>Safe absolute URL for link nodes.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>Safe absolute URL for image nodes.</summary>
    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; set; }

    /// <summary>Accessible alternative text for image nodes.</summary>
    [JsonPropertyName("altText")]
    public string? AltText { get; set; }

    /// <summary>
    /// Optional renderer hint for inline media, for example "emote" or "sticker".
    /// </summary>
    [JsonPropertyName("imagePresentation")]
    public string? ImagePresentation { get; set; }

    /// <summary>Heading level from 1 through 6.</summary>
    [JsonPropertyName("level")]
    public int? Level { get; set; }

    /// <summary>Nested inline or block content.</summary>
    [JsonPropertyName("children")]
    public List<NewsContentNode> Children { get; set; } = [];
}
