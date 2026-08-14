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

    [AvaloniaFact]
    public async Task ManagedActionOwnsProgressCancellationAndRunningStateWithoutGlobalNotify()
    {
        var instance = new InstanceInfo
        {
            Id = "action-instance",
            Name = "Action Instance",
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
        var launchCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementLaunchCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var launchThreadObserved = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var launchCallCount = 0;

        instances.Setup(service => service.GetCachedInstances()).Returns([instance]);
        instances.Setup(service => service.GetSelectedInstance()).Returns(instance);
        instances.Setup(service => service.GetInstancePathById(instance.Id)).Returns("/tmp/action-instance");
        instances.Setup(service => service.IsClientPresent("/tmp/action-instance")).Returns(true);
        profiles.Setup(service => service.GetNick()).Returns("Action Test");
        settings.SetupGet(service => service.AvailableBackgrounds).Returns([]);
        launchCoordinator.Setup(service => service.LaunchAsync(
                instance.Id,
                It.IsAny<AuthUriPresenter?>()))
            .Returns(() =>
            {
                launchThreadObserved.TrySetResult(Avalonia.Threading.Dispatcher.UIThread.CheckAccess());
                return Interlocked.Increment(ref launchCallCount) == 1
                    ? launchCompletion.Task
                    : replacementLaunchCompletion.Task;
            });
        gameProcess.Setup(service => service.ExitGame()).Returns(true);

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
            new StringLocalizer("en-US"));

        var launchOperation = viewModel.RunManagedInstanceCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsManagedInstanceActionActive);
        Assert.Equal("Launching", viewModel.ManagedInstanceActionStatusText);
        Assert.Equal("0:00", viewModel.ManagedInstanceActionMetricText);
        Assert.False(await launchThreadObserved.Task);

        progress.Raise(service => service.GameStateChanged += null!, "started", 123);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.IsManagedInstanceActionRunning);
        Assert.Equal("Running", viewModel.ManagedInstanceActionStatusText);

        await viewModel.RunManagedInstanceCommand.ExecuteAsync(null);
        gameProcess.Verify(service => service.ExitGame(), Times.Never);

        viewModel.ArmManagedInstanceCancellation();
        await viewModel.RunManagedInstanceCommand.ExecuteAsync(null);
        gameProcess.Verify(service => service.ExitGame(), Times.Once);

        progress.Raise(service => service.GameStateChanged += null!, "stopped", 0);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(viewModel.IsManagedInstanceActionActive);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanRunManagedInstanceAction);

        var replacementLaunch = viewModel.RunManagedInstanceCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsBusy);

        launchCompletion.SetResult();
        await launchOperation;
        Assert.True(viewModel.IsBusy);

        progress.Raise(service => service.GameStateChanged += null!, "stopped", 0);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        replacementLaunchCompletion.SetResult();
        await replacementLaunch;
    }

    [AvaloniaFact]
    public async Task ManagedInstallShowsProgressAndSecondActionCancelsIt()
    {
        var instance = new InstanceInfo
        {
            Id = "install-instance",
            Name = "Install Instance",
            Branch = "pre-release",
            Version = 21,
            IsInstalled = false
        };
        var otherInstance = new InstanceInfo
        {
            Id = "other-install-instance",
            Name = "Other Install Instance",
            Branch = "release",
            Version = 20,
            IsInstalled = false
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
        var installCompletion = new TaskCompletionSource<DownloadProgress>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        instances.Setup(service => service.GetCachedInstances()).Returns([instance, otherInstance]);
        instances.Setup(service => service.GetSelectedInstance()).Returns(instance);
        instances.Setup(service => service.GetInstancePathById(instance.Id)).Returns("/tmp/install-instance");
        instances.Setup(service => service.GetInstancePathById(otherInstance.Id))
            .Returns("/tmp/other-install-instance");
        instances.Setup(service => service.IsClientPresent("/tmp/install-instance")).Returns(false);
        instances.Setup(service => service.IsClientPresent("/tmp/other-install-instance")).Returns(false);
        profiles.Setup(service => service.GetNick()).Returns("Install Test");
        settings.SetupGet(service => service.AvailableBackgrounds).Returns([]);
        installationWorkflow.Setup(service => service.DownloadAndLaunchInstanceAsync(
                instance.Id,
                It.IsAny<AuthUriPresenter?>()))
            .Returns(installCompletion.Task);

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
            new StringLocalizer("en-US"));

        var installOperation = viewModel.RunManagedInstanceCommand.ExecuteAsync(null);
        progress.Raise(service => service.DownloadProgressChanged += null!, new ProgressUpdateMessage
        {
            State = "downloading",
            Progress = 0.37,
            MessageKey = "common.loading"
        });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.IsManagedInstanceActionActive);
        Assert.Equal("Loading...", viewModel.ManagedInstanceActionStatusText);
        Assert.Equal("37%", viewModel.ManagedInstanceActionMetricText);

        await viewModel.RunManagedInstanceCommand.ExecuteAsync(null);
        installationWorkflow.Verify(service => service.CancelDownload(), Times.Never);

        viewModel.ArmManagedInstanceCancellation();
        await viewModel.RunManagedInstanceCommand.ExecuteAsync(null);
        installationWorkflow.Verify(service => service.CancelDownload(), Times.Once);

        installCompletion.SetResult(new DownloadProgress { Cancelled = true });
        await installOperation;
        Assert.False(viewModel.IsManagedInstanceActionActive);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanRunManagedInstanceAction);

        progress.Raise(service => service.DownloadProgressChanged += null!, new ProgressUpdateMessage
        {
            State = "downloading",
            Progress = 0.42,
            MessageKey = "common.loading"
        });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanRunManagedInstanceAction);

        viewModel.OpenInstanceDetailsCommand.Execute(otherInstance.Id);
        Assert.Equal(
            otherInstance.Id,
            Assert.Single(viewModel.AllInstances, item => item.IsManaged).Id);
        Assert.True(viewModel.CanRunManagedInstanceAction);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 50 && !condition(); attempt++)
            await Task.Delay(20);

        Assert.True(condition());
    }
}
