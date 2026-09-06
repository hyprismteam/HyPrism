// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Labs.Lottie;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HyPrism.Core.Game.Sources;
using HyPrism.Core.Game.Versions;
using HyPrism.Core.Infrastructure;
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
    public void CancelAddMirrorKeepsCurrentStepUntilTheVisualTransitionCompletes()
    {
        using var viewModel = new SettingsViewModel(
            CreateSettingsStore().Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"));

        viewModel.ShowAddMirrorCommand.Execute(null);
        viewModel.BeginManualMirrorAdditionCommand.Execute(null);
        viewModel.ManualMirrorJson = "source definition";
        viewModel.CancelAddMirrorCommand.Execute(null);

        Assert.False(viewModel.IsAddingMirror);
        Assert.True(viewModel.IsManualSourceVisible);
        Assert.Equal("source definition", viewModel.ManualMirrorJson);

        viewModel.CompleteMirrorAdditionTransition();

        Assert.False(viewModel.IsAddSourceChoiceVisible);
        Assert.False(viewModel.IsAutomaticSourceVisible);
        Assert.False(viewModel.IsManualSourceVisible);
        Assert.Empty(viewModel.ManualMirrorJson);

        viewModel.ShowAddMirrorCommand.Execute(null);

        Assert.True(viewModel.IsAddingMirror);
        Assert.True(viewModel.IsAddSourceChoiceVisible);
        Assert.False(viewModel.IsAutomaticSourceVisible);
        Assert.False(viewModel.IsManualSourceVisible);
    }

    [Fact]
    public async Task AvailabilityProbeDetectsMirrorWithoutVersionsForCurrentPlatform()
    {
        var appDir = Path.Combine(Path.GetTempPath(), $"hyprism-platform-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDir);

        try
        {
            var source = new Mock<IVersionSource>();
            source.SetupGet(item => item.SourceId).Returns("platform-limited");
            source.SetupGet(item => item.Type).Returns(VersionSourceType.Mirror);
            source.SetupGet(item => item.IsAvailable).Returns(true);
            source.SetupGet(item => item.Priority).Returns(10);
            source.SetupGet(item => item.LayoutInfo).Returns(new VersionSourceLayoutInfo());
            source.Setup(item => item.ProbeAvailabilityAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MirrorSpeedTestResult
                {
                    MirrorId = "platform-limited",
                    IsAvailable = true,
                    PingMs = 24
                });
            source.Setup(item => item.GetVersionsAsync(
                    LauncherUtilities.GetOS(),
                    LauncherUtilities.GetArch(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            using var httpClient = new HttpClient();
            var catalog = new GameVersionCatalog(
                appDir,
                new Mock<IConfigStore>().Object,
                httpClient,
                mirrorSources: [source.Object]);

            var result = await catalog.ProbeSourceAvailabilityAsync(
                "platform-limited",
                TestContext.Current.CancellationToken);

            Assert.True(result.IsAvailable);
            Assert.False(result.HasVersionsForCurrentPlatform);
            source.Verify(item => item.GetVersionsAsync(
                LauncherUtilities.GetOS(),
                LauncherUtilities.GetArch(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task DownloadCategoryReusesProbeResultsAcrossRepeatedSelectionAndReentry()
    {
        var appDir = Path.Combine(Path.GetTempPath(), $"hyprism-download-category-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDir);

        try
        {
            using var httpClient = new HttpClient();
            var catalog = new MirrorCatalog(appDir, httpClient);
            catalog.Save(CreateMirror());
            var versions = new Mock<IGameVersionCatalog>();
            versions
                .Setup(service => service.ProbeSourceAvailabilityAsync(
                    "detected-source",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MirrorSpeedTestResult
                {
                    MirrorId = "detected-source",
                    IsAvailable = true,
                    HasVersionsForCurrentPlatform = false,
                    PingMs = 42
                });
            using var viewModel = new SettingsViewModel(
                CreateSettingsStore().Object,
                new Mock<IExternalUriLauncher>().Object,
                new StringLocalizer("en-US"),
                mirrorCatalog: catalog,
                versionCatalog: versions.Object);
            var source = Assert.Single(viewModel.MirrorSources);
            var downloads = viewModel.Categories.Single(category => category.Id == "downloads");
            var general = viewModel.Categories.Single(category => category.Id == "general");

            viewModel.SelectCategoryCommand.Execute(downloads);
            await WaitUntilAsync(() => source.HasNoCompatibleVersions);

            viewModel.RefreshLocalization();
            viewModel.SelectCategoryCommand.Execute(downloads);
            viewModel.SelectCategoryCommand.Execute(general);
            viewModel.SelectCategoryCommand.Execute(downloads);
            await Task.Delay(80);

            Assert.Same(source, Assert.Single(viewModel.MirrorSources));
            Assert.False(source.IsChecking);
            Assert.True(source.HasNoCompatibleVersions);
            Assert.Equal("Available, but no versions for this system", source.Availability);
            versions.Verify(service => service.ProbeSourceAvailabilityAsync(
                "detected-source",
                It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task OfficialSourceIsProbedOnceWhenAnOfficialAccountExists()
    {
        var versions = new Mock<IGameVersionCatalog>();
        versions.SetupGet(service => service.HasOfficialAccount).Returns(true);
        versions
            .Setup(service => service.ProbeSourceAvailabilityAsync(
                "hytale",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MirrorSpeedTestResult
            {
                MirrorId = "hytale",
                IsAvailable = true,
                PingMs = 24
            });
        using var viewModel = new SettingsViewModel(
            CreateSettingsStore().Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"),
            versionCatalog: versions.Object);
        var downloads = viewModel.Categories.Single(category => category.Id == "downloads");
        var general = viewModel.Categories.Single(category => category.Id == "general");

        viewModel.SelectCategoryCommand.Execute(downloads);
        await WaitUntilAsync(() => viewModel.OfficialSourceIsAvailable);

        viewModel.RefreshLocalization();
        viewModel.SelectCategoryCommand.Execute(downloads);
        viewModel.SelectCategoryCommand.Execute(general);
        viewModel.SelectCategoryCommand.Execute(downloads);
        await Task.Delay(80);

        Assert.False(viewModel.OfficialSourceIsChecking);
        Assert.False(viewModel.OfficialSourceIsUnavailable);
        Assert.Equal("Available", viewModel.OfficialSourceAvailability);
        versions.Verify(service => service.ProbeSourceAvailabilityAsync(
            "hytale",
            It.IsAny<CancellationToken>()), Times.Once);
    }

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
                HasVersionsForCurrentPlatform = false,
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

            var noteCard = Assert.IsType<NoteCard>(view.FindControl<NoteCard>("DownloadsNoteCard"));
            Assert.Contains("note", noteCard.Classes);
            Assert.DoesNotContain("important", noteCard.Classes);
            Assert.Equal("Note", noteCard.Title);
            Assert.Equal(new CornerRadius(14), noteCard.CornerRadius);
            Assert.Equal(
                Color.Parse("#1A79B0F4"),
                Assert.IsAssignableFrom<ISolidColorBrush>(noteCard.Background).Color);
            var noteText = Assert.IsType<TextBlock>(noteCard.Content);
            Assert.Equal(viewModel.DownloadsInfo, noteText.Text);
            Assert.Equal(TextWrapping.Wrap, noteText.TextWrapping);

            probeCompletion.SetResult(probeResult);
            await WaitUntilAsync(() => source.Ping == "42 ms");
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();
            Assert.False(source.IsChecking);
            Assert.False(source.IsAvailable);
            Assert.True(source.HasNoCompatibleVersions);
            Assert.Equal("Available, but no versions for this system", source.Availability);
            Assert.Contains("noVersions", mirrorAvailability.Classes);
            Assert.InRange(checkingState.Opacity, 0, 0.01);
            var noVersionsState = Assert.Single(
                mirrorAvailability.GetVisualDescendants().OfType<Grid>(),
                grid => grid.Classes.Contains("sourceAvailabilityState") &&
                        grid.Classes.Contains("noVersions"));
            Assert.InRange(noVersionsState.Opacity, 0.99, 1);
            var warningIcon = Assert.Single(
                noVersionsState.GetVisualDescendants().OfType<PathIcon>(),
                icon => icon.Classes.Contains("noVersions"));
            Assert.Equal(
                Color.Parse("#FFC54D"),
                Assert.IsAssignableFrom<ISolidColorBrush>(warningIcon.Foreground).Color);

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
            var wizardReveal = Assert.IsType<WizardRevealIcon>(
                view.FindControl<WizardRevealIcon>("DownloadSourceWizardReveal"));
            var wizardAnimation = wizardReveal.Animation;
            Assert.True(wizard.IsEffectivelyVisible);
            Assert.True(choice.IsEffectivelyVisible);
            Assert.Equal("/Assets/Lotties/loader-reveal.json", wizardAnimation.Path);
            Assert.True(wizardAnimation.AutoPlay);
            Assert.Equal(2, wizardAnimation.PlayBackRate);
            Assert.Equal(1, wizardAnimation.RepeatCount);
            Assert.NotNull(wizardAnimation.OpacityMask);
            Assert.Equal(64, wizardAnimation.Width);
            Assert.Equal(64, wizardAnimation.Height);
            var wizardAnimationAnchor = wizardReveal.Anchor;
            var wizardAnimationTranslation = Assert.IsType<TranslateTransform>(
                wizardAnimationAnchor.RenderTransform);
            var manualContent = Assert.IsType<StackPanel>(
                view.FindControl<StackPanel>("ManualSourceAdditionContent"));

            var manualButton = Assert.IsType<Button>(view.FindControl<Button>("BeginManualSourceAdditionButton"));
            var manualStepActivated = WaitForAvaloniaPropertyAsync(
                manualContent,
                InputElement.IsHitTestVisibleProperty,
                () => manualContent.IsHitTestVisible,
                "manual source step to become interactive");
            manualButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await manualStepActivated;
            Dispatcher.UIThread.RunJobs();
            Assert.True(manualContent.IsEffectivelyVisible);
            Assert.InRange(Math.Abs(wizardAnimationTranslation.Y), 0, 0.01);
            Assert.Equal("/Assets/Lotties/loader-reveal.json", wizardAnimation.Path);
            Assert.False(wizardReveal.LastSelectionWasAnimated);

            var wizardRenderPath = Environment.GetEnvironmentVariable("HYPRISM_DOWNLOAD_SOURCE_WIZARD_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(wizardRenderPath))
            {
                await Task.Delay(800);
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame()!.Save(wizardRenderPath, PngBitmapEncoderOptions.Default);
            }

            var wizardClosed = WaitForAvaloniaPropertyAsync(
                wizard,
                Visual.IsVisibleProperty,
                () => !wizard.IsVisible,
                "download source wizard to close");
            viewModel.CancelAddMirrorCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(manualContent.IsEffectivelyVisible);
            await wizardClosed;
            Dispatcher.UIThread.RunJobs();
            Assert.False(wizard.IsEffectivelyVisible);
            Assert.False(viewModel.IsManualSourceVisible);
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

    private static async Task WaitForAvaloniaPropertyAsync(
        AvaloniaObject source,
        AvaloniaProperty property,
        Func<bool> condition,
        string description)
    {
        if (condition())
            return;

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == property && condition())
                completion.TrySetResult(true);
        }

        source.PropertyChanged += OnPropertyChanged;
        try
        {
            if (!condition())
                await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
            Assert.True(condition(), $"Timed out waiting for {description}");
        }
        finally
        {
            source.PropertyChanged -= OnPropertyChanged;
        }
    }

    private static double GetCenterX(Control control, Visual relativeTo)
        => control.TranslatePoint(
               new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
               relativeTo)!.Value.X;
}
