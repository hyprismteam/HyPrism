// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core;
using HyPrism.Core.Infrastructure;

namespace HyPrism.Desktop.Features.Settings;

/// <summary>
/// Persists Avalonia preferences through the shared configuration store
/// </summary>
public sealed class DesktopSettingsStore : IDesktopSettingsStore
{
    private static readonly IReadOnlyList<string> BackgroundFiles = CreateBackgroundFiles();
    private readonly IConfigStore _configStore;
    private readonly string _appDirectory;

    public DesktopSettingsStore(IConfigStore configStore)
        : this(configStore, new AppPathConfiguration(LauncherUtilities.GetEffectiveAppDir()))
    {
    }

    public DesktopSettingsStore(IConfigStore configStore, AppPathConfiguration appPath)
    {
        _configStore = configStore;
        _appDirectory = Path.GetFullPath(appPath.AppDir);
    }

    /// <inheritdoc/>
    public event Action<string?>? BackgroundChanged;

    /// <inheritdoc/>
    public string Language
    {
        get => _configStore.Configuration.Language;
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
        get => _configStore.Configuration.MusicEnabled;
        set => Save(config => config.MusicEnabled = value);
    }

    /// <inheritdoc/>
    public bool CloseAfterLaunch
    {
        get => _configStore.Configuration.CloseAfterLaunch;
        set => Save(config => config.CloseAfterLaunch = value);
    }

    /// <inheritdoc/>
    public bool ShowDiscordAnnouncements
    {
        get => _configStore.Configuration.ShowDiscordAnnouncements;
        set => Save(config => config.ShowDiscordAnnouncements = value);
    }

    /// <inheritdoc/>
    public bool DisableNews
    {
        get => _configStore.Configuration.DisableNews;
        set => Save(config => config.DisableNews = value);
    }

    /// <inheritdoc/>
    public string BackgroundMode
    {
        get => _configStore.Configuration.BackgroundMode;
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
        get => _configStore.Configuration.OnlineMode;
        set => Save(config => config.OnlineMode = value);
    }

    /// <inheritdoc/>
    public string AuthDomain
    {
        get => _configStore.Configuration.AuthDomain;
        set => Save(config => config.AuthDomain = string.IsNullOrWhiteSpace(value)
            ? "sessions.sanasol.ws"
            : value.Trim());
    }

    /// <inheritdoc/>
    public string JavaArguments
    {
        get => _configStore.Configuration.JavaArguments;
        set => Save(config => config.JavaArguments = value?.Trim() ?? string.Empty);
    }

    /// <inheritdoc/>
    public bool UseCustomJava
    {
        get => _configStore.Configuration.UseCustomJava;
        set => Save(config => config.UseCustomJava = value);
    }

    /// <inheritdoc/>
    public string CustomJavaPath
    {
        get => _configStore.Configuration.CustomJavaPath;
        set => Save(config => config.CustomJavaPath = value?.Trim() ?? string.Empty);
    }

    /// <inheritdoc/>
    public string GpuPreference
    {
        get => _configStore.Configuration.GpuPreference;
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
        get => _configStore.Configuration.GameEnvironmentVariables;
        set => Save(config => config.GameEnvironmentVariables = value ?? string.Empty);
    }

    /// <inheritdoc/>
    public string InstanceDirectory => _configStore.Configuration.InstanceDirectory;

    /// <inheritdoc/>
    public string DefaultInstanceDirectory => Path.Combine(_appDirectory, "Instances");

    /// <inheritdoc/>
    public string LauncherDataDirectory => _appDirectory;

    /// <inheritdoc/>
    public async Task<bool> SetInstanceDirectoryAsync(string path)
    {
        var resetToDefault = string.IsNullOrWhiteSpace(path);
        var targetDirectory = resetToDefault
            ? DefaultInstanceDirectory
            : NormalizeDirectory(path);
        var currentDirectory = string.IsNullOrWhiteSpace(InstanceDirectory)
            ? DefaultInstanceDirectory
            : NormalizeDirectory(InstanceDirectory);

        if (DirectoriesEqual(currentDirectory, targetDirectory))
        {
            if (resetToDefault && !string.IsNullOrWhiteSpace(InstanceDirectory))
                await _configStore.SetInstanceDirectoryAsync(string.Empty);
            return true;
        }

        if (IsNestedDirectory(currentDirectory, targetDirectory) ||
            IsNestedDirectory(targetDirectory, currentDirectory))
        {
            Logger.Warning("Settings", "Instance storage cannot be moved into its current directory tree");
            return false;
        }

        try
        {
            await Task.Run(() => CopyDirectory(currentDirectory, targetDirectory));
            var saved = resetToDefault
                ? await SaveDefaultInstanceDirectoryAsync()
                : await _configStore.SetInstanceDirectoryAsync(targetDirectory) is not null;
            if (!saved)
                return false;

            TryRemovePreviousDirectory(currentDirectory);
            return true;
        }
        catch (Exception exception)
        {
            Logger.Error("Settings", $"Failed to move instance storage: {exception.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public Task<LauncherStorageUsage> GetLauncherStorageUsageAsync(CancellationToken cancellationToken = default)
        => LauncherStorageUsageAnalyzer.MeasureAsync(
            LauncherDataDirectory,
            string.IsNullOrWhiteSpace(InstanceDirectory)
                ? DefaultInstanceDirectory
                : NormalizeDirectory(InstanceDirectory),
            cancellationToken);

    /// <inheritdoc/>
    public bool ShowAlphaMods
    {
        get => _configStore.Configuration.ShowAlphaMods;
        set => Save(config => config.ShowAlphaMods = value);
    }

    private void Save(Action<HyPrism.Core.Models.Config> update)
    {
        update(_configStore.Configuration);
        _configStore.SaveConfig();
    }

    private async Task<bool> SaveDefaultInstanceDirectoryAsync()
    {
        await _configStore.SetInstanceDirectoryAsync(string.Empty);
        return string.IsNullOrWhiteSpace(_configStore.Configuration.InstanceDirectory);
    }

    private string NormalizeDirectory(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        return Path.GetFullPath(Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(_appDirectory, expanded));
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        if (!Directory.Exists(sourceDirectory))
            return;

        foreach (var directory in Directory.EnumerateDirectories(
                     sourceDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(
                targetDirectory,
                Path.GetRelativePath(sourceDirectory, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(
                targetDirectory,
                Path.GetRelativePath(sourceDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static void TryRemovePreviousDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception)
        {
            Logger.Warning("Settings", $"Instance storage moved, but the old directory could not be removed: {exception.Message}");
        }
    }

    private static bool IsNestedDirectory(string parent, string candidate)
    {
        var parentWithSeparator = Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(parentWithSeparator, PathComparison);
    }

    private static bool DirectoriesEqual(string first, string second)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(first),
            Path.TrimEndingDirectorySeparator(second),
            PathComparison);

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static IReadOnlyList<string> CreateBackgroundFiles()
    {
        var pngIds = new HashSet<int> { 4, 6, 9, 12, 16, 19 };
        var ids = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 22, 23, 24, 25, 26, 27, 28, 29, 30 };
        return ids.Select(id => $"bg_{id}.{(pngIds.Contains(id) ? "png" : "jpg")}").ToArray();
    }
}
