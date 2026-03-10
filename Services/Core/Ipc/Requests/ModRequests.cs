using System.Collections.Generic;

namespace HyPrism.Services.Core.Ipc.Requests;

public record ModSearchRequest(
    string Query,
    int Page,
    int PageSize,
    int SortField,
    int SortOrder,
    List<string> Categories);

public record ModInstallRequest(string ModId, string FileId, string? InstanceId = null);
public record ModUninstallRequest(string ModId, string? InstanceId = null);
public record ModToggleRequest(string ModId, string? InstanceId = null);
public record ModFilesRequest(string ModId, int? Page = null, int? PageSize = null);
public record ModInfoRequest(string ModId);
public record ModChangelogRequest(string ModId, string FileId);
public record ModInstalledRequest(string? InstanceId = null);
public record ModCheckUpdatesRequest(string? InstanceId = null);
public record ModInstallLocalRequest(string SourcePath, string? InstanceId = null);
public record ModInstallBase64Request(string FileName, string Base64Content, string? InstanceId = null);
public record ModExportRequest(string? InstanceId = null, string ExportPath = "", string? ExportType = null);
public record ModImportListRequest(string ListPath);
public record ModOpenFolderRequest(string? InstanceId = null);
