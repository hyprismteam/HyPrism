namespace HyPrism.Models;

/// <summary>
/// Metadata stored in each instance's meta.json file.
/// Contains instance-specific configuration and identification.
/// </summary>
public class InstanceMeta
{
    /// <summary>
    /// Unique identifier for this instance. Generated on creation.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Display name of the instance. Can be customized by the user.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Game branch (release, pre-release).
    /// </summary>
    public string Branch { get; set; } = "release";

    /// <summary>
    /// Installed game version number.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// When this instance was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last time this instance was launched.
    /// </summary>
    public DateTime? LastPlayedAt { get; set; }

    /// <summary>
    /// Whether this is the "latest" rolling instance that auto-updates.
    /// </summary>
    public bool IsLatest { get; set; } = false;

    /// <summary>
    /// The actual installed version number.
    /// For "latest" instances (Version == 0), this tracks which version is currently
    /// on disk after download/update. Updated by SaveLatestInfo / PatchManager.
    /// </summary>
    public int InstalledVersion { get; set; }

    /// <summary>
    /// The version that was being installed/patched before an interruption.
    /// If non-zero on next launch, the launcher will attempt to re-patch from
    /// <see cref="InstalledVersion"/> to this version.
    /// Reset to 0 after successful install/update.
    /// </summary>
    public int PendingVersion { get; set; }

    /// <summary>
    /// Notes or description for this instance.
    /// </summary>
    public string? Notes { get; set; }
}

/// <summary>
/// Lightweight instance reference stored in Config for fallback and quick lookup.
/// This is a minimal copy of InstanceMeta to avoid reading meta.json for every operation.
/// </summary>
public class InstanceInfo
{
    /// <summary>
    /// Unique identifier matching InstanceMeta.Id.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Display name of the instance.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Game branch (release, pre-release).
    /// </summary>
    public string Branch { get; set; } = "release";

    /// <summary>
    /// Installed game version number.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Whether the game files are actually installed (valid client/server executables exist).
    /// </summary>
    public bool IsInstalled { get; set; } = true;
}
