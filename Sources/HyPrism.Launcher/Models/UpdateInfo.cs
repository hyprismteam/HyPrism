namespace HyPrism.Models;

/// <summary>
/// Information about a pending update.
/// </summary>
public class UpdateInfo
{
    /// <summary>The currently installed game version before this update.</summary>
    public int OldVersion { get; set; }
    /// <summary>The game version that will be installed by this update.</summary>
    public int NewVersion { get; set; }
    /// <summary>Whether the old version directory contained user-generated data.</summary>
    public bool HasOldUserData { get; set; }
    /// <summary>Game branch this update applies to (e.g. "release" or "pre-release").</summary>
    public string Branch { get; set; } = "";
}
