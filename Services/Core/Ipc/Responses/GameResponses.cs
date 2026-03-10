namespace HyPrism.Services.Core.Ipc.Responses;

/// <summary>Push event: game process lifecycle state change.</summary>
public record GameState(string State, int ExitCode);

/// <summary>Push event: error that occurred during game launch or patching.</summary>
public record GameError(string Type, string Message, string? Technical = null);

/// <summary>Push event: download/install progress notification.</summary>
public record ProgressUpdate(
    string State,
    double Progress,
    string MessageKey,
    object[]? Args,
    long DownloadedBytes,
    long TotalBytes);
