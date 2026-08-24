// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Game.Sources;
using HyPrism.Core.Models;
using System.Net;
using System.Text.Json;

namespace HyPrism.Core.Tests.Game.Sources;

public sealed class MirrorCatalogTests
{
    [Fact]
    public void SaveGetAllAndDeleteRoundTripMirrorDefinition()
    {
        var appDir = CreateTemporaryDirectory();
        try
        {
            var catalog = new MirrorCatalog(appDir, new HttpClient());
            var mirror = CreateMirror("community-one", enabled: true);

            catalog.Save(mirror);

            var mirrorPath = Assert.Single(Directory.EnumerateFiles(Path.Combine(appDir, "Mirrors")));
            using var document = JsonDocument.Parse(File.ReadAllText(mirrorPath));
            Assert.Equal("community-one", document.RootElement.GetProperty("Id").GetString());
            Assert.False(document.RootElement.TryGetProperty("id", out _));

            var saved = Assert.Single(catalog.GetAll());
            Assert.Equal("community-one", saved.Id);
            Assert.Equal("Community One", saved.Name);
            Assert.True(saved.Enabled);
            Assert.Single(catalog.CreateEnabledSources());

            Assert.True(catalog.Delete(mirror.Id));
            Assert.Empty(catalog.GetAll());
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    [Fact]
    public void CreateEnabledSourcesExcludesDisabledDefinitions()
    {
        var appDir = CreateTemporaryDirectory();
        try
        {
            var catalog = new MirrorCatalog(appDir, new HttpClient());
            catalog.Save(CreateMirror("enabled-source", enabled: true));
            catalog.Save(CreateMirror("disabled-source", enabled: false));

            var source = Assert.Single(catalog.CreateEnabledSources());

            Assert.Equal("enabled-source", source.SourceId);
            Assert.Equal(2, catalog.GetAll().Count);
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("source/name")]
    [InlineData("UPPERCASE")]
    public void SaveRejectsUnsafeMirrorIdentifiers(string mirrorId)
    {
        var appDir = CreateTemporaryDirectory();
        try
        {
            var catalog = new MirrorCatalog(appDir, new HttpClient());

            Assert.Throws<ArgumentException>(() => catalog.Save(CreateMirror(mirrorId, enabled: true)));
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    [Fact]
    public async Task AvailabilityProbeUsesHeadersOnlyAndDoesNotDownloadGameData()
    {
        var appDir = CreateTemporaryDirectory();
        try
        {
            var handler = new ProbeHandler();
            var catalog = new MirrorCatalog(appDir, new HttpClient(handler));
            catalog.Save(CreateMirror("probe-source", enabled: true));
            var source = Assert.Single(catalog.CreateEnabledSources());

            var result = await source.ProbeAvailabilityAsync();

            Assert.True(result.IsAvailable);
            Assert.True(result.PingMs >= 0);
            Assert.Equal(1, handler.RequestCount);
            Assert.Equal(HttpMethod.Head, handler.LastMethod);
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    private static MirrorMeta CreateMirror(string id, bool enabled)
        => new()
        {
            Id = id,
            Name = "Community One",
            Enabled = enabled,
            SourceType = "pattern",
            Pattern = new MirrorPatternConfig
            {
                BaseUrl = "https://mirror.example.com",
                VersionDiscovery = new VersionDiscoveryConfig
                {
                    Method = "static-list",
                    StaticVersions = [1]
                }
            }
        };

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hyprism-mirror-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class ProbeHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public HttpMethod? LastMethod { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastMethod = request.Method;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request
            });
        }
    }
}
