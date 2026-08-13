// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Models;
using HyPrism.Core.Application.Progress;
using HyPrism.Core.Game;
using HyPrism.Core.Game.Instances;
using HyPrism.Core.Game.Launch;
using HyPrism.Core.Accounts;

namespace HyPrism.Core.Tests.Game;

public sealed class GameLaunchCoordinatorTests
{
    private readonly Mock<IGameInstallationWorkflow> _gameSession = new();
    private readonly Mock<IGameProcessTracker> _gameProcess = new();
    private readonly Mock<IInstanceRepository> _instances = new();
    private readonly Mock<IProgressReporter> _progress = new();

    [Fact]
    public async Task LaunchAsync_UsesRequestedInstanceAndForwardsAuthorizationPresenter()
    {
        var instance = CreateInstalledInstance();
        AuthUriPresenter uriLauncher = (_, _) => Task.FromResult(true);
        AuthUriPresenter? forwardedUriLauncher = null;

        _gameProcess.Setup(service => service.IsGameRunning()).Returns(false);
        _instances.Setup(service => service.GetSelectedInstance()).Returns(instance);
        _instances.Setup(service => service.GetInstancePathById(instance.Id)).Returns("/game");
        _instances.Setup(service => service.IsClientPresent("/game")).Returns(true);
        _gameSession
            .Setup(service => service.DownloadAndLaunchAsync(It.IsAny<AuthUriPresenter?>()))
            .Callback<AuthUriPresenter?>(launcher => forwardedUriLauncher = launcher)
            .ReturnsAsync(new DownloadProgress { Success = true });

        await CreateSubject().LaunchAsync(
            instance.Id,
            authorizationUriPresenter: uriLauncher);

        _instances.Verify(service => service.SetSelectedInstance(instance.Id), Times.Once);
        Assert.Same(uriLauncher, forwardedUriLauncher);
    }

    [Fact]
    public async Task LaunchAsync_WhenClientIsMissing_ReportsErrorWithoutStartingSession()
    {
        var instance = CreateInstalledInstance();

        _gameProcess.Setup(service => service.IsGameRunning()).Returns(false);
        _instances.Setup(service => service.GetSelectedInstance()).Returns(instance);
        _instances.Setup(service => service.GetInstancePathById(instance.Id)).Returns("/game");
        _instances.Setup(service => service.IsClientPresent("/game")).Returns(false);

        await CreateSubject().LaunchAsync();

        _progress.Verify(
            service => service.ReportError(
                "launch",
                "Game not installed",
                It.Is<string>(value => value.Contains(instance.Name))),
            Times.Once);
        _gameSession.Verify(
            service => service.DownloadAndLaunchAsync(It.IsAny<AuthUriPresenter?>()),
            Times.Never);
    }

    [Fact]
    public async Task LaunchAsync_MapsMirrorFailureToStableExitCode()
    {
        var instance = CreateInstalledInstance();

        _gameProcess.Setup(service => service.IsGameRunning()).Returns(false);
        _instances.Setup(service => service.GetSelectedInstance()).Returns(instance);
        _instances.Setup(service => service.GetInstancePathById(instance.Id)).Returns("/game");
        _instances.Setup(service => service.IsClientPresent("/game")).Returns(true);
        _gameSession
            .Setup(service => service.DownloadAndLaunchAsync(It.IsAny<AuthUriPresenter?>()))
            .ReturnsAsync(new DownloadProgress
            {
                Success = false,
                Error = "Mirror unreachable"
            });

        await CreateSubject().LaunchAsync();

        _progress.Verify(
            service => service.ReportGameStateChanged("stopped", 14),
            Times.Once);
    }

    private GameLaunchCoordinator CreateSubject()
        => new(
            _gameSession.Object,
            _gameProcess.Object,
            _instances.Object,
            _progress.Object);

    private static InstanceInfo CreateInstalledInstance()
        => new()
        {
            Id = "release-instance",
            Name = "Hytale Release",
            Branch = "release",
            Version = 42,
            IsInstalled = true
        };
}
