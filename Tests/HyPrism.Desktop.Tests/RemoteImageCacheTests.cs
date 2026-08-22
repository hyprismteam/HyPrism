// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using HyPrism.Core;
using HyPrism.Desktop.Platform;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class RemoteImageCacheTests
{
    [Fact]
    public async Task EncodedImagesSurviveLauncherRestartInDiskCache()
    {
        var appDirectory = Path.Combine(
            Path.GetTempPath(),
            $"hyprism-image-cache-{Guid.NewGuid():N}");

        try
        {
            var onlineHandler = new ImageHandler([1, 2, 3, 4]);
            using (var onlineClient = new HttpClient(onlineHandler))
            {
                var cache = new RemoteImageCache(
                    onlineClient,
                    new AppPathConfiguration(appDirectory));
                Assert.Equal(
                    [1, 2, 3, 4],
                    await cache.GetBytesAsync(
                        "https://cdn.example.com/cover.png",
                        "news",
                        TestContext.Current.CancellationToken));
                Assert.Equal(
                    [1, 2, 3, 4],
                    await cache.GetBytesAsync(
                        "https://cdn.example.com/cover.png",
                        "news",
                        TestContext.Current.CancellationToken));
            }

            Assert.Equal(1, onlineHandler.Requests);
            Assert.Single(Directory.EnumerateFiles(
                Path.Combine(appDirectory, "Cache", "RemoteImages", "news"),
                "*.bin"));

            var offlineHandler = new ImageHandler(null);
            using var offlineClient = new HttpClient(offlineHandler);
            var restartedCache = new RemoteImageCache(
                offlineClient,
                new AppPathConfiguration(appDirectory));

            Assert.Equal(
                [1, 2, 3, 4],
                await restartedCache.GetBytesAsync(
                    "https://cdn.example.com/cover.png",
                    "news",
                    TestContext.Current.CancellationToken));
            Assert.Equal(0, offlineHandler.Requests);
        }
        finally
        {
            if (Directory.Exists(appDirectory))
                Directory.Delete(appDirectory, recursive: true);
        }
    }

    private sealed class ImageHandler(byte[]? payload) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            if (payload is null)
                throw new HttpRequestException("Network access was not expected");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
                RequestMessage = request
            });
        }
    }
}
