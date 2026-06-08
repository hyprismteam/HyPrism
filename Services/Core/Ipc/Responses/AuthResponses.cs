namespace HyPrism.Services.Core.Ipc.Responses;

/// <summary>Hytale authentication status snapshot.</summary>
public record HytaleAuthStatus(
    bool LoggedIn,
    string? Username = null,
    string? Uuid = null,
    string? Error = null,
    string? ErrorType = null,
    List<HytaleAccountProfile>? AccountProfiles = null);

public record HytaleAccountProfile(string Username, string Uuid);

/// <summary>Result of a ping to the auth server.</summary>
public record AuthServerPingResult(
    bool IsAvailable,
    long PingMs,
    string AuthDomain,
    string CheckedAt,
    bool IsOfficial,
    string? Error = null);
