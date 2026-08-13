// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Infrastructure;

namespace HyPrism.Desktop.Features.Settings;

/// <summary>
/// Persists Avalonia preferences through the shared configuration store
/// </summary>
/// <param name="configStore">Shared configuration persistence</param>
public sealed class DesktopSettingsStore(IConfigStore configStore) : IDesktopSettingsStore
{
    private static readonly IReadOnlyList<string> BackgroundFiles = CreateBackgroundFiles();

    /// <inheritdoc/>
    public event Action<string?>? BackgroundChanged;

    /// <inheritdoc/>
    public string Language
    {
        get => configStore.Configuration.Language;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            Save(config => config.Language = value);
        }
    }

    /// <inheritdoc/>
    public bool MusicEnabled
    {
        get => configStore.Configuration.MusicEnabled;
        set => Save(config => config.MusicEnabled = value);
    }

    /// <inheritdoc/>
    public bool CloseAfterLaunch
    {
        get => configStore.Configuration.CloseAfterLaunch;
        set => Save(config => config.CloseAfterLaunch = value);
    }

    /// <inheritdoc/>
    public bool LaunchAfterDownload
    {
        get => configStore.Configuration.LaunchAfterDownload;
        set => Save(config => config.LaunchAfterDownload = value);
    }

    /// <inheritdoc/>
    public bool ShowDiscordAnnouncements
    {
        get => configStore.Configuration.ShowDiscordAnnouncements;
        set => Save(config => config.ShowDiscordAnnouncements = value);
    }

    /// <inheritdoc/>
    public bool DisableNews
    {
        get => configStore.Configuration.DisableNews;
        set => Save(config => config.DisableNews = value);
    }

    /// <inheritdoc/>
    public string BackgroundMode
    {
        get => configStore.Configuration.BackgroundMode;
        set
        {
            var mode = string.IsNullOrWhiteSpace(value) ? "auto" : value;
            Save(config => config.BackgroundMode = mode);
            BackgroundChanged?.Invoke(mode);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> AvailableBackgrounds => BackgroundFiles;

    /// <inheritdoc/>
    public bool OnlineMode
    {
        get => configStore.Configuration.OnlineMode;
        set => Save(config => config.OnlineMode = value);
    }

    /// <inheritdoc/>
    public string AuthDomain
    {
        get => configStore.Configuration.AuthDomain;
        set => Save(config => config.AuthDomain = string.IsNullOrWhiteSpace(value)
            ? "sessions.sanasol.ws"
            : value.Trim());
    }

    /// <inheritdoc/>
    public string JavaArguments
    {
        get => configStore.Configuration.JavaArguments;
        set => Save(config => config.JavaArguments = value?.Trim() ?? string.Empty);
    }

    /// <inheritdoc/>
    public bool UseCustomJava
    {
        get => configStore.Configuration.UseCustomJava;
        set => Save(config => config.UseCustomJava = value);
    }

    /// <inheritdoc/>
    public string CustomJavaPath
    {
        get => configStore.Configuration.CustomJavaPath;
        set => Save(config => config.CustomJavaPath = value?.Trim() ?? string.Empty);
    }

    /// <inheritdoc/>
    public string GpuPreference
    {
        get => configStore.Configuration.GpuPreference;
        set
        {
            var normalized = value?.ToLowerInvariant();
            if (normalized is not ("dedicated" or "integrated" or "auto"))
                normalized = "dedicated";
            Save(config => config.GpuPreference = normalized);
        }
    }

    /// <inheritdoc/>
    public string GameEnvironmentVariables
    {
        get => configStore.Configuration.GameEnvironmentVariables;
        set => Save(config => config.GameEnvironmentVariables = value ?? string.Empty);
    }

    /// <inheritdoc/>
    public string InstanceDirectory => configStore.Configuration.InstanceDirectory;

    /// <inheritdoc/>
    public bool ShowAlphaMods
    {
        get => configStore.Configuration.ShowAlphaMods;
        set => Save(config => config.ShowAlphaMods = value);
    }

    private void Save(Action<HyPrism.Core.Models.Config> update)
    {
        update(configStore.Configuration);
        configStore.SaveConfig();
    }

    private static IReadOnlyList<string> CreateBackgroundFiles()
    {
        var pngIds = new HashSet<int> { 4, 6, 9, 12, 16, 19 };
        var ids = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 22, 23, 24, 25, 26, 27, 28, 29, 30 };
        return ids.Select(id => $"bg_{id}.{(pngIds.Contains(id) ? "png" : "jpg")}").ToArray();
    }
}
