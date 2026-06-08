using HyPrism.Models;

namespace HyPrism.Services.Game.Sources;

/// <summary>
/// Result of mirror auto-discovery attempt.
/// </summary>
public class DiscoveryResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public MirrorMeta? Mirror { get; set; }
    public string? DetectedType { get; set; }
}

/// <summary>
/// Service for automatically discovering mirror configuration from a URL.
/// Attempts to detect mirror type (pattern/json-index) and build a <see cref="MirrorMeta"/> schema.
/// </summary>
public interface IMirrorDiscoveryService
{
    /// <summary>
    /// Attempts to discover mirror configuration from a URL.
    /// Tries multiple detection strategies with extensive endpoint probing.
    /// </summary>
    /// <param name="url">The mirror base URL to inspect.</param>
    /// <param name="headers">Optional custom HTTP headers (supports <c>{hytaleAgent}</c> variable).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<DiscoveryResult> DiscoverMirrorAsync(string url, Dictionary<string, string>? headers = null, CancellationToken ct = default);
}
