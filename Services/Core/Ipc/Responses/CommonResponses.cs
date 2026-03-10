namespace HyPrism.Services.Core.Ipc.Responses;

/// <summary>Generic success/error reply for operations that only report outcome.</summary>
public record SuccessResult(bool Success, string? Error = null);
