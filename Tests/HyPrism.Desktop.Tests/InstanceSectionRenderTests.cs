// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net.Http;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HyPrism.Core.Accounts;
using HyPrism.Core.Application.Ports;
using HyPrism.Core.Application.Progress;
using HyPrism.Core.Game;
using HyPrism.Core.Game.Instances;
using HyPrism.Core.Game.Launch;
using HyPrism.Core.Game.Mods;
using HyPrism.Core.Models;
using HyPrism.Desktop.Features.Instances;
using HyPrism.Desktop.Features.News;
using HyPrism.Desktop.Features.Settings;
using HyPrism.Desktop.Localization;
using HyPrism.Desktop.Platform;
using HyPrism.Desktop.Shell;
using Moq;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class InstanceSectionRenderTests
{
    [AvaloniaFact]
    public async Task ModsBrowseAndConsoleSectionsRenderInteractiveRows()
    {
        const string instancePath = "/tmp/hyprism-section-render-test";
        var instance = new InstanceInfo
        {
            Id = "render-instance",
            Name = "Render Instance",
            Branch = "release",
            Version = 20,
            IsInstalled = true
        };
        var instances = new Mock<IInstanceRepository>();
        var profiles = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var launchCoordinator = new Mock<IGameLaunchCoordinator>();
        var installationWorkflow = new Mock<IGameInstallationWorkflow>();
        var gameProcess = new Mock<IGameProcessTracker>();
        var progress = new Mock<IProgressReporter>();
        var settings = new Mock<IDesktopSettingsStore>();
        var news = new Mock<IHytaleNewsClient>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var modManager = new Mock<IModManager>();
        var console = new GameConsoleService();

        instances.Setup(service => service.GetCachedInstances()).Returns([instance]);
        instances.Setup(service => service.GetSelectedInstance()).Returns(instance);
        instances.Setup(service => service.GetInstancePathById(instance.Id)).Returns(instancePath);
        instances.Setup(service => service.IsClientPresent(instancePath)).Returns(true);
        profiles.Setup(service => service.GetNick()).Returns("Render Player");
        settings.SetupGet(service => service.AvailableBackgrounds).Returns([]);
        modManager.Setup(service => service.GetInstanceInstalledMods(instancePath)).Returns(
        [
            new InstalledMod
            {
                Id = "cf-1",
                CurseForgeId = "1",
                Name = "Rendered Mod",
                Version = "1.0",
                Author = "Author",
                Enabled = true
            }
        ]);
        modManager.Setup(service => service.SearchModsAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string[]>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ModSearchResult
            {
                Mods =
                [
                    new ModInfo
                    {
                        Id = "10",
                        Name = "Catalog Mod",
                        Author = "Creator",
                        AuthorUrl = "https://www.curseforge.com/members/creator",
                        AuthorAvatarUrl = "https://media.forgecdn.net/avatars/1/2/avatar.png",
                        Summary = "Summary",
                        LatestFileId = "900"
                    }
                ],
                TotalCount = 1
            });
        modManager.Setup(service => service.GetModFilesAsync("10", 0, 10))
            .ReturnsAsync(new ModFilesResult
            {
                Files =
                [
                    new ModFileInfo
                    {
                        Id = "900",
                        ModId = "10",
                        DisplayName = "Catalog Mod 1.0",
                        FileName = "catalog-mod.jar",
                        ReleaseType = 1,
                        GameVersions = ["release"]
                    },
                    new ModFileInfo
                    {
                        Id = "899",
                        ModId = "10",
                        DisplayName = "Catalog Mod alpha",
                        FileName = "catalog-mod-alpha.jar",
                        ReleaseType = 3,
                        GameVersions = ["release"]
                    },
                    new ModFileInfo
                    {
                        Id = "898",
                        ModId = "10",
                        DisplayName = "Catalog Mod beta",
                        FileName = "catalog-mod-beta.jar",
                        ReleaseType = 2,
                        GameVersions = ["release"]
                    }
                ],
                TotalCount = 3
            });
        console.Append(instance.Id, "ERR", "rendered error line");

        using var viewModel = new MainWindowViewModel(
            instances.Object,
            profiles.Object,
            profileRepository.Object,
            launchCoordinator.Object,
            installationWorkflow.Object,
            gameProcess.Object,
            progress.Object,
            settings.Object,
            news.Object,
            uriLauncher.Object,
            new HttpClient(),
            new StringLocalizer("en-US"),
            modManager: modManager.Object,
            gameConsole: console);

        var view = new InstancesView { DataContext = viewModel };
        var window = new Window
        {
            Width = 1180,
            Height = 760,
            Content = view
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        viewModel.SelectInstanceSectionCommand.Execute("mods");
        await WaitUntilAsync(() => viewModel.InstalledMods.Count == 1);
        await WaitUntilAsync(() => FindRows(view, "instanceModRow").Any(border => border.IsEffectivelyVisible));

        var modRows = FindRows(view, "instanceModRow").Where(border => border.IsEffectivelyVisible).ToList();
        Assert.NotEmpty(modRows);
        Assert.Contains(modRows[0].GetVisualDescendants(), element => element is CheckBox);
        Assert.Contains(modRows[0].GetVisualDescendants(), element => element is ToggleSwitch);
        Assert.Contains(
            modRows[0].GetVisualDescendants(),
            element => element is Button button && button.Classes.Contains("instanceRowIconButton"));

        viewModel.SelectInstanceSectionCommand.Execute("browse");
        await WaitUntilAsync(() => viewModel.ModCatalogItems.Count == 1);
        viewModel.ModCatalogItems.Add(new ModCatalogItemViewModel(
            "11",
            "Incompatible Mod",
            "Other creator",
            "Incompatible summary",
            "901",
            recommendedFileId: "901",
            compatibility: ModCompatibilityStatus.Incompatible,
            compatibilityLabel: "Incompatible"));
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        await WaitUntilAsync(() =>
            FindRows(view, "instanceModRow").Count(border =>
                border.IsEffectivelyVisible &&
                border.Classes.Contains("catalog") &&
                border.GetVisualDescendants().OfType<CheckBox>().Any()) == 2);
        var catalogRows = FindRows(view, "instanceModRow")
            .Where(border => border.IsEffectivelyVisible && border.Classes.Contains("catalog"))
            .ToList();
        Assert.Equal(2, catalogRows.Count);
        Assert.All(catalogRows, row => Assert.Equal(80, row.MinHeight));
        Assert.All(catalogRows, row => Assert.Contains(
            row.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("instanceCatalogAuthorBadge")));
        Assert.All(catalogRows, row =>
        {
            var modIcon = Assert.Single(
                row.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("instanceModIcon"));
            Assert.Equal(56, modIcon.Width);
            var authorAvatar = Assert.Single(
                row.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("instanceCatalogAuthorAvatar"));
            Assert.Equal(22, authorAvatar.Width);
            Assert.Contains(authorAvatar.GetVisualDescendants(), descendant => descendant is Image);
        });
        Assert.DoesNotContain(
            catalogRows.SelectMany(row => row.GetVisualDescendants()).OfType<Avalonia.Controls.Shapes.Path>(),
            path => path.Classes.Contains("instanceCatalogAuthorBrand"));
        Assert.Equal(
            "https://media.forgecdn.net/avatars/1/2/avatar.png",
            viewModel.ModCatalogItems[0].AuthorAvatarUrl);
        Assert.DoesNotContain(
            catalogRows.SelectMany(row => row.GetVisualDescendants()).OfType<Border>(),
            border => border.Classes.Contains("instanceCompatibilityBadge") ||
                      border.Classes.Contains("instanceBadge") &&
                      !border.Classes.Contains("update"));
        var incompatibleRow = Assert.Single(catalogRows, row => row.Classes.Contains("incompatible"));
        Assert.Equal(0.48, incompatibleRow.Opacity);
        var catalogCheck = Assert.Single(
            catalogRows[0].GetVisualDescendants().OfType<CheckBox>());
        Assert.Equal(new Thickness(0), catalogCheck.BorderThickness);
        var checkBackground = Assert.Single(
            catalogCheck.GetVisualDescendants().OfType<Border>(),
            border => border.Name == "SelectionIndicator");
        Assert.Contains(
            Assert.IsAssignableFrom<IEnumerable<ITransition>>(checkBackground.Transitions),
            transition => transition is BrushTransition);
        var checkGlyph = Assert.Single(
            catalogCheck.GetVisualDescendants().OfType<PathIcon>(),
            icon => icon.Name == "CheckGlyph");
        Assert.Contains(
            Assert.IsAssignableFrom<IEnumerable<ITransition>>(checkGlyph.Transitions),
            transition => transition is DoubleTransition);
        Assert.Contains(
            Assert.IsAssignableFrom<IEnumerable<ITransition>>(checkGlyph.Transitions),
            transition => transition is TransformOperationsTransition);
        Assert.DoesNotContain(
            FindRows(view, "instanceModRow")
                .Where(border => border.IsEffectivelyVisible)
                .SelectMany(border => border.GetVisualDescendants())
                .OfType<Button>(),
            button => button.Classes.Contains("instanceInstall"));
        var comboCount = view.GetVisualDescendants()
            .OfType<ComboBox>()
            .Count(combo => combo.IsEffectivelyVisible && combo.Classes.Contains("instanceFilterCombo"));
        Assert.Equal(2, comboCount);
        var filterCombos = view.GetVisualDescendants()
            .OfType<ComboBox>()
            .Where(combo => combo.IsEffectivelyVisible && combo.Classes.Contains("instanceFilterCombo"))
            .ToList();
        var searchBox = view.GetVisualDescendants()
            .OfType<TextBox>()
            .Single(textBox => textBox.IsEffectivelyVisible && textBox.Classes.Contains("instanceSearch"));
        Assert.All(filterCombos, combo => Assert.Same(searchBox.Parent, combo.Parent));

        viewModel.ToggleModCatalogSelectionCommand.Execute(viewModel.ModCatalogItems[0]);
        Assert.True(viewModel.HasSelectedCatalogMods);
        Assert.Contains(view.GetVisualDescendants().OfType<Button>(), button =>
            button.IsEffectivelyVisible &&
            ReferenceEquals(button.Command, viewModel.InstallSelectedCatalogModsCommand));

        var listPreviewPath = Environment.GetEnvironmentVariable("HYPRISM_MOD_CATALOG_LIST_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(listPreviewPath))
        {
            window.CaptureRenderedFrame()!.Save(listPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(listPreviewPath));
        }

        await viewModel.SelectModCatalogPreviewCommand.ExecuteAsync(viewModel.ModCatalogItems[0]);
        await WaitUntilAsync(() => viewModel.ModCatalogPreviewFiles.Count == 3);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        await WaitUntilAsync(() => view.GetVisualDescendants()
            .OfType<ItemsControl>()
            .Any(items => items.IsEffectivelyVisible && items.Classes.Contains("instancePreviewFiles")));
        var preview = view.FindControl<Grid>("ModCatalogModalSheet");
        Assert.NotNull(preview);
        Assert.True(preview.IsEffectivelyVisible);
        var modal = view.FindControl<Grid>("ModCatalogModal");
        var instancesLayout = view.FindControl<Grid>("InstancesLayout");
        Assert.NotNull(modal);
        Assert.True(modal.IsVisible);
        var blurEffect = Assert.IsType<BlurEffect>(instancesLayout?.Effect);
        Assert.NotEmpty(Assert.IsAssignableFrom<IEnumerable<ITransition>>(blurEffect.Transitions));
        Assert.True(view.FindControl<Grid>("ModCatalogSection")?.IsVisible);
        Assert.Contains(
            preview.GetVisualDescendants(),
            element => element is ItemsControl items && items.Classes.Contains("instancePreviewFiles"));
        Assert.Contains(
            preview.GetVisualDescendants(),
            element => element is Border border && border.Classes.Contains("sourceTableHeader"));
        Assert.DoesNotContain(
            preview.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("instanceCompatibilitySummary"));
        var releaseBadges = preview.GetVisualDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("modPreviewReleaseBadge"))
            .ToList();
        var releaseBadge = Assert.Single(releaseBadges, border => border.Classes.Contains("release"));
        var betaBadge = Assert.Single(releaseBadges, border => border.Classes.Contains("beta"));
        var alphaBadge = Assert.Single(releaseBadges, border => border.Classes.Contains("alpha"));
        Assert.Contains("release", releaseBadge.Classes);
        Assert.Equal(new Thickness(0), releaseBadge.BorderThickness);
        Assert.Equal(new Thickness(0), betaBadge.BorderThickness);
        Assert.Equal(new Thickness(0), alphaBadge.BorderThickness);
        Assert.All(
            preview.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("instancePreviewFile")),
            button => Assert.Null(ToolTip.GetTip(button)));
        var curseForgeAction = Assert.Single(
            preview.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("modPreviewCurseForgeAction"));
        Assert.Same(viewModel.OpenCatalogModPageCommand, curseForgeAction.Command);
        Assert.Contains(
            curseForgeAction.GetVisualDescendants(),
            element => element is Avalonia.Controls.Shapes.Path path &&
                path.Classes.Contains("modPreviewCurseForgeIcon"));
        Assert.Equal(HorizontalAlignment.Center, curseForgeAction.HorizontalContentAlignment);
        Assert.Equal(HorizontalAlignment.Stretch, curseForgeAction.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Stretch, curseForgeAction.VerticalContentAlignment);
        var installAction = Assert.Single(
            preview.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("splitMain"));
        Assert.Equal(HorizontalAlignment.Stretch, installAction.HorizontalAlignment);
        Assert.Equal(HorizontalAlignment.Center, installAction.HorizontalContentAlignment);
        Assert.Equal(VerticalAlignment.Center, installAction.VerticalContentAlignment);
        var splitActionGrid = Assert.IsType<Grid>(curseForgeAction.Parent);
        Assert.True(splitActionGrid.ColumnDefinitions[2].Width.Value >
                    splitActionGrid.ColumnDefinitions[0].Width.Value);
        Assert.Contains(
            Assert.IsAssignableFrom<IEnumerable<ITransition>>(curseForgeAction.Transitions),
            transition => transition is BrushTransition);
        Assert.Contains(
            Assert.IsAssignableFrom<IEnumerable<ITransition>>(installAction.Transitions),
            transition => transition is BrushTransition);
        var curseForgeIcon = Assert.Single(
            curseForgeAction.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>(),
            path => path.Classes.Contains("modPreviewCurseForgeIcon"));
        Assert.Equal(23, curseForgeIcon.Width);
        Assert.Equal(5, Assert.IsType<TranslateTransform>(curseForgeIcon.RenderTransform).Y);
        var shoulderScale = Assert.IsType<ScaleTransform>(
            view.FindControl<Grid>("ModCatalogModalShoulders")?.RenderTransform);
        Assert.NotEmpty(Assert.IsAssignableFrom<IEnumerable<ITransition>>(shoulderScale.Transitions));

        var previewPath = Environment.GetEnvironmentVariable("HYPRISM_MOD_CATALOG_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(previewPath))
        {
            window.CaptureRenderedFrame()!.Save(previewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(previewPath));
        }

        window.Width = 680;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        await WaitUntilAsync(() => view.Classes.Contains("compact"));
        var modalSheet = view.FindControl<Grid>("ModCatalogModalSheet");
        var modalShoulders = view.FindControl<Grid>("ModCatalogModalShoulders");
        Assert.Equal(560, modalSheet?.MaxWidth);
        Assert.Equal(608, modalShoulders?.MaxWidth);
        Assert.Equal(520, modalSheet?.MaxHeight);
        var compactPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_MOD_CATALOG_COMPACT_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(compactPreviewPath))
            window.CaptureRenderedFrame()!.Save(compactPreviewPath, PngBitmapEncoderOptions.Default);
        window.Width = 1180;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        await WaitUntilAsync(() => view.Classes.Contains("wide"));

        Assert.True(view.TryNavigateBack());
        Assert.False(viewModel.HasModCatalogPreview);
        Assert.True(viewModel.IsInstanceBrowseSection);
        await WaitUntilAsync(() => !modal.IsVisible);
        Assert.Equal(0, Assert.IsType<BlurEffect>(instancesLayout?.Effect).Radius);

        Assert.Equal(720, view.FindControl<Grid>("InstalledModsSection")?.MaxWidth);
        Assert.Equal(720, view.FindControl<Grid>("ModCatalogSection")?.MaxWidth);
        Assert.Equal(720, view.FindControl<Grid>("InstanceConsoleSection")?.MaxWidth);

        viewModel.SelectInstanceSectionCommand.Execute("console");
        Assert.Single(viewModel.ConsoleLines);
        await WaitUntilAsync(() => FindConsoleLines(view).Any(text => text.IsEffectivelyVisible));
        var consoleLines = FindConsoleLines(view).Where(text => text.IsEffectivelyVisible).ToList();
        Assert.NotEmpty(consoleLines);
        Assert.Contains("rendered error line", consoleLines[0].Text);
        Assert.Contains("error", consoleLines[0].Classes);
    }

    [AvaloniaFact]
    public async Task ModPreviewModalShowsSkeletonAndPreloadsAllScreenshots()
    {
        const string instancePath = "/tmp/hyprism-preview-skeleton-test";
        var instance = new InstanceInfo
        {
            Id = "preview-instance",
            Name = "Preview Instance",
            Branch = "release",
            Version = 20,
            IsInstalled = true
        };
        var instances = new Mock<IInstanceRepository>();
        var profiles = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var launchCoordinator = new Mock<IGameLaunchCoordinator>();
        var installationWorkflow = new Mock<IGameInstallationWorkflow>();
        var gameProcess = new Mock<IGameProcessTracker>();
        var progress = new Mock<IProgressReporter>();
        var settings = new Mock<IDesktopSettingsStore>();
        var news = new Mock<IHytaleNewsClient>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var modManager = new Mock<IModManager>();
        var console = new GameConsoleService();
        var imageHandler = new CountingImageHandler();

        instances.Setup(service => service.GetCachedInstances()).Returns([instance]);
        instances.Setup(service => service.GetSelectedInstance()).Returns(instance);
        instances.Setup(service => service.GetInstancePathById(instance.Id)).Returns(instancePath);
        instances.Setup(service => service.IsClientPresent(instancePath)).Returns(true);
        profiles.Setup(service => service.GetNick()).Returns("Preview Player");
        settings.SetupGet(service => service.AvailableBackgrounds).Returns([]);
        modManager.Setup(service => service.GetInstanceInstalledMods(instancePath)).Returns([]);
        modManager.Setup(service => service.SearchModsAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string[]>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ModSearchResult
            {
                Mods =
                [
                    new ModInfo
                    {
                        Id = "20",
                        Name = "Preview Mod",
                        Author = "Creator",
                        Summary = "Summary",
                        LatestFileId = "910",
                        Screenshots =
                        [
                            new CurseForgeScreenshot { Url = "https://fake.local/a.png" },
                            new CurseForgeScreenshot { Url = "https://fake.local/b.png" }
                        ]
                    }
                ],
                TotalCount = 1
            });
        var filesGate = new TaskCompletionSource();
        modManager.Setup(service => service.GetModFilesAsync("20", 0, 10))
            .Returns(async () =>
            {
                await filesGate.Task;
                return new ModFilesResult
                {
                    Files =
                    [
                        new ModFileInfo
                        {
                            Id = "910",
                            ModId = "20",
                            DisplayName = "Preview Mod 1.0",
                            FileName = "preview-mod.jar",
                            ReleaseType = 1,
                            GameVersions = ["release"]
                        }
                    ],
                    TotalCount = 1
                };
            });

        using var viewModel = new MainWindowViewModel(
            instances.Object,
            profiles.Object,
            profileRepository.Object,
            launchCoordinator.Object,
            installationWorkflow.Object,
            gameProcess.Object,
            progress.Object,
            settings.Object,
            news.Object,
            uriLauncher.Object,
            new HttpClient(imageHandler),
            new StringLocalizer("en-US"),
            modManager: modManager.Object,
            gameConsole: console);

        var view = new InstancesView { DataContext = viewModel };
        var window = new Window
        {
            Width = 1180,
            Height = 760,
            Content = view
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        viewModel.SelectInstanceSectionCommand.Execute("browse");
        await WaitUntilAsync(() => viewModel.ModCatalogItems.Count == 1);
        imageHandler.Requests = 0;

        var openTask = viewModel.SelectModCatalogPreviewCommand.ExecuteAsync(
            viewModel.ModCatalogItems[0]);
        Assert.True(viewModel.IsModCatalogPreviewFilesSkeletonVisible);
        filesGate.SetResult();
        await openTask;

        Assert.True(viewModel.IsModCatalogPreviewFilesSkeletonVisible);
        Assert.False(viewModel.IsModCatalogPreviewFilesContentVisible);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var skeletonPanel = FindPanels(view).Single(panel =>
            panel.Classes.Contains("modPreviewFilesSkeleton"));
        Assert.True(skeletonPanel.IsVisible);
        Assert.True(skeletonPanel.IsEffectivelyVisible);
        var skeletonPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_MOD_PREVIEW_SKELETON_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(skeletonPreviewPath))
        {
            window.CaptureRenderedFrame()!.Save(skeletonPreviewPath, PngBitmapEncoderOptions.Default);
        }

        await WaitUntilAsync(() => viewModel.IsModCatalogPreviewFilesContentVisible);
        Assert.False(viewModel.IsModCatalogPreviewFilesSkeletonVisible);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var contentPanel = FindPanels(view).Single(panel =>
            panel.Classes.Contains("modPreviewContent"));
        Assert.False(skeletonPanel.IsVisible);
        var items = FindItemsControls(view).Single(items =>
            items.Classes.Contains("instancePreviewFiles"));
        Assert.True(items.IsEffectivelyVisible);
        await WaitUntilAsync(() => contentPanel.Opacity > 0.9);
        var revealedPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_MOD_PREVIEW_REVEALED_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(revealedPreviewPath))
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()!.Save(revealedPreviewPath, PngBitmapEncoderOptions.Default);
        }

        await WaitUntilAsync(() => viewModel.ModCatalogPreviewImage is not null);
        Assert.Equal(2, imageHandler.Requests);
        Assert.False(viewModel.CanShowPreviousModCatalogScreenshot);
        Assert.True(viewModel.CanShowNextModCatalogScreenshot);

        viewModel.ShowNextModCatalogScreenshotCommand.Execute(null);
        Assert.True(viewModel.ShowNextModCatalogScreenshotCommand.CanExecute(null));
        Assert.False(viewModel.CanShowPreviousModCatalogScreenshot);
        Assert.True(viewModel.CanShowNextModCatalogScreenshot);
        await WaitUntilAsync(() => viewModel.ModCatalogPreviewScreenshotIndex == 1);
        await WaitUntilAsync(() => !viewModel.IsModCatalogPreviewImageTransitioning);
        Assert.False(viewModel.IsModCatalogPreviewImageLoading);
        Assert.NotNull(viewModel.ModCatalogPreviewImage);
        Assert.True(viewModel.CanShowPreviousModCatalogScreenshot);
        Assert.False(viewModel.CanShowNextModCatalogScreenshot);
        viewModel.ShowNextModCatalogScreenshotCommand.Execute(null);
        Assert.Equal(1, viewModel.ModCatalogPreviewScreenshotIndex);
        await Task.Delay(150);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, imageHandler.Requests);

        var closingImage = viewModel.ModCatalogPreviewImage;
        viewModel.CloseModCatalogPreviewCommand.Execute(null);
        Assert.False(viewModel.HasModCatalogPreview);
        Assert.True(viewModel.IsModCatalogPreviewMounted);
        Assert.Equal(1d, view.FindControl<Grid>("ModCatalogModalSheet")!.Opacity);
        Assert.Same(closingImage, viewModel.ModCatalogPreviewImage);
        await Task.Delay(100);
        Assert.Same(closingImage, viewModel.ModCatalogPreviewImage);
        var closingPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_MOD_PREVIEW_CLOSING_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(closingPreviewPath))
            window.CaptureRenderedFrame()!.Save(closingPreviewPath, PngBitmapEncoderOptions.Default);
        await WaitUntilAsync(() => viewModel.ModCatalogPreviewImage is null);
        Assert.False(viewModel.IsModCatalogPreviewMounted);
        Assert.False(viewModel.IsModCatalogPreviewFilesSkeletonVisible);
    }

    private static List<ItemsControl> FindItemsControls(InstancesView view)
        => view.GetVisualDescendants().OfType<ItemsControl>().ToList();

    private static List<StackPanel> FindPanels(InstancesView view)
        => view.GetVisualDescendants().OfType<StackPanel>().ToList();

    private static List<Border> FindRows(InstancesView view, string className)
        => view.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains(className))
            .ToList();

    private static List<TextBlock> FindConsoleLines(InstancesView view)
        => view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.Classes.Contains("consoleText"))
            .ToList();

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 50 && !condition(); attempt++)
            await Task.Delay(20);

        Assert.True(condition());
    }

    private sealed class CountingImageHandler : HttpMessageHandler
    {
        public int Requests;

        private static readonly byte[] SinglePixelPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ" +
            "AAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(SinglePixelPng)
            });
        }
    }
}
