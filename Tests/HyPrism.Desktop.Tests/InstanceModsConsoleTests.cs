// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net.Http;
using Avalonia.Headless.XUnit;
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

public sealed class InstanceModsConsoleTests
{
    [AvaloniaFact]
    public async Task ModSelectionAndToggleDriveInstalledModCommands()
    {
        const string instancePath = "/tmp/hyprism-mod-toggle-test";
        var instance = new InstanceInfo
        {
            Id = "mods-instance",
            Name = "Mods Instance",
            Branch = "release",
            Version = 20,
            IsInstalled = true
        };
        var (instances, profiles, profileRepository, launchCoordinator, installationWorkflow,
            gameProcess, progress, settings, news, uriLauncher) = CreateFakes(instance, instancePath);
        var modManager = new Mock<IModManager>();
        modManager.Setup(service => service.GetInstanceInstalledMods(instancePath)).Returns(
        [
            new InstalledMod
            {
                Id = "cf-1",
                CurseForgeId = "1",
                Name = "First Mod",
                Version = "1.0",
                Author = "Author",
                Enabled = true
            },
            new InstalledMod
            {
                Id = "cf-2",
                CurseForgeId = "2",
                Name = "Second Mod",
                Version = "2.0",
                Author = "Author",
                Enabled = true
            }
        ]);
        modManager.Setup(service => service.SetModEnabledAsync(
                instancePath, "cf-1", false))
            .ReturnsAsync(true);

        using var viewModel = CreateViewModel(
            instances, profiles, profileRepository, launchCoordinator, installationWorkflow,
            gameProcess, progress, settings, news, uriLauncher, modManager);

        viewModel.SelectInstanceSectionCommand.Execute("mods");
        await WaitUntilAsync(() => viewModel.InstalledMods.Count == 2);

        var first = viewModel.InstalledMods.First(mod => mod.Id == "cf-1");
        first.IsSelected = true;
        Assert.Equal(1, viewModel.SelectedModCount);
        Assert.True(viewModel.HasModSelection);

        await viewModel.ToggleModCommand.ExecuteAsync(first);
        Assert.False(first.IsEnabled);
        modManager.Verify(
            service => service.SetModEnabledAsync(instancePath, "cf-1", false),
            Times.Once);

        viewModel.ClearInstalledModsSelectionCommand.Execute(null);
        Assert.Equal(0, viewModel.SelectedModCount);
        Assert.False(viewModel.HasModSelection);
    }

