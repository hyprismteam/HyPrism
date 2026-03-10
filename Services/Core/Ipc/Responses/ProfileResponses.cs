namespace HyPrism.Services.Core.Ipc.Responses;

/// <summary>Lightweight current profile snapshot (nick + uuid + avatar).</summary>
public record ProfileSnapshot(string Nick, string Uuid, string? AvatarPath = null);

/// <summary>IPC-facing profile DTO (differs from Models.Profile which has internal fields).</summary>
public record IpcProfile(
    string Id,
    string Name,
    string? Uuid = null,
    bool? IsOfficial = null,
    string? Avatar = null,
    string? FolderName = null);
