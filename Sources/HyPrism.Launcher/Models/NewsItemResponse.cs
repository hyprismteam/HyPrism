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
    
    /// <summary>News source identifier: "hytale" for official blog posts, "hyprism" for launcher announcements.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "hytale"; // "hytale" or "hyprism"
}
