using System;

namespace HyPrism.Services;

/// <summary>
/// Stores information about the latest installed game instance version.
/// </summary>
public sealed class LatestInstanceInfo
{
    /// <summary>Latest installed game version number.</summary>
    public int Version { get; set; }
    /// <summary>Timestamp when this version info was last updated.</summary>
    public DateTime UpdatedAt { get; set; }
}
