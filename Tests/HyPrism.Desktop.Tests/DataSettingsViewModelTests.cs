// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HyPrism.Core.Game.Launch;
using HyPrism.Desktop.Controls;
using HyPrism.Desktop.Features.Settings;
using HyPrism.Desktop.Localization;
using HyPrism.Desktop.Platform;
using Moq;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class DataSettingsViewModelTests
{
    [Fact]
    public void StorageDonutLabelFont_GrowsWithTheSegmentShare()
    {
        var tiny = StorageDonutChart.GetPreferredLabelFontSize(0.026);
        var medium = StorageDonutChart.GetPreferredLabelFontSize(0.12);
        var large = StorageDonutChart.GetPreferredLabelFontSize(0.48);

        Assert.True(tiny < medium);
        Assert.True(medium < large);
    }

    [AvaloniaFact]
    public async Task BrowseInstanceFolder_MovesDataAndUpdatesTheDisplayedPath()
    {
        var configuredDirectory = "C:\\HyPrism\\Instances";
        var selectedDirectory = "D:\\Games\\HyPrism";
        var settings = CreateSettingsStore(
            () => configuredDirectory,
            "C:\\HyPrism\\Instances",
            "C:\\HyPrism");
        settings
            .Setup(service => service.SetInstanceDirectoryAsync(selectedDirectory))
            .Callback(() => configuredDirectory = selectedDirectory)
            .ReturnsAsync(true);
        var picker = new Mock<IFilePicker>();
        picker
            .Setup(service => service.BrowseFolderAsync(configuredDirectory))
            .ReturnsAsync(selectedDirectory);
        using var viewModel = new SettingsViewModel(
            settings.Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"),
            picker.Object);

        await viewModel.BrowseInstanceFolderCommand.ExecuteAsync(null);

        Assert.Equal(selectedDirectory, viewModel.InstanceFolder);
        Assert.False(viewModel.IsChangingInstanceFolder);
        settings.Verify(
            service => service.SetInstanceDirectoryAsync(selectedDirectory),
            Times.Once);
    }

    [AvaloniaFact]
    public async Task DataFolderActions_OpenBothEffectiveLocations()
    {
        const string instanceDirectory = "/home/user/Games/HyPrism";
        const string launcherDirectory = "/home/user/.local/share/HyPrism";
        var launcher = new Mock<IExternalUriLauncher>();
        launcher
            .Setup(service => service.LaunchDirectoryAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        using var viewModel = new SettingsViewModel(
            CreateSettingsStore(
                () => instanceDirectory,
                "/home/user/.local/share/HyPrism/Instances",
                launcherDirectory).Object,
            launcher.Object,
            new StringLocalizer("en-US"));

        await viewModel.OpenInstanceFolderCommand.ExecuteAsync(null);
        await viewModel.OpenLauncherDataFolderCommand.ExecuteAsync(null);

        launcher.Verify(
            service => service.LaunchDirectoryAsync(instanceDirectory, It.IsAny<CancellationToken>()),
            Times.Once);
        launcher.Verify(
            service => service.LaunchDirectoryAsync(launcherDirectory, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [AvaloniaFact]
    public async Task DataPage_UsesStorageChartAndLocksChangesWhileGameIsRunning()
    {
        var running = true;
        var gameProcess = new Mock<IGameProcessTracker>();
        gameProcess.Setup(service => service.IsGameRunning()).Returns(() => running);
        using var viewModel = new SettingsViewModel(
            CreateSettingsStore(
                () => "/home/user/Games/HyPrism",
                "/home/user/.local/share/HyPrism/Instances",
                "/home/user/.local/share/HyPrism").Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"),
            gameProcess: gameProcess.Object);
        var storageLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.StorageUsageItems))
                storageLoaded.TrySetResult();
        };
        viewModel.SelectCategoryCommand.Execute(
            viewModel.Categories.Single(category => category.Id == "data"));
        await storageLoaded.Task.WaitAsync(TestContext.Current.CancellationToken);
        var view = new SettingsView
        {
            Width = 1180,
            Height = 760,
            DataContext = viewModel
        };
        var window = new Window
        {
            Width = 1180,
            Height = 760,
            Content = view
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var instanceCard = Assert.IsType<Border>(view.FindControl<Border>("InstanceFolderCard"));
        var launcherCard = Assert.IsType<Border>(view.FindControl<Border>("LauncherDataCard"));
        var launcherFilesCard = Assert.IsType<Border>(view.FindControl<Border>("LauncherFilesCard"));
        var storageLegendCard = Assert.IsType<Border>(view.FindControl<Border>("StorageLegendCard"));
        var storageDonut = Assert.IsType<StorageDonutChart>(view.FindControl<StorageDonutChart>("StorageDonut"));
        var warning = Assert.IsType<Border>(view.FindControl<Border>("DataGameRunningWarning"));
        var selectButton = Assert.IsType<Button>(view.FindControl<Button>("SelectInstanceFolderButton"));
        Assert.True(instanceCard.IsEffectivelyVisible);
        Assert.True(launcherCard.IsEffectivelyVisible);
        Assert.True(launcherFilesCard.IsEffectivelyVisible);
        Assert.True(storageLegendCard.IsEffectivelyVisible);
        Assert.True(storageDonut.IsEffectivelyVisible);
        Assert.Equal(6, storageDonut.Items.Count);
        Assert.Equal("News", storageDonut.Items[3].Label);
        Assert.Equal("151 MB", viewModel.TotalStorageUsage);
        Assert.True(warning.IsEffectivelyVisible);
        Assert.False(selectButton.IsEnabled);
        Assert.Equal(
            "/home/user/Games/HyPrism",
            view.FindControl<TextBlock>("InstanceFolderPath")?.Text);
        Assert.Equal(
            "/home/user/.local/share/HyPrism",
            view.FindControl<TextBlock>("LauncherDataPath")?.Text);

        running = false;
        gameProcess.Raise(
            service => service.GameProcessExited += null,
            new GameProcessExitedEventArgs(
                new GameProcessInfo(1, DateTime.UtcNow, "instance", "profile", null, DateTime.UtcNow),
                0));
        Dispatcher.UIThread.RunJobs();

        Assert.False(warning.IsEffectivelyVisible);
        Assert.True(selectButton.IsEnabled);

        var renderPath = Environment.GetEnvironmentVariable("HYPRISM_DATA_SETTINGS_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(renderPath))
            window.CaptureRenderedFrame()!.Save(renderPath, PngBitmapEncoderOptions.Default);

        window.Close();
    }

    private static Mock<IDesktopSettingsStore> CreateSettingsStore(
        Func<string> instanceDirectory,
        string defaultInstanceDirectory,
        string launcherDataDirectory)
    {
        var settings = new Mock<IDesktopSettingsStore>();
        settings.SetupGet(service => service.Language).Returns("en-US");
        settings.SetupGet(service => service.BackgroundMode).Returns("auto");
        settings.SetupGet(service => service.GpuPreference).Returns("auto");
        settings.SetupGet(service => service.JavaArguments).Returns(string.Empty);
        settings.SetupGet(service => service.AuthDomain).Returns(string.Empty);
        settings.SetupGet(service => service.CustomJavaPath).Returns(string.Empty);
        settings.SetupGet(service => service.GameEnvironmentVariables).Returns(string.Empty);
        settings.SetupGet(service => service.InstanceDirectory).Returns(instanceDirectory);
        settings.SetupGet(service => service.DefaultInstanceDirectory).Returns(defaultInstanceDirectory);
        settings.SetupGet(service => service.LauncherDataDirectory).Returns(launcherDataDirectory);
        settings
            .Setup(service => service.GetLauncherStorageUsageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LauncherStorageUsage(
                72 * 1024 * 1024,
                18 * 1024 * 1024,
                42 * 1024 * 1024,
                12 * 1024 * 1024,
                3 * 1024 * 1024,
                4 * 1024 * 1024));
        settings.SetupGet(service => service.AvailableBackgrounds).Returns(
            ["bg_1.jpg", "bg_2.jpg", "bg_3.jpg"]);
        return settings;
    }
}
