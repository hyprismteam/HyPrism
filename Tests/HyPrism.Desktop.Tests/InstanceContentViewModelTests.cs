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
using Avalonia.Headless.XUnit;
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
    [AvaloniaFact]
    public async Task ManagerContentUsesItsOwnInstanceWithoutChangingLaunchSelection()
    {
        const string instancePath = "/tmp/hyprism-instance-content-test";
        var managed = new InstanceInfo
        {
            Id = "managed-instance",
            Name = "Managed Instance",
            Branch = "release",
            Version = 20,
            IsInstalled = true
        };
        var other = new InstanceInfo
        {
            Id = "other-instance",
            Name = "Other Instance",
            Branch = "pre-release",
            Version = 21
        };
        var selectedForLaunch = new InstanceInfo
        {
            Id = "launch-instance",
            Name = "Launch Instance",
            Branch = "release",
            Version = 19
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

        instances.Setup(service => service.GetCachedInstances()).Returns([managed, other, selectedForLaunch]);
        instances.Setup(service => service.GetSelectedInstance()).Returns(selectedForLaunch);
        instances.Setup(service => service.GetInstancePathById(managed.Id)).Returns(instancePath);
        instances.Setup(service => service.IsClientPresent(instancePath)).Returns(true);
        instances.Setup(service => service.GetInstanceMeta(instancePath)).Returns(new InstanceMeta
        {
            Id = managed.Id,
            Name = managed.Name,
            Branch = managed.Branch,
            Version = managed.Version,
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
        launchCoordinator.Setup(service => service.LaunchAsync(
                managed.Id,
                It.IsAny<AuthUriPresenter?>()))
            .Returns(Task.CompletedTask);
        installationWorkflow.Setup(service => service.DownloadAndLaunchInstanceAsync(
                other.Id,
                It.IsAny<AuthUriPresenter?>()))
            .ReturnsAsync(new DownloadProgress { Success = true });
        uriLauncher.Setup(service => service.LaunchDirectoryAsync(instancePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        instances.Setup(service => service.DeleteGameById(other.Id)).Returns(true);

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
        Assert.Equal("Launch Instance", viewModel.SelectedInstanceName);
        Assert.Equal("Managed Instance", viewModel.ManagedInstanceName);
        Assert.Equal(managed.Id, Assert.Single(viewModel.AllInstances, instance => instance.IsManaged).Id);
        Assert.Equal("1", viewModel.InstanceModsCountText);
        Assert.Equal("0", viewModel.InstanceWorldsCountText);
        Assert.Equal("1 h 2 min", viewModel.ManagedInstancePlayTime);
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

        viewModel.OpenInstanceDetailsCommand.Execute(other.Id);
        Assert.Equal("Other Instance", viewModel.ManagedInstanceName);
        Assert.Equal(other.Id, Assert.Single(viewModel.AllInstances, instance => instance.IsManaged).Id);
        Assert.Equal("Not Installed", viewModel.ManagedInstanceState);
        Assert.False(viewModel.IsManagedInstanceInstalled);
        Assert.Equal("Launch Instance", viewModel.SelectedInstanceName);
        instances.Verify(service => service.SetSelectedInstance(It.IsAny<string>()), Times.Never);

        viewModel.OpenInstanceDetailsCommand.Execute(managed.Id);
        Assert.Equal("Ready", viewModel.ManagedInstanceState);
        Assert.Equal(managed.Id, Assert.Single(viewModel.AllInstances, instance => instance.IsManaged).Id);
        Assert.True(viewModel.IsManagedInstanceInstalled);
        await viewModel.OpenManagedInstanceFolderCommand.ExecuteAsync(null);
        await viewModel.RunManagedInstanceCommand.ExecuteAsync(null);
        uriLauncher.Verify(service => service.LaunchDirectoryAsync(
            instancePath,
            It.IsAny<CancellationToken>()), Times.Once);
        launchCoordinator.Verify(service => service.LaunchAsync(
            managed.Id,
            It.IsAny<AuthUriPresenter?>()), Times.Once);

        viewModel.OpenInstanceDetailsCommand.Execute(other.Id);
        Assert.Equal("Not Installed", viewModel.ManagedInstanceState);
        Assert.Equal(other.Id, Assert.Single(viewModel.AllInstances, instance => instance.IsManaged).Id);
        Assert.False(viewModel.IsManagedInstanceInstalled);
        await viewModel.RunManagedInstanceCommand.ExecuteAsync(null);
        installationWorkflow.Verify(service => service.DownloadAndLaunchInstanceAsync(
            other.Id,
            It.IsAny<AuthUriPresenter?>()), Times.Once);
        instances.Verify(service => service.SetSelectedInstance(It.IsAny<string>()), Times.Never);

        viewModel.MoveInstance(managed.Id, 2);
        Assert.Equal(
            [other.Id, selectedForLaunch.Id, managed.Id],
            viewModel.AllInstances.Select(instance => instance.Id));
        instances.Verify(service => service.SetInstanceOrder(
            It.Is<IReadOnlyList<string>>(ids =>
                ids.SequenceEqual(new[] { other.Id, selectedForLaunch.Id, managed.Id }))), Times.Once);

        viewModel.DeleteManagedInstanceCommand.Execute(null);
        instances.Verify(service => service.DeleteGameById(other.Id), Times.Once);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 50 && !condition(); attempt++)
            await Task.Delay(20);

        Assert.True(condition());
    }
}
