// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Models;

namespace HyPrism.Core.Game.Sources;

/// <summary>
/// Result of mirror auto-discovery attempt
/// </summary>
public class DiscoveryResult
{
    /// <summary>Whether mirror discovery produced a usable definition</summary>
    public bool Success { get; set; }
    /// <summary>Error description when discovery fails</summary>
    public string? Error { get; set; }
    /// <summary>Discovered mirror definition, when discovery succeeds</summary>
    public MirrorMeta? Mirror { get; set; }
    /// <summary>Detected mirror layout identifier, when available</summary>
    public string? DetectedType { get; set; }
}

/// <summary>
/// Service for automatically discovering mirror configuration from a URL.
/// Attempts to detect mirror type (pattern/json-index) and build a <see cref="MirrorMeta"/> schema
/// </summary>
public interface IMirrorDiscovery
{
    /// <summary>
    /// Attempts to discover mirror configuration from a URL.
    /// Tries multiple detection strategies with extensive endpoint probing
    /// </summary>
    /// <param name="url">The mirror base URL to inspect</param>
    /// <param name="headers">Optional custom HTTP headers (supports <c>{hytaleAgent}</c> variable)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The inferred mirror definition and discovery diagnostics</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="url"/> is not a valid absolute URL</exception>
    Task<DiscoveryResult> DiscoverMirrorAsync(string url, Dictionary<string, string>? headers = null, CancellationToken ct = default);
}
