// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using HyPrism.Core.Game.Launch;

namespace HyPrism.Core.Tests.Game.Launch;

public sealed class RuntimeProvisionerTests
{
    [Fact]
    public async Task EnsureJreInstalledAsync_InvalidOfficialChecksum_RejectsArchive()
    {
        var appDir = CreateTemporaryDirectory();
        var handler = new RuntimeDownloadHandler();
        using var httpClient = new HttpClient(handler);
        var provisioner = new RuntimeProvisioner(appDir, httpClient);

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                provisioner.EnsureJREInstalledAsync((_, _) => { }));

            Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(appDir, "Cache", handler.ArchiveFileName)));
            Assert.Equal(2, handler.RequestCount);
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureJreInstalledAsync_PreCancelled_DoesNotStartNetworkRequest()
    {
        var appDir = CreateTemporaryDirectory();
        var handler = new RuntimeDownloadHandler();
        using var httpClient = new HttpClient(handler);
        var provisioner = new RuntimeProvisioner(appDir, httpClient);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                provisioner.EnsureJREInstalledAsync((_, _) => { }, cancellation.Token));

            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hyprism-runtime-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RuntimeDownloadHandler : HttpMessageHandler
    {
        private readonly string _operatingSystem = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? "darwin"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "windows"
                : "linux";
        private readonly string _architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64
            ? "arm64"
            : "amd64";

        public int RequestCount { get; private set; }

        public string ArchiveFileName => _operatingSystem == "windows" ? "jre.zip" : "jre.tar.gz";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;

            if (request.RequestUri?.AbsolutePath.EndsWith("/jre.json", StringComparison.Ordinal) == true)
            {
                var json = $$"""
                    {
                      "download_url": {
                        "{{_operatingSystem}}": {
                          "{{_architecture}}": {
                            "url": "https://runtime.test/archive",
                            "sha256": "00"
                          }
                        }
                      }
                    }
                    """;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("not a runtime archive"u8.ToArray())
            });
        }
    }
}
