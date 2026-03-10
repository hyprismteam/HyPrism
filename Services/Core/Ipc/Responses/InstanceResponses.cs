namespace HyPrism.Services.Core.Ipc.Responses;

/// <summary>Information about a world save inside an instance.</summary>
public record SaveInfo(
    string Name,
    string? PreviewPath = null,
    string? LastModified = null,
    long? SizeBytes = null);

/// <summary>Minimal app-level configuration exposed to the frontend.</summary>
public record AppConfig(string Language, string DataDirectory);
