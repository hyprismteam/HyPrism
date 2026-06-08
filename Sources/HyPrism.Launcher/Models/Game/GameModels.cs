using System.Text.Json.Serialization;

namespace HyPrism.Models;

/// <summary>
/// Represents a cosmetic item from Assets.zip
/// </summary>
public class CosmeticItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Detailed information about an installed game instance.
/// </summary>
public class InstalledVersionInfo
{
    public int Version { get; set; }
    public string Branch { get; set; } = "";
    public string Path { get; set; } = "";
    public long UserDataSize { get; set; }
    public bool HasUserData { get; set; }
    /// <summary>
    /// When the instance was created (UTC).
    /// </summary>
    public DateTime? CreatedAt { get; set; }
    /// <summary>
    /// Total playtime in seconds.
    /// </summary>
    public int PlayTimeSeconds { get; set; }
    /// <summary>
    /// Formatted playtime string (HH:MM:SS or Dd HH:MM:SS).
    /// </summary>
    public string PlayTimeFormatted => FormatPlayTime(PlayTimeSeconds);

    private static string FormatPlayTime(int seconds)
    {
        if (seconds <= 0) return "0:00:00";

        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.Days > 0)
            return $"{ts.Days}d {ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        return $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }
}

/// <summary>
/// Version status response for latest instance.
/// </summary>
public class VersionStatus
{
    /// <summary>
    /// Status: "not_installed", "update_available", "current", "none", "error"
    /// </summary>
    public string Status { get; set; } = "none";

    /// <summary>
    /// Currently installed version number (0 for latest).
    /// </summary>
    public int InstalledVersion { get; set; }

    /// <summary>
    /// Latest available version number.
    /// </summary>
    public int LatestVersion { get; set; }
}
