namespace HyPrism.Services.Core.Ipc.Requests;

public record LaunchGameRequest(string? InstanceId = null, bool? LaunchAfterDownload = null);
public record GetVersionsRequest(string? Branch = null);
public record GetLogsRequest(int? Count = null);
