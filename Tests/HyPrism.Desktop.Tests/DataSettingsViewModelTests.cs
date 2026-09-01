// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HyPrism.Core.Game.Instances;
using HyPrism.Core.Game.Launch;
using HyPrism.Core.Models;
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

    [Fact]
    public void StorageDonutParticlePhases_AreDistributedAcrossTheAnimationCycle()
    {
        var phases = Enumerable.Range(0, 14)
            .Select(index => StorageDonutChart.GetParticlePhase(
                StorageDonutIconKind.Instances,
                index))
            .ToArray();

        Assert.True(phases.Min() < 0.15);
        Assert.True(phases.Max() > 0.85);
        Assert.Equal(phases.Length, phases.Distinct().Count());
    }

    [AvaloniaFact]
    public async Task StorageDonutParticles_UseRoundedIconsAndAnimate()
    {
        var chart = new StorageDonutChart
        {
            Width = 300,
            Height = 300,
            TrackBrush = Brushes.Black,
            HoleBrush = Brushes.Black,
            Items =
            [
                new("Instances", 40, "40 MB", "40%", Brushes.Blue, StorageDonutIconKind.Instances),
                new("Images", 20, "20 MB", "20%", Brushes.Teal, StorageDonutIconKind.Images),
                new("Mods", 15, "15 MB", "15%", Brushes.Purple, StorageDonutIconKind.Mods),
                new("News", 10, "10 MB", "10%", Brushes.Orange, StorageDonutIconKind.News),
                new("Logs", 8, "8 MB", "8%", Brushes.Crimson, StorageDonutIconKind.Logs),
                new("Other", 7, "7 MB", "7%", Brushes.DarkSlateGray, StorageDonutIconKind.Other)
            ]
        };
        var window = new Window
        {
            Width = 320,
            Height = 320,
            Content = chart
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(6, chart.LoadedParticleIconCount);
        Assert.True(chart.IsParticleAnimationRunning);
        await Task.Delay(120, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        using var firstFrame = window.CaptureRenderedFrame();
        var sceneBuildCount = chart.StaticSceneBuildCount;
        Assert.Equal(20, chart.CachedParticleCount);
        await Task.Delay(360, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        using var secondFrame = window.CaptureRenderedFrame();

        Assert.NotNull(firstFrame);
        Assert.NotNull(secondFrame);
        using var firstBytes = new MemoryStream();
        using var secondBytes = new MemoryStream();
        firstFrame.Save(firstBytes, PngBitmapEncoderOptions.Default);
        secondFrame.Save(secondBytes, PngBitmapEncoderOptions.Default);
        Assert.False(firstBytes.ToArray().SequenceEqual(secondBytes.ToArray()));
        Assert.Equal(sceneBuildCount, chart.StaticSceneBuildCount);

        chart.IsAnimationEnabled = false;
        Assert.False(chart.IsParticleAnimationRunning);
        chart.IsAnimationEnabled = true;
        Assert.True(chart.IsParticleAnimationRunning);

        window.Close();
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

        Assert.Equal("Edit", viewModel.ChangeInstanceFolderLabel);
        Assert.Equal("Reset", viewModel.ResetInstanceFolderLabel);
        Assert.True(viewModel.IsDefaultInstanceFolder);
        Assert.False(viewModel.CanResetInstanceFolder);

        await viewModel.BrowseInstanceFolderCommand.ExecuteAsync(null);

        Assert.Equal(selectedDirectory, viewModel.InstanceFolder);
        Assert.False(viewModel.IsChangingInstanceFolder);
        Assert.False(viewModel.IsDefaultInstanceFolder);
        Assert.True(viewModel.CanResetInstanceFolder);
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
        var instances = new Mock<IInstanceRepository>();
        var cachedInstances = new List<InstanceInfo>
        {
            new InstanceInfo { Id = "one" },
            new InstanceInfo { Id = "two" },
            new InstanceInfo { Id = "three" }
        };
        instances.Setup(service => service.GetCachedInstances()).Returns(cachedInstances);
        using var viewModel = new SettingsViewModel(
            CreateSettingsStore(
                () => "/home/user/Games/HyPrism",
                "/home/user/.local/share/HyPrism/Instances",
                "/home/user/.local/share/HyPrism").Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"),
            gameProcess: gameProcess.Object,
            instanceRepository: instances.Object);
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
        var resetButton = Assert.IsType<Button>(view.FindControl<Button>("ResetInstanceFolderButton"));
        var instancePathSurface = Assert.IsType<Border>(
            view.FindControl<Border>("InstanceFolderPathSurface"));
        var launcherPathSurface = Assert.IsType<Border>(
            view.FindControl<Border>("LauncherDataPathSurface"));
        Assert.True(instanceCard.IsEffectivelyVisible);
        Assert.True(launcherCard.IsEffectivelyVisible);
        Assert.True(launcherFilesCard.IsEffectivelyVisible);
        Assert.True(storageLegendCard.IsEffectivelyVisible);
        Assert.True(storageDonut.IsEffectivelyVisible);
        Assert.Equal(6, storageDonut.Items.Count);
        Assert.Equal("Instances", storageDonut.Items[0].Label);
        Assert.Equal("3", storageDonut.Items[0].Count);
        Assert.Equal(StorageDonutIconKind.Instances, storageDonut.Items[0].IconKind);
        Assert.Equal("News", storageDonut.Items[3].Label);
        Assert.Equal("151 MB", viewModel.TotalStorageUsage);
        Assert.Contains("Google Sans", storageDonut.LabelFontFamily.ToString());
        Assert.True(warning.IsEffectivelyVisible);
        Assert.False(selectButton.IsEnabled);
        Assert.False(resetButton.IsEnabled);
        Assert.True(resetButton.IsEffectivelyVisible);
        Assert.Equal(VerticalAlignment.Center, selectButton.VerticalContentAlignment);
        var selectLabel = Assert.IsType<TextBlock>(selectButton.Content);
        Assert.Equal("Edit", selectLabel.Text);
        Assert.Equal(VerticalAlignment.Center, selectLabel.VerticalAlignment);
        Assert.Equal(default, instancePathSurface.BorderThickness);
        Assert.Equal(default, launcherPathSurface.BorderThickness);
        Assert.Same(storageDonut.TrackBrush, instancePathSurface.Background);
        Assert.Same(storageDonut.TrackBrush, launcherPathSurface.Background);
        Assert.InRange(
            Math.Abs(storageLegendCard.Bounds.Width - launcherFilesCard.Bounds.Width),
            0,
            1);
        var legendItems = storageLegendCard.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("storageLegendItem"))
            .ToArray();
        var legendGrid = Assert.Single(
            storageLegendCard.GetVisualDescendants().OfType<UniformGrid>());
        Assert.Equal(6, legendItems.Length);
        Assert.All(legendItems, item => Assert.True(item.Bounds.Width > 250));
        Assert.True(storageLegendCard.ClipToBounds);
        Assert.Equal(new CornerRadius(14), storageLegendCard.CornerRadius);
        Assert.Equal(2, legendGrid.ColumnSpacing);
        Assert.Equal(2, legendGrid.RowSpacing);
        Assert.All(legendItems, item =>
        {
            Assert.Equal(default, item.Margin);
            Assert.Equal(64, item.MinHeight);
        });
        cachedInstances.Add(new InstanceInfo { Id = "four" });
        instances.Raise(service => service.InstancesChanged += null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("4", viewModel.StorageUsageItems[0].Count);
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
        Assert.True(resetButton.IsEnabled);

        var renderPath = Environment.GetEnvironmentVariable("HYPRISM_DATA_SETTINGS_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(renderPath))
            window.CaptureRenderedFrame()!.Save(renderPath, PngBitmapEncoderOptions.Default);

        window.Width = 760;
        Dispatcher.UIThread.RunJobs();
        var dataCategoryButton = view.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.DataContext is SettingCategoryViewModel { Id: "data" });
        dataCategoryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(420, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        Assert.True(storageLegendCard.IsEffectivelyVisible);
        Assert.InRange(
            Math.Abs(storageLegendCard.Bounds.Width - launcherFilesCard.Bounds.Width),
            0,
            1);
        Assert.All(legendItems, item => Assert.True(item.Bounds.Width > 250));

        var compactRenderPath = Environment.GetEnvironmentVariable(
            "HYPRISM_DATA_SETTINGS_COMPACT_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(compactRenderPath))
            window.CaptureRenderedFrame()!.Save(compactRenderPath, PngBitmapEncoderOptions.Default);

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
