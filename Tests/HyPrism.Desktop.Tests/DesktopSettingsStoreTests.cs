// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core;
using HyPrism.Core.Infrastructure;
using HyPrism.Desktop.Features.Settings;
using Moq;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class DesktopSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"HyPrismDesktopSettings_{Guid.NewGuid():N}");
    private readonly JsonConfigStore _config;
    private readonly DesktopSettingsStore _settings;

    public DesktopSettingsStoreTests()
    {
        _config = new JsonConfigStore(_directory);
        _settings = new DesktopSettingsStore(_config, new AppPathConfiguration(_directory));
    }

    [Fact]
    public void Language_PersistsSupportedPreference()
    {
        _settings.Language = "ru-RU";

        Assert.Equal("ru-RU", _config.Configuration.Language);
    }

    [Fact]
    public void GpuPreference_NormalizesUnknownValue()
    {
        _settings.GpuPreference = "unsupported";

        Assert.Equal("dedicated", _config.Configuration.GpuPreference);
    }

    [Fact]
    public void BackgroundMode_RaisesChangeAfterSaving()
    {
        string? received = null;
        _settings.BackgroundChanged += value => received = value;

        _settings.BackgroundMode = "bg_4.png";

        Assert.Equal("bg_4.png", _config.Configuration.BackgroundMode);
        Assert.Equal("bg_4.png", received);
    }

    [Fact]
    public void AvailableBackgrounds_ContainsAutoPickerAssets()
    {
        Assert.Contains("bg_1.jpg", _settings.AvailableBackgrounds);
        Assert.Contains("bg_4.png", _settings.AvailableBackgrounds);
    }

    [Fact]
    public void DataDirectories_ExposeEffectiveStorageLocations()
    {
        Assert.Equal(Path.Combine(_directory, "Instances"), _settings.DefaultInstanceDirectory);
        Assert.Equal(Path.GetFullPath(_directory), _settings.LauncherDataDirectory);
    }

    [Fact]
    public async Task SetInstanceDirectoryAsync_MovesExistingDataAndPersistsTheNewRoot()
    {
        var originalDirectory = _settings.DefaultInstanceDirectory;
        var targetDirectory = Path.Combine(Path.GetTempPath(), $"HyPrismInstances_{Guid.NewGuid():N}");
        var originalFile = Path.Combine(originalDirectory, "release", "instance.json");
        Directory.CreateDirectory(Path.GetDirectoryName(originalFile)!);
        await File.WriteAllTextAsync(
            originalFile,
            "instance metadata",
            TestContext.Current.CancellationToken);
        var progressReports = new List<InstanceDirectoryMoveProgress>();
        var progress = new Mock<IProgress<InstanceDirectoryMoveProgress>>();
        progress
            .Setup(service => service.Report(It.IsAny<InstanceDirectoryMoveProgress>()))
            .Callback<InstanceDirectoryMoveProgress>(progressReports.Add);

        try
        {
            var changed = await _settings.SetInstanceDirectoryAsync(
                targetDirectory,
                TestContext.Current.CancellationToken,
                progress.Object);

            Assert.True(changed);
            Assert.Equal(Path.GetFullPath(targetDirectory), _config.Configuration.InstanceDirectory);
            Assert.Equal(
                "instance metadata",
                await File.ReadAllTextAsync(
                    Path.Combine(targetDirectory, "release", "instance.json"),
                    TestContext.Current.CancellationToken));
            Assert.False(Directory.Exists(originalDirectory));
            Assert.Equal(0, progressReports[0].BytesCopied);
            Assert.Equal("instance metadata".Length, progressReports[0].TotalBytes);
            Assert.Equal(100, progressReports[^1].Percentage);
        }
        finally
        {
            if (Directory.Exists(targetDirectory))
                Directory.Delete(targetDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SetInstanceDirectoryAsync_ResetMovesDataBackToDefaultRoot()
    {
        var customDirectory = Path.Combine(Path.GetTempPath(), $"HyPrismInstances_{Guid.NewGuid():N}");
        var customFile = Path.Combine(customDirectory, "pre-release", "instance.json");
        Directory.CreateDirectory(Path.GetDirectoryName(customFile)!);
        await File.WriteAllTextAsync(
            customFile,
            "preview metadata",
            TestContext.Current.CancellationToken);
        await _config.SetInstanceDirectoryAsync(customDirectory);

        try
        {
            var changed = await _settings.SetInstanceDirectoryAsync(
                string.Empty,
                TestContext.Current.CancellationToken);

            Assert.True(changed);
            Assert.True(string.IsNullOrWhiteSpace(_config.Configuration.InstanceDirectory));
            Assert.Equal(
                "preview metadata",
                await File.ReadAllTextAsync(
                    Path.Combine(
                        _settings.DefaultInstanceDirectory,
                        "pre-release",
                        "instance.json"),
                    TestContext.Current.CancellationToken));
            Assert.False(Directory.Exists(customDirectory));
        }
        finally
        {
            if (Directory.Exists(customDirectory))
                Directory.Delete(customDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SetInstanceDirectoryAsync_DoesNotChangeTheConfiguredRootWhenCanceled()
    {
        var targetDirectory = Path.Combine(
            Path.GetTempPath(),
            $"HyPrismInstances_{Guid.NewGuid():N}");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _settings.SetInstanceDirectoryAsync(targetDirectory, cancellation.Token));

        Assert.True(string.IsNullOrWhiteSpace(_config.Configuration.InstanceDirectory));
        Assert.False(Directory.Exists(targetDirectory));
    }

    [Fact]
    public async Task GetLauncherStorageUsageAsync_GroupsFilesByPurposeWithoutDoubleCountingInstances()
    {
        var baseline = await _settings.GetLauncherStorageUsageAsync(TestContext.Current.CancellationToken);
        await WriteSizedFileAsync(Path.Combine(_directory, "Preferences", "custom.bin"), 11);
        await WriteSizedFileAsync(Path.Combine(_directory, "Cache", "Images", "News", "cover.bin"), 13);
        await WriteSizedFileAsync(Path.Combine(_directory, "Cache", "News", "Article-test.json"), 19);
        await WriteSizedFileAsync(Path.Combine(_settings.DefaultInstanceDirectory, "release", "Mods", "mod.jar"), 17);
        await WriteSizedFileAsync(Path.Combine(_settings.DefaultInstanceDirectory, "release", "client.pak"), 31);
        await WriteSizedFileAsync(Path.Combine(_directory, "Cache", "archive.zip"), 7);
        await WriteSizedFileAsync(Path.Combine(_directory, "Logs", "latest.log"), 23);
        await WriteSizedFileAsync(Path.Combine(_directory, "Profiles", "avatar.skin"), 29);

        var usage = await _settings.GetLauncherStorageUsageAsync(TestContext.Current.CancellationToken);

        Assert.Equal(baseline.InstanceBytes + 31, usage.InstanceBytes);
        Assert.Equal(baseline.ImageBytes + 13, usage.ImageBytes);
        Assert.Equal(baseline.ModBytes + 17, usage.ModBytes);
        Assert.Equal(baseline.NewsBytes + 19, usage.NewsBytes);
        Assert.Equal(baseline.LogBytes + 23, usage.LogBytes);
        Assert.Equal(baseline.OtherBytes + 47, usage.OtherBytes);
        Assert.Equal(baseline.TotalBytes + 150, usage.TotalBytes);
    }

    private static async Task WriteSizedFileAsync(string path, int size)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(
            path,
            new byte[size],
            TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }
}
