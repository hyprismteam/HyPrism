// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Text;
using HyPrism.Models;
using HyPrism.Services.Core.App;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Game;
using HyPrism.Services.Game.Instance;
using HyPrism.Services.Game.Version;

namespace HyPrism.Core.Tests.Core.App;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task CheckForLauncherUpdates_UsesOnlyLatestPublishedReleaseEndpoint()
    {
        var handler = new RecordingHandler(
            """
            {
              "tag_name": "v999.0.0",
              "html_url": "https://github.com/hyprismteam/HyPrism/releases/tag/v999.0.0",
              "body": "Release notes",
              "prerelease": false,
              "assets": [
                {
                  "name": "HyPrism-999.0.0-linux-x64.AppImage",
                  "browser_download_url": "https://example.invalid/HyPrism.AppImage"
                },
                {
                  "name": "HyPrism-999.0.0-win-x64.zip",
                  "browser_download_url": "https://example.invalid/HyPrism.zip"
                },
                {
                  "name": "HyPrism-999.0.0-osx-arm64.dmg",
                  "browser_download_url": "https://example.invalid/HyPrism.dmg"
                }
              ]
            }
            """);
        using var httpClient = new HttpClient(handler);
        var config = new Mock<IConfigService>();
        config.SetupGet(service => service.Configuration).Returns(new Config());
        var service = new UpdateService(
            httpClient,
            config.Object,
            Mock.Of<IVersionService>(),
            Mock.Of<IInstanceService>(),
            Mock.Of<IProgressNotificationService>());
        object? update = null;
        service.LauncherUpdateAvailable += value => update = value;

        await service.CheckForLauncherUpdatesAsync();

        Assert.Equal(
            "https://api.github.com/repos/hyprismteam/HyPrism/releases/latest",
            handler.RequestUri?.ToString());
        Assert.NotNull(update);
        Assert.Equal("999.0.0", update.GetType().GetProperty("latestVersion")?.GetValue(update));
        Assert.Null(update.GetType().GetProperty("isBeta"));
    }

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
        }
    }
}
