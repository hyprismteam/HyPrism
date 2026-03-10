using System.Collections.Generic;

namespace HyPrism.Models;

/// <summary>Paged result set returned from mod search operations.</summary>
public class ModSearchResult
{
    /// <summary>Mods returned in this page of results.</summary>
    public List<ModInfo> Mods { get; set; } = new();
    /// <summary>Total number of mods matching the search query.</summary>
    public int TotalCount { get; set; }
}

/// <summary>Represents a mod available on CurseForge, normalized for launcher display.</summary>
public class ModInfo
{
    /// <summary>Mod identifier (numeric CurseForge ID as string).</summary>
    public string Id { get; set; } = "";
    /// <summary>Display name of the mod.</summary>
    public string Name { get; set; } = "";
    /// <summary>CurseForge URL slug.</summary>
    public string Slug { get; set; } = "";
    /// <summary>Short summary / tagline.</summary>
    public string Summary { get; set; } = "";
    /// <summary>Full HTML description.</summary>
    public string Description { get; set; } = "";
    /// <summary>Primary author display name.</summary>
    public string Author { get; set; } = "";
    /// <summary>Total download count on CurseForge.</summary>
    public int DownloadCount { get; set; }
    /// <summary>URL of the mod icon image.</summary>
    public string IconUrl { get; set; } = "";
    /// <summary>URL of the mod thumbnail image.</summary>
    public string ThumbnailUrl { get; set; } = "";
    /// <summary>Category names the mod belongs to.</summary>
    public List<string> Categories { get; set; } = new();
    /// <summary>ISO 8601 timestamp of the last mod file update.</summary>
    public string DateUpdated { get; set; } = "";
    /// <summary>CurseForge file ID of the most recent release.</summary>
    public string LatestFileId { get; set; } = "";
    /// <summary>Screenshots attached to the mod page.</summary>
    public List<CurseForgeScreenshot> Screenshots { get; set; } = new();
}

/// <summary>Paged file list for a specific mod.</summary>
public class ModFilesResult
{
    public List<ModFileInfo> Files { get; set; } = new();
    public int TotalCount { get; set; }
}

/// <summary>Represents a single file/release of a mod.</summary>
public class ModFileInfo
{
    /// <summary>CurseForge file identifier.</summary>
    public string Id { get; set; } = "";
    /// <summary>Parent mod identifier.</summary>
    public string ModId { get; set; } = "";
    /// <summary>Actual JAR or ZIP file name on disk.</summary>
    public string FileName { get; set; } = "";
    /// <summary>Human-readable version label.</summary>
    public string DisplayName { get; set; } = "";
    /// <summary>Direct download URL from CurseForge CDN.</summary>
    public string DownloadUrl { get; set; } = "";
    /// <summary>File size in bytes.</summary>
    public long FileLength { get; set; }
    /// <summary>ISO 8601 release date of the file.</summary>
    public string FileDate { get; set; } = "";
    /// <summary>CurseForge release type: 1 = Release, 2 = Beta, 3 = Alpha.</summary>
    public int ReleaseType { get; set; }
    /// <summary>Game version tags this file is compatible with.</summary>
    public List<string> GameVersions { get; set; } = new();
    /// <summary>Download count for this specific file.</summary>
    public int DownloadCount { get; set; }
}

/// <summary>A mod category returned from CurseForge.</summary>
public class ModCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
}

/// <summary>Represents a mod that is installed in a game instance.</summary>
public class InstalledMod
{
    /// <summary>Mod identifier (CurseForge numeric ID or local prefix).</summary>
    public string Id { get; set; } = "";
    /// <summary>Display name of the mod.</summary>
    public string Name { get; set; } = "";
    /// <summary>CurseForge URL slug.</summary>
    public string Slug { get; set; } = "";
    /// <summary>Installed version string.</summary>
    public string Version { get; set; } = "";
    /// <summary>CurseForge file identifier of the installed file.</summary>
    public string FileId { get; set; } = "";
    /// <summary>JAR file name on disk (without path).</summary>
    public string FileName { get; set; } = "";
    /// <summary>Whether the mod is enabled; disabled mods have a .disabled extension.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Primary author display name.</summary>
    public string Author { get; set; } = "";
    /// <summary>Short description or summary.</summary>
    public string Description { get; set; } = "";
    /// <summary>URL of the mod icon image.</summary>
    public string IconUrl { get; set; } = "";
    /// <summary>Numeric CurseForge project ID (as string) for update-checking.</summary>
    public string CurseForgeId { get; set; } = "";
    /// <summary>ISO 8601 release date of the installed file.</summary>
    public string FileDate { get; set; } = "";
    
    /// <summary>
    /// CurseForge release type: 1 = Release, 2 = Beta, 3 = Alpha
    /// </summary>
    public int ReleaseType { get; set; } = 1;
    
    /// <summary>Screenshots attached to the mod page.</summary>
    public List<CurseForgeScreenshot> Screenshots { get; set; } = new();
    
    /// <summary>
    /// The latest available file ID from CurseForge (for update checking).
    /// </summary>
    public string LatestFileId { get; set; } = "";
    
    /// <summary>
    /// The latest available version string from CurseForge (for update display).
    /// </summary>
    public string LatestVersion { get; set; } = "";

    /// <summary>
    /// Original file extension used before disabling (e.g. .jar or .zip).
    /// </summary>
    public string DisabledOriginalExtension { get; set; } = "";
}

/// <summary>
/// Entry for mod list import/export
/// </summary>
public class ModListEntry
{
    public string? CurseForgeId { get; set; }
    public string? FileId { get; set; }
    public string? Name { get; set; }
    public string? Version { get; set; }
}

/// <summary>Describes an available update for an installed mod.</summary>
public class ModUpdate
{
    /// <summary>CurseForge mod ID of the mod to update.</summary>
    public string ModId { get; set; } = "";
    /// <summary>Currently installed file ID.</summary>
    public string CurrentFileId { get; set; } = "";
    /// <summary>Latest available file ID on CurseForge.</summary>
    public string LatestFileId { get; set; } = "";
    /// <summary>Latest available file name (for display).</summary>
    public string LatestFileName { get; set; } = "";
}
