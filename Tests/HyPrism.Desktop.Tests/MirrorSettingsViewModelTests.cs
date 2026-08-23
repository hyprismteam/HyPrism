// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Labs.Lottie;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HyPrism.Core.Game.Sources;
using HyPrism.Core.Game.Versions;
using HyPrism.Core.Models;
using HyPrism.Desktop.Controls;
using HyPrism.Desktop.Features.Settings;
using HyPrism.Desktop.Localization;
using HyPrism.Desktop.Platform;
using Moq;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class MirrorSettingsViewModelTests
{
    [AvaloniaFact]
    public async Task AddToggleAndDeleteMirrorUpdatesPersistedAndRuntimeCatalogs()
    {
        var appDir = Path.Combine(Path.GetTempPath(), $"hyprism-mirror-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDir);

        try
        {
            using var httpClient = new HttpClient();
            var catalog = new MirrorCatalog(appDir, httpClient);
            var discovery = new Mock<IMirrorDiscovery>();
            var versions = new Mock<IGameVersionCatalog>();
            var settings = CreateSettingsStore();
            var uriLauncher = new Mock<IExternalUriLauncher>();
            discovery
                .Setup(service => service.DiscoverMirrorAsync(
                    "https://mirror.example.com/hytale",
                    null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DiscoveryResult
                {
                    Success = true,
                    Mirror = CreateMirror()
                });
            versions
                .Setup(service => service.ProbeSourceAvailabilityAsync(
                    "detected-source",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MirrorSpeedTestResult
                {
                    MirrorId = "detected-source",
                    IsAvailable = true,
                    PingMs = 42
                });

            using var viewModel = new SettingsViewModel(
                settings.Object,
                uriLauncher.Object,
                new StringLocalizer("en-US"),
                mirrorCatalog: catalog,
                mirrorDiscovery: discovery.Object,
                versionCatalog: versions.Object)
            {
                MirrorUrl = "mirror.example.com/hytale"
            };

            await viewModel.AddMirrorCommand.ExecuteAsync(null);

            var source = Assert.Single(viewModel.MirrorSources);
            Assert.Equal("https://mirror.example.com/hytale", source.Endpoint);
            Assert.True(source.IsEnabled);
            Assert.Single(catalog.GetAll());

            viewModel.SelectCategoryCommand.Execute(
                viewModel.Categories.Single(category => category.Id == "downloads"));
            source = Assert.Single(viewModel.MirrorSources);
            await WaitUntilAsync(() => source.Ping == "42 ms");
            Assert.Equal("Available", source.Availability);

            source.IsEnabled = false;
            Assert.False(Assert.Single(catalog.GetAll()).Enabled);

            viewModel.RequestDeleteMirrorCommand.Execute(source);
            viewModel.ConfirmDeleteMirrorCommand.Execute(null);

            Assert.Empty(catalog.GetAll());
            Assert.Empty(viewModel.MirrorSources);
            versions.Verify(service => service.ReloadMirrorSources(), Times.Exactly(3));
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    [AvaloniaFact]
    public void ManualJsonWizardValidatesAndPersistsDownloadSource()
    {
        var appDir = Path.Combine(Path.GetTempPath(), $"hyprism-manual-mirror-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDir);

        try
        {
            using var httpClient = new HttpClient();
            var catalog = new MirrorCatalog(appDir, httpClient);
            var versions = new Mock<IGameVersionCatalog>();
            using var viewModel = new SettingsViewModel(
                CreateSettingsStore().Object,
                new Mock<IExternalUriLauncher>().Object,
                new StringLocalizer("en-US"),
                mirrorCatalog: catalog,
                versionCatalog: versions.Object);

            viewModel.ShowAddMirrorCommand.Execute(null);
            Assert.True(viewModel.IsAddingMirror);
            Assert.True(viewModel.IsAddSourceChoiceVisible);

            viewModel.BeginManualMirrorAdditionCommand.Execute(null);
            Assert.True(viewModel.IsManualSourceVisible);

            viewModel.ManualMirrorJson = "{";
            viewModel.AddManualMirrorCommand.Execute(null);
            Assert.True(viewModel.HasMirrorOperationError);
            Assert.Empty(catalog.GetAll());

            viewModel.ManualMirrorJson = """
                {
                  "schemaVersion": 1,
                  "id": "manual-source",
                  "name": "Manual source",
                  "sourceType": "pattern",
                  "pattern": {
                    "baseUrl": "https://manual.example.com/hytale"
                  }
                }
                """;
            viewModel.AddManualMirrorCommand.Execute(null);

            var source = Assert.Single(viewModel.MirrorSources);
            Assert.Equal("manual-source", source.Id);
            Assert.Equal("https://manual.example.com/hytale", source.Endpoint);
            Assert.False(viewModel.IsAddingMirror);
            Assert.False(viewModel.HasMirrorOperationError);
            Assert.Single(catalog.GetAll());
            versions.Verify(service => service.ReloadMirrorSources(), Times.Once);
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task DownloadSourcesTableAlignsEnabledAndActionsAndOpensWizard()
    {
        var appDir = Path.Combine(Path.GetTempPath(), $"hyprism-mirror-table-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDir);

        try
        {
            using var httpClient = new HttpClient();
            var catalog = new MirrorCatalog(appDir, httpClient);
            catalog.Save(CreateMirror());
            var versions = new Mock<IGameVersionCatalog>();
            var probeCompletion = new TaskCompletionSource<MirrorSpeedTestResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            versions
                .Setup(service => service.ProbeSourceAvailabilityAsync(
                    "detected-source",
                    It.IsAny<CancellationToken>()))
                .Returns(probeCompletion.Task);
            var probeResult = new MirrorSpeedTestResult
            {
                MirrorId = "detected-source",
                IsAvailable = true,
                PingMs = 42
            };
            using var viewModel = new SettingsViewModel(
                CreateSettingsStore().Object,
                new Mock<IExternalUriLauncher>().Object,
                new StringLocalizer("en-US"),
                mirrorCatalog: catalog,
                versionCatalog: versions.Object);
            viewModel.SelectCategoryCommand.Execute(
                viewModel.Categories.Single(category => category.Id == "downloads"));
            var source = Assert.Single(viewModel.MirrorSources);
            Assert.True(source.IsChecking);

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
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();

            var categoryScroll = Assert.IsType<ScrollViewer>(
                view.FindControl<ScrollViewer>("SettingsCategoryScroll"));
            var fixedRailContent = Assert.IsType<Grid>(categoryScroll.Parent);
            Assert.Equal(276, fixedRailContent.MinWidth);
            var categoryDescription = Assert.Single(
                categoryScroll.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible &&
                        text.Classes.Contains("settingsCategoryDescription") &&
                        text.Text == viewModel.Categories.Single(category => category.Id == "downloads").Description);
            var categoryDescriptionSize = categoryDescription.Bounds.Size;

            var table = Assert.IsType<Border>(view.FindControl<Border>("DownloadSourcesTable"));
            var visibleText = table.GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible)
                .ToArray();
            Assert.Contains(visibleText, text => text.Text == viewModel.SourceEnabledColumn);
            Assert.DoesNotContain(visibleText, text => text.Text == viewModel.SourcePingColumn);

            var enabledHeader = Assert.Single(
                visibleText,
                text => text.Text == viewModel.SourceEnabledColumn);
            var mirrorToggle = Assert.Single(
                table.GetVisualDescendants().OfType<ToggleSwitch>(),
                toggle => toggle.IsEffectivelyVisible && toggle.DataContext is MirrorSourceViewModel);
            var availabilityHeader = Assert.Single(
                visibleText,
                text => text.Text == viewModel.SourceAvailabilityColumn);
            var mirrorAvailability = Assert.Single(
                table.GetVisualDescendants().OfType<Grid>(),
                grid => grid.IsEffectivelyVisible &&
                        grid.Classes.Contains("sourceAvailability") &&
                        grid.DataContext is MirrorSourceViewModel);
            Assert.Contains("checking", mirrorAvailability.Classes);
            var checkingState = Assert.Single(
                mirrorAvailability.GetVisualDescendants().OfType<Grid>(),
                grid => grid.Classes.Contains("sourceAvailabilityState") &&
                        grid.Classes.Contains("checking"));
            var stateTransitions = checkingState.Transitions;
            Assert.NotNull(stateTransitions);
            var opacityTransition = Assert.IsType<Avalonia.Animation.DoubleTransition>(
                Assert.Single(
                    stateTransitions!,
                    transition => transition is Avalonia.Animation.DoubleTransition));
            var movementTransition = Assert.IsType<Avalonia.Animation.TransformOperationsTransition>(
                Assert.Single(
                    stateTransitions!,
                    transition => transition is Avalonia.Animation.TransformOperationsTransition));
            Assert.Equal(TimeSpan.FromMilliseconds(220), opacityTransition.Duration);
            Assert.Equal(TimeSpan.FromMilliseconds(260), movementTransition.Duration);
            Assert.Single(
                checkingState.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>(),
                path => path.Classes.Contains("sourceAvailabilitySpinner"));
            Assert.InRange(Math.Abs(GetCenterX(enabledHeader, table) - GetCenterX(mirrorToggle, table)), 0, 1);
            Assert.InRange(Math.Abs(GetCenterX(availabilityHeader, table) - GetCenterX(mirrorAvailability, table)), 0, 1);
            Assert.Single(
                table.GetVisualDescendants().OfType<Border>(),
                border => border.IsEffectivelyVisible && border.Classes.Contains("sourceMoreTarget"));

            var checkingRenderPath = Environment.GetEnvironmentVariable(
                "HYPRISM_DOWNLOAD_SOURCES_CHECKING_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(checkingRenderPath))
                window.CaptureRenderedFrame()!.Save(checkingRenderPath, PngBitmapEncoderOptions.Default);

            probeCompletion.SetResult(probeResult);
            await WaitUntilAsync(() => source.Ping == "42 ms");
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();
            Assert.False(source.IsChecking);
            Assert.True(source.IsAvailable);
            Assert.Contains("available", mirrorAvailability.Classes);
            Assert.InRange(checkingState.Opacity, 0, 0.01);
            var availableState = Assert.Single(
                mirrorAvailability.GetVisualDescendants().OfType<Grid>(),
                grid => grid.Classes.Contains("sourceAvailabilityState") &&
                        grid.Classes.Contains("available"));
            Assert.InRange(availableState.Opacity, 0.99, 1);

            var tableRenderPath = Environment.GetEnvironmentVariable("HYPRISM_DOWNLOAD_SOURCES_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(tableRenderPath))
                window.CaptureRenderedFrame()!.Save(tableRenderPath, PngBitmapEncoderOptions.Default);

            var actionPopup = Assert.Single(
                table.GetVisualDescendants().OfType<FadingPopup>());
            source.IsMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(actionPopup.IsRequestedOpen);
            Assert.True(actionPopup.IsOpen);
            var removeAction = Assert.IsType<Button>(
                actionPopup.Child!.GetVisualDescendants()
                    .OfType<Button>()
                    .Single(button => button.Classes.Contains("sourceMenuAction")));
            Assert.Same(viewModel.RequestDeleteMirrorCommand, removeAction.Command);
            Assert.Same(source, removeAction.CommandParameter);
            source.IsMenuOpen = false;

            var addButton = Assert.IsType<Button>(view.FindControl<Button>("AddDownloadSourceButton"));
            Assert.Same(viewModel.ShowAddMirrorCommand, addButton.Command);
            viewModel.ShowAddMirrorCommand.Execute(null);
            await Task.Delay(420);
            Dispatcher.UIThread.RunJobs();
            var wizard = Assert.IsType<Border>(view.FindControl<Border>("DownloadSourceWizardScreen"));
            var choice = Assert.IsType<StackPanel>(view.FindControl<StackPanel>("SourceAdditionChoiceContent"));
            var wizardAnimation = Assert.IsType<Lottie>(
                view.FindControl<Lottie>("DownloadSourceWizardAnimation"));
            Assert.True(wizard.IsEffectivelyVisible);
            Assert.True(choice.IsEffectivelyVisible);
            Assert.Equal("/Assets/Lotties/loader-reveal.json", wizardAnimation.Path);
            Assert.True(wizardAnimation.AutoPlay);
            Assert.Equal(1, wizardAnimation.RepeatCount);
            Assert.NotNull(wizardAnimation.OpacityMask);
            Assert.Equal(64, wizardAnimation.Width);
            Assert.Equal(64, wizardAnimation.Height);
            var wizardAnimationAnchor = Assert.IsType<Border>(
                view.FindControl<Border>("DownloadSourceWizardAnimationAnchor"));
            var wizardAnimationTranslation = Assert.IsType<TranslateTransform>(
                wizardAnimationAnchor.RenderTransform);
            var wizardAnimationMotion = Assert.IsType<Border>(
                view.FindControl<Border>("DownloadSourceWizardAnimationMotion"));
            var wizardAnimationMotionTranslation = Assert.IsType<TranslateTransform>(
                wizardAnimationMotion.RenderTransform);

            var manualButton = Assert.IsType<Button>(view.FindControl<Button>("BeginManualSourceAdditionButton"));
            manualButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Math.Abs(wizardAnimationMotionTranslation.Y) > 0.5);
            await Task.Delay(140);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Math.Abs(wizardAnimationTranslation.Y) > 0.5);
            await Task.Delay(230);
            Dispatcher.UIThread.RunJobs();
            var manualContent = Assert.IsType<StackPanel>(
                view.FindControl<StackPanel>("ManualSourceAdditionContent"));
            Assert.True(manualContent.IsEffectivelyVisible);
            Assert.InRange(Math.Abs(wizardAnimationTranslation.Y), 0, 0.01);

            var wizardRenderPath = Environment.GetEnvironmentVariable("HYPRISM_DOWNLOAD_SOURCE_WIZARD_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(wizardRenderPath))
                window.CaptureRenderedFrame()!.Save(wizardRenderPath, PngBitmapEncoderOptions.Default);

            viewModel.CancelAddMirrorCommand.Execute(null);
            await Task.Delay(260);
            Dispatcher.UIThread.RunJobs();
            Assert.InRange(Math.Abs(categoryDescription.Bounds.Width - categoryDescriptionSize.Width), 0, 0.01);
            Assert.InRange(Math.Abs(categoryDescription.Bounds.Height - categoryDescriptionSize.Height), 0, 0.01);

            var railTransitionRenderPath = Environment.GetEnvironmentVariable(
                "HYPRISM_SETTINGS_RAIL_TRANSITION_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(railTransitionRenderPath))
                window.CaptureRenderedFrame()!.Save(railTransitionRenderPath, PngBitmapEncoderOptions.Default);

            window.Close();
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    private static Mock<IDesktopSettingsStore> CreateSettingsStore()
    {
        var settings = new Mock<IDesktopSettingsStore>();
        settings.SetupGet(service => service.Language).Returns("en-US");
        settings.SetupGet(service => service.BackgroundMode).Returns("auto");
        settings.SetupGet(service => service.GpuPreference).Returns("auto");
        settings.SetupGet(service => service.JavaArguments).Returns(string.Empty);
        settings.SetupGet(service => service.AuthDomain).Returns(string.Empty);
        settings.SetupGet(service => service.CustomJavaPath).Returns(string.Empty);
        settings.SetupGet(service => service.GameEnvironmentVariables).Returns(string.Empty);
        settings.SetupGet(service => service.AvailableBackgrounds).Returns(
            ["bg_1.jpg", "bg_2.jpg", "bg_3.jpg"]);
        return settings;
    }

    private static MirrorMeta CreateMirror()
        => new()
        {
            Id = "detected-source",
            Name = "Detected source",
            SourceType = "pattern",
            Pattern = new MirrorPatternConfig
            {
                BaseUrl = "https://mirror.example.com/hytale",
                VersionDiscovery = new VersionDiscoveryConfig
                {
                    Method = "static-list",
                    StaticVersions = [1]
                }
            }
        };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 20 && !condition(); attempt++)
            await Task.Delay(10);

        Assert.True(condition());
    }

    private static double GetCenterX(Control control, Visual relativeTo)
        => control.TranslatePoint(
               new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
               relativeTo)!.Value.X;
}
