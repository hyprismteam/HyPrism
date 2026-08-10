// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Models;
using HyPrism.Services.Core.App;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Game;
using HyPrism.Services.Game.Instance;
using HyPrism.Services.Game.Launch;

namespace HyPrism.Core.Tests.Game;

public sealed class GameLaunchCoordinatorTests
{
    private readonly Mock<IGameSessionService> _gameSession = new();
    private readonly Mock<IGameProcessService> _gameProcess = new();
    private readonly Mock<IInstanceService> _instances = new();
    private readonly Mock<IConfigService> _config = new();
    private readonly Mock<IProgressNotificationService> _progress = new();

    [Fact]
    public async Task LaunchAsync_UsesRequestedInstanceAndPreference()
    {
        var instance = CreateInstalledInstance();
        Func<bool>? preference = null;

        _gameProcess.Setup(service => service.IsGameRunning()).Returns(false);
        _config.SetupGet(service => service.Configuration)
            .Returns(new Config { LaunchAfterDownload = true });
        _instances.Setup(service => service.GetSelectedInstance()).Returns(instance);
        _instances.Setup(service => service.GetInstancePathById(instance.Id)).Returns("/game");
        _instances.Setup(service => service.IsClientPresent("/game")).Returns(true);
        _gameSession
            .Setup(service => service.DownloadAndLaunchAsync(It.IsAny<Func<bool>>()))
            .Callback<Func<bool>?>(value => preference = value)
            .ReturnsAsync(new DownloadProgress { Success = true });

        await CreateSubject().LaunchAsync(instance.Id, launchAfterDownload: false);

        _instances.Verify(service => service.SetSelectedInstance(instance.Id), Times.Once);
        Assert.NotNull(preference);
        Assert.False(preference!());
        _progress.Verify(
            service => service.ReportGameStateChanged("stopped", 0),
            Times.Once);
    }

    [Fact]
    public async Task LaunchAsync_WhenClientIsMissing_ReportsErrorWithoutStartingSession()
    {
        var instance = CreateInstalledInstance();

        _gameProcess.Setup(service => service.IsGameRunning()).Returns(false);
        _config.SetupGet(service => service.Configuration).Returns(new Config());
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
            service => service.DownloadAndLaunchAsync(It.IsAny<Func<bool>>()),
            Times.Never);
    }

    [Fact]
    public async Task LaunchAsync_MapsMirrorFailureToStableExitCode()
    {
        var instance = CreateInstalledInstance();

        _gameProcess.Setup(service => service.IsGameRunning()).Returns(false);
        _config.SetupGet(service => service.Configuration).Returns(new Config());
        _instances.Setup(service => service.GetSelectedInstance()).Returns(instance);
        _instances.Setup(service => service.GetInstancePathById(instance.Id)).Returns("/game");
        _instances.Setup(service => service.IsClientPresent("/game")).Returns(true);
        _gameSession
            .Setup(service => service.DownloadAndLaunchAsync(It.IsAny<Func<bool>>()))
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
            _config.Object,
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
