// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Accounts;
using HyPrism.Core.Application.Ports;
using HyPrism.Core.Application.Progress;
using HyPrism.Core.Game;
using HyPrism.Core.Game.Instances;
using HyPrism.Core.Game.Launch;
using HyPrism.Core.Game.Versions;
using HyPrism.Desktop.Features.Settings;
using HyPrism.Desktop.Features.News;
using HyPrism.Desktop.Localization;
using HyPrism.Desktop.Platform;
using HyPrism.Desktop.Shell;
using Avalonia.Headless.XUnit;
using Moq;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class InstanceWizardViewModelTests
{
    [AvaloniaFact]
    public void SwitchingToCachedBranchDoesNotStartVersionLoading()
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
        var versionCatalog = new Mock<IGameVersionCatalog>();
        var releaseVersions = new List<int> { 20, 19 };
        var preReleaseVersions = new List<int> { 61, 60 };

        instances.Setup(service => service.GetCachedInstances()).Returns([]);
        profiles.Setup(service => service.GetNick()).Returns("Wizard Test");
        settings.SetupGet(service => service.AvailableBackgrounds).Returns([]);
        versionCatalog
            .Setup(service => service.TryGetCachedVersions(
                "release",
                It.IsAny<TimeSpan>(),
                out releaseVersions))
            .Returns(true);
        versionCatalog
            .Setup(service => service.TryGetCachedVersions(
                "pre-release",
                It.IsAny<TimeSpan>(),
                out preReleaseVersions))
            .Returns(true);

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
            versionCatalog: versionCatalog.Object);

        viewModel.OpenInstanceCreatorCommand.Execute(null);
        viewModel.SetNewInstanceBranchCommand.Execute("pre-release");

        Assert.False(viewModel.IsInstanceVersionsLoading);
        Assert.Equal([61, 60], viewModel.AvailableInstanceVersions.Select(item => item.Version));
        versionCatalog.Verify(
            service => service.GetVersionListAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
