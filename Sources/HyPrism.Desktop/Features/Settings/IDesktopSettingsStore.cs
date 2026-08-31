// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Desktop.Features.Settings;

/// <summary>
/// Exposes persisted preferences consumed by the Avalonia application
/// </summary>
public interface IDesktopSettingsStore
{
    /// <summary>Raised after the selected background changes</summary>
    event Action<string?>? BackgroundChanged;

    /// <summary>Gets or sets the interface language code</summary>
    string Language { get; set; }

    /// <summary>Gets or sets whether background music is enabled</summary>
    bool MusicEnabled { get; set; }

    /// <summary>Gets or sets whether Desktop closes after starting the game</summary>
    bool CloseAfterLaunch { get; set; }

    /// <summary>Gets or sets whether Discord announcements are displayed</summary>
    bool ShowDiscordAnnouncements { get; set; }

    /// <summary>Gets or sets whether the news page is disabled</summary>
    bool DisableNews { get; set; }

    /// <summary>Gets or sets the selected background mode or file name</summary>
    string BackgroundMode { get; set; }

    /// <summary>Gets the background files bundled with Desktop</summary>
    IReadOnlyList<string> AvailableBackgrounds { get; }

    /// <summary>Gets or sets whether authenticated game mode is enabled</summary>
    bool OnlineMode { get; set; }

    /// <summary>Gets or sets the authentication service domain</summary>
    string AuthDomain { get; set; }

    /// <summary>Gets or sets custom Java arguments</summary>
    string JavaArguments { get; set; }

    /// <summary>Gets or sets whether a custom Java executable is used</summary>
    bool UseCustomJava { get; set; }

    /// <summary>Gets or sets the custom Java executable path</summary>
    string CustomJavaPath { get; set; }

    /// <summary>Gets or sets the preferred GPU selection mode</summary>
    string GpuPreference { get; set; }

    /// <summary>Gets or sets custom environment variables passed to the game</summary>
    string GameEnvironmentVariables { get; set; }

    /// <summary>Gets the configured game instance root</summary>
    string InstanceDirectory { get; }

    /// <summary>Gets the default game instance root</summary>
    string DefaultInstanceDirectory { get; }

    /// <summary>Gets the launcher data root</summary>
    string LauncherDataDirectory { get; }

    /// <summary>
    /// Changes the game instance root and moves existing instance data
    /// </summary>
    /// <param name="path">New root, or an empty value to restore the default root</param>
    /// <returns><see langword="true"/> when the root was changed successfully</returns>
    Task<bool> SetInstanceDirectoryAsync(string path);

    /// <summary>
    /// Measures launcher and instance storage grouped by file purpose
    /// </summary>
    /// <param name="cancellationToken">Cancellation requested when the view is closed</param>
    /// <returns>Current storage usage</returns>
    Task<LauncherStorageUsage> GetLauncherStorageUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets or sets whether alpha mod releases are visible</summary>
    bool ShowAlphaMods { get; set; }
}