    [AvaloniaFact]
    public async Task DeleteSelectedModsRemovesEachSelectedMod()
    {
        const string instancePath = "/tmp/hyprism-mod-delete-test";
        var instance = new InstanceInfo
        {
            Id = "delete-instance",
            Name = "Delete Instance",
            Branch = "release",
            Version = 20,
            IsInstalled = true
        };
        var (instances, profiles, profileRepository, launchCoordinator, installationWorkflow,
            gameProcess, progress, settings, news, uriLauncher) = CreateFakes(instance, instancePath);
        var firstMod = new InstalledMod
        {
            Id = "cf-1",
            Name = "First Mod",
            Version = "1.0",
            Author = "Author",
            Enabled = true
        };
        var secondMod = new InstalledMod
        {
            Id = "cf-2",
            Name = "Second Mod",
            Version = "2.0",
            Author = "Author",
            Enabled = true
        };
        var modManager = new Mock<IModManager>();
        modManager.SetupSequence(service => service.GetInstanceInstalledMods(instancePath))
            .Returns([firstMod, secondMod])
            .Returns([]);
        modManager.Setup(service => service.RemoveInstalledModAsync(
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        using var viewModel = CreateViewModel(
            instances, profiles, profileRepository, launchCoordinator, installationWorkflow,
            gameProcess, progress, settings, news, uriLauncher, modManager);

        viewModel.SelectInstanceSectionCommand.Execute("mods");
        await WaitUntilAsync(() => viewModel.InstalledMods.Count == 2);

        foreach (var mod in viewModel.InstalledMods)
            mod.IsSelected = true;
        Assert.Equal(2, viewModel.SelectedModCount);

        await viewModel.DeleteSelectedModsCommand.ExecuteAsync(null);
        modManager.Verify(
            service => service.RemoveInstalledModAsync(instancePath, "cf-1"),
            Times.Once);
        modManager.Verify(
            service => service.RemoveInstalledModAsync(instancePath, "cf-2"),
            Times.Once);
        await WaitUntilAsync(() => viewModel.InstalledMods.Count == 0);
        Assert.Equal(0, viewModel.SelectedModCount);
    }

    [AvaloniaFact]
    public async Task ConsoleSectionStreamsFiltersAndClearsGameLines()
    {
        const string instancePath = "/tmp/hyprism-console-test";
        var instance = new InstanceInfo
        {
            Id = "console-instance",
            Name = "Console Instance",
            Branch = "release",
            Version = 20,
            IsInstalled = true
        };
        var (instances, profiles, profileRepository, launchCoordinator, installationWorkflow,
            gameProcess, progress, settings, news, uriLauncher) = CreateFakes(instance, instancePath);
        var console = new GameConsoleService();
        console.Append(instance.Id, "OUT", "hello from game");

        using var viewModel = CreateViewModel(
            instances, profiles, profileRepository, launchCoordinator, installationWorkflow,
            gameProcess, progress, settings, news, uriLauncher,
            new Mock<IModManager>(),
            console);

        viewModel.SelectInstanceSectionCommand.Execute("console");
        Assert.Single(viewModel.ConsoleLines);
        Assert.Equal("hello from game", viewModel.ConsoleLines[0].Text);

        console.Append(instance.Id, "ERR", "boom");
        await WaitUntilAsync(() => viewModel.ConsoleLines.Count == 2);
        Assert.True(viewModel.ConsoleLines[1].IsError);

        console.Append("other-instance", "OUT", "not ours");
        await Task.Delay(300);
        Assert.Equal(2, viewModel.ConsoleLines.Count);

        viewModel.ConsoleSearchQuery = "boom";
        Assert.Single(viewModel.ConsoleLines);
        Assert.Equal("boom", viewModel.ConsoleLines[0].Text);

        viewModel.ConsoleSearchQuery = string.Empty;
        Assert.Equal(2, viewModel.ConsoleLines.Count);

        viewModel.ClearConsoleCommand.Execute(null);
        Assert.Empty(viewModel.ConsoleLines);
    }

    private static (
        Mock<IInstanceRepository> Instances,
        Mock<IProfileManager> Profiles,
        Mock<IProfileRepository> ProfileRepository,
        Mock<IGameLaunchCoordinator> LaunchCoordinator,
        Mock<IGameInstallationWorkflow> InstallationWorkflow,
        Mock<IGameProcessTracker> GameProcess,
        Mock<IProgressReporter> Progress,
        Mock<IDesktopSettingsStore> Settings,
        Mock<IHytaleNewsClient> News,
        Mock<IExternalUriLauncher> UriLauncher) CreateFakes(
            InstanceInfo instance,
            string instancePath)
    {
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

        instances.Setup(service => service.GetCachedInstances()).Returns([instance]);
        instances.Setup(service => service.GetSelectedInstance()).Returns(instance);
        instances.Setup(service => service.GetInstancePathById(instance.Id)).Returns(instancePath);
        instances.Setup(service => service.IsClientPresent(instancePath)).Returns(true);
        profiles.Setup(service => service.GetNick()).Returns("Console Test");
        settings.SetupGet(service => service.AvailableBackgrounds).Returns([]);

        return (instances, profiles, profileRepository, launchCoordinator, installationWorkflow,
            gameProcess, progress, settings, news, uriLauncher);
    }

    private static MainWindowViewModel CreateViewModel(
        Mock<IInstanceRepository> instances,
        Mock<IProfileManager> profiles,
        Mock<IProfileRepository> profileRepository,
        Mock<IGameLaunchCoordinator> launchCoordinator,
        Mock<IGameInstallationWorkflow> installationWorkflow,
        Mock<IGameProcessTracker> gameProcess,
        Mock<IProgressReporter> progress,
        Mock<IDesktopSettingsStore> settings,
        Mock<IHytaleNewsClient> news,
        Mock<IExternalUriLauncher> uriLauncher,
        Mock<IModManager> modManager,
        IGameConsoleService? gameConsole = null)
        => new(
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
            gameConsole: gameConsole);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 50 && !condition(); attempt++)
            await Task.Delay(20);

        Assert.True(condition());
    }
}
