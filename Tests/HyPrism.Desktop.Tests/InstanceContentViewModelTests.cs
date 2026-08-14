// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Accounts;
using HyPrism.Core.Application.Ports;
using HyPrism.Core.Application.Progress;
using HyPrism.Core.Game;
using HyPrism.Core.Game.Instances;
using HyPrism.Core.Game.Launch;
using HyPrism.Core.Game.Mods;
using HyPrism.Core.Models;
using HyPrism.Desktop.Features.News;
using HyPrism.Desktop.Features.Settings;
using HyPrism.Desktop.Localization;
using HyPrism.Desktop.Platform;
using HyPrism.Desktop.Shell;
using Moq;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class InstanceContentViewModelTests
{
    [Fact]
    public async Task ModsAndCatalogUseTheSelectedInstancePath()
    {
        const string instancePath = "/tmp/hyprism-instance-content-test";
        var selected = new InstanceInfo
        {
            Id = "selected-instance",
            Name = "Selected Instance",
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

        instances.Setup(service => service.GetCachedInstances()).Returns([selected]);
        instances.Setup(service => service.GetSelectedInstance()).Returns(selected);
        instances.Setup(service => service.GetInstancePathById(selected.Id)).Returns(instancePath);
        instances.Setup(service => service.IsClientPresent(instancePath)).Returns(true);
        instances.Setup(service => service.GetInstanceMeta(instancePath)).Returns(new InstanceMeta
        {
            Id = selected.Id,
            Name = selected.Name,
            Branch = selected.Branch,
            Version = selected.Version,
            PlayTimeSeconds = 3720
        });
        profiles.Setup(service => service.GetNick()).Returns("Instance Test");
        settings.SetupGet(service => service.AvailableBackgrounds).Returns([]);
        modManager.Setup(service => service.GetInstanceInstalledMods(instancePath)).Returns(
        [
            new InstalledMod
            {
                Id = "cf-101",
                CurseForgeId = "101",
                Name = "Installed Mod",
                Version = "1.0",
                Author = "Author",
                Enabled = true
            }
        ]);
        modManager.Setup(service => service.SearchModsAsync(
                It.IsAny<string>(), 0, 24, It.IsAny<string[]>(), 2, 1))
            .ReturnsAsync(new ModSearchResult
            {
                Mods =
                [
                    new ModInfo
                    {
                        Id = "202",
                        Name = "Catalog Mod",
                        Author = "Creator",
                        LatestFileId = "303"
                    }
                ],
                TotalCount = 1
            });
        modManager.Setup(service => service.InstallModFileToInstanceAsync(
                "202", "303", instancePath, It.IsAny<Action<string, string>?>()))
            .ReturnsAsync(true);

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
            modManager: modManager.Object);

        viewModel.SelectInstanceSectionCommand.Execute("mods");
        await WaitUntilAsync(() => viewModel.InstalledMods.Count == 1);
        Assert.Equal("1", viewModel.InstanceModsCountText);
        Assert.Equal("0", viewModel.InstanceWorldsCountText);
        Assert.Equal("1 h 2 min", viewModel.SelectedInstancePlayTime);
        viewModel.CloseInstanceSectionCommand.Execute(null);
        viewModel.SelectInstanceSectionCommand.Execute("console");
        Assert.True(viewModel.IsInstanceConsoleSection);
        Assert.Equal("Console", viewModel.InstanceSectionTitle);
        viewModel.SelectInstanceSectionCommand.Execute("logs");
        Assert.True(viewModel.IsInstanceLogsSection);
        Assert.Equal("Logs", viewModel.InstanceSectionTitle);
        Assert.Equal("Logs", viewModel.DisplayedInstanceSectionTitle);
        viewModel.CloseInstanceSectionCommand.Execute(null);
        Assert.Equal("Logs", viewModel.DisplayedInstanceSectionTitle);
        viewModel.SelectInstanceSectionCommand.Execute("browse");
        await WaitUntilAsync(() => viewModel.ModCatalogItems.Count == 1);
        await viewModel.InstallModCommand.ExecuteAsync(viewModel.ModCatalogItems[0]);

        modManager.Verify(service => service.GetInstanceInstalledMods(instancePath), Times.AtLeastOnce);
        modManager.Verify(service => service.InstallModFileToInstanceAsync(
            "202", "303", instancePath, It.IsAny<Action<string, string>?>()), Times.Once);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 50 && !condition(); attempt++)
            await Task.Delay(20);

        Assert.True(condition());
    }
}
