using System.Collections.Generic;

namespace HyPrism.Services.Core.Ipc.Responses;

/// <summary>Full settings snapshot exposed to the frontend.</summary>
public record SettingsSnapshot(
    string Language,
    bool MusicEnabled,
    string LauncherBranch,
    string VersionType,
    int SelectedVersion,
    bool CloseAfterLaunch,
    bool LaunchAfterDownload,
    bool ShowDiscordAnnouncements,
    bool DisableNews,
    string BackgroundMode,
    List<string> AvailableBackgrounds,
    string AccentColor,
    bool HasCompletedOnboarding,
    bool OnlineMode,
    string AuthDomain,
    string DataDirectory,
    string InstanceDirectory,
    bool ShowAlphaMods,
    string LauncherVersion,
    string? JavaArguments = null,
    bool? UseCustomJava = null,
    string? CustomJavaPath = null,
    int? SystemMemoryMb = null,
    string? GpuPreference = null,
    string? GameEnvironmentVariables = null,
    bool? UseDualAuth = null,
    bool? LaunchOnStartup = null,
    bool? MinimizeToTray = null);

/// <summary>Information about a download mirror source.</summary>
public record MirrorInfo(
    string Id,
    string Name,
    int Priority,
    bool Enabled,
    string SourceType,
    string Hostname,
    string? Description = null);

/// <summary>Result of a mirror speed test.</summary>
public record MirrorSpeedTestResultDto(
    string MirrorId,
    string MirrorUrl,
    string MirrorName,
    long PingMs,
    double SpeedMBps,
    bool IsAvailable,
    string TestedAt);

/// <summary>Summary of available download sources.</summary>
public record DownloadSourcesSummary(
    bool HasDownloadSources,
    bool HasOfficialAccount,
    int EnabledMirrorCount);

/// <summary>Result of changing the instance directory.</summary>
public record SetInstanceDirResult(
    bool Success,
    string Path,
    bool? Noop = null,
    string? Reason = null,
    string? Error = null);

/// <summary>Result of adding a mirror.</summary>
public record AddMirrorResult(
    bool Success,
    string? Error = null,
    MirrorInfo? Mirror = null);
