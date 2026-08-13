// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Headless.XUnit;
using HyPrism.Core.Game.Sources;
using HyPrism.Core.Game.Versions;
using HyPrism.Core.Models;
using HyPrism.Desktop.Features.Settings;
using HyPrism.Desktop.Localization;
using HyPrism.Desktop.Platform;
using Moq;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class MirrorSettingsViewModelTests
{
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
}
