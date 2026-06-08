using System.Collections.Generic;

namespace HyPrism.Models;

/// <summary>Paged search response from the CurseForge Search API.</summary>
public class CurseForgeSearchResponse
{
    public List<CurseForgeMod>? Data { get; set; }
    public CurseForgePagination? Pagination { get; set; }
}

/// <summary>Single-mod response from the CurseForge Mod API.</summary>
public class CurseForgeModResponse
{
    public CurseForgeMod? Data { get; set; }
}

/// <summary>Pagination metadata included in CurseForge list responses.</summary>
public class CurseForgePagination
{
    public int Index { get; set; }
    public int PageSize { get; set; }
    public int ResultCount { get; set; }
    public int TotalCount { get; set; }
}

/// <summary>Represents a single mod entry from the CurseForge API.</summary>
public class CurseForgeMod
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? Summary { get; set; }
    public int DownloadCount { get; set; }
    public string? DateCreated { get; set; }
    public string? DateModified { get; set; }
    public CurseForgeLogo? Logo { get; set; }
    public List<CurseForgeCategory>? Categories { get; set; }
    public List<CurseForgeAuthor>? Authors { get; set; }
    public List<CurseForgeFile>? LatestFiles { get; set; }
    public List<CurseForgeScreenshot>? Screenshots { get; set; }
}

/// <summary>A screenshot attached to a CurseForge mod.</summary>
public class CurseForgeScreenshot
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Url { get; set; }
}

/// <summary>Logo/thumbnail image for a CurseForge mod.</summary>
public class CurseForgeLogo
{
    public int Id { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Url { get; set; }
}

/// <summary>A mod category from CurseForge.</summary>
public class CurseForgeCategory
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public int ParentCategoryId { get; set; }
    public bool? IsClass { get; set; }
}

/// <summary>Author entry for a CurseForge mod.</summary>
public class CurseForgeAuthor
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Url { get; set; }
}

/// <summary>A specific file/release attached to a CurseForge mod.</summary>
public class CurseForgeFile
{
    public int Id { get; set; }
    public int ModId { get; set; }
    public string? DisplayName { get; set; }
    public string? FileName { get; set; }
    public string? DownloadUrl { get; set; }
    public long FileLength { get; set; }
    public string? FileDate { get; set; }
    public int ReleaseType { get; set; }
    public int DownloadCount { get; set; }
    public List<string>? GameVersions { get; set; }
}

/// <summary>Categories list response from the CurseForge API.</summary>
public class CurseForgeCategoriesResponse
{
    public List<CurseForgeCategory>? Data { get; set; }
}

/// <summary>Paged files list response from the CurseForge Files API.</summary>
public class CurseForgeFilesResponse
{
    public List<CurseForgeFile>? Data { get; set; }
    public CurseForgePagination? Pagination { get; set; }
}

/// <summary>Single-file response from the CurseForge Files API.</summary>
public class CurseForgeFileResponse
{
    public CurseForgeFile? Data { get; set; }
}

/// <summary>Download URL response from the CurseForge Files API.</summary>
public class CurseForgeDownloadUrlResponse
{
    public string? Data { get; set; }
}
