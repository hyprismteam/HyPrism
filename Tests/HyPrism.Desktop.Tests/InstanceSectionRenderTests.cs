// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
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
                    }
                ],
                TotalCount = 1
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
        await WaitUntilAsync(() =>
            FindRows(view, "instanceModRow").Any(border =>
                border.IsEffectivelyVisible &&
                border.GetVisualDescendants().OfType<Button>().Any(button =>
                    button.Classes.Contains("instanceInstall"))));
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

        await viewModel.SelectModCatalogPreviewCommand.ExecuteAsync(viewModel.ModCatalogItems[0]);
        await WaitUntilAsync(() => viewModel.ModCatalogPreviewFiles.Count == 1);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        await WaitUntilAsync(() => view.GetVisualDescendants()
            .OfType<ListBox>()
            .Any(listBox => listBox.IsEffectivelyVisible && listBox.Classes.Contains("instancePreviewFiles")));
        var preview = FindRows(view, "instanceModPreview").Single();
        Assert.True(preview.IsEffectivelyVisible);
        Assert.Contains(
            preview.GetVisualDescendants(),
            element => element is ListBox listBox && listBox.Classes.Contains("instancePreviewFiles"));

        var previewPath = Environment.GetEnvironmentVariable("HYPRISM_MOD_CATALOG_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(previewPath))
        {
            window.CaptureRenderedFrame()!.Save(previewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(previewPath));
        }

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
}
