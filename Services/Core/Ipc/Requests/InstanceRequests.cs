namespace HyPrism.Services.Core.Ipc.Requests;

public record CreateInstanceRequest(string Branch, int Version, string? CustomName = null, bool? IsLatest = null);
public record InstanceIdRequest(string InstanceId);
public record SelectInstanceRequest(string Id);
public record RenameInstanceRequest(string InstanceId, string? CustomName = null);
public record ChangeVersionRequest(string InstanceId, string Branch, int Version);
public record SetIconRequest(string InstanceId, string IconBase64);
public record OpenSaveFolderRequest(string InstanceId, string SaveName);
