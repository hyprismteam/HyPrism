// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Text;
using HyPrism.Core.Game.Sources;
using HyPrism.Core.Models;

namespace HyPrism.Core.Tests.Game.Sources;

public sealed class JsonMirrorSourceTests
{
    [Fact]
    public async Task JsonApiDiscoveryKeepsVersionNameSeparateFromBuild()
    {
        var mirror = new MirrorMeta
        {
            Id = "test-mirror",
            Name = "Test mirror",
            Pattern = new MirrorPatternConfig
            {
                BaseUrl = "https://mirror.example.test",
                FullBuildUrl = "{base}/builds/{version}.pwr",
                VersionDiscovery = new VersionDiscoveryConfig
                {
                    Method = "json-api",
                    Url = "{base}/versions",
                    JsonPath = "items[].version"
                }
            }
        };

        using var client = new HttpClient(new StubHandler(
            "{\"items\":[{\"version\":\"2026.09.08-e1d69dd\",\"build\":42},{\"version\":\"2026.09.01-abc1234\",\"build\":41}]}"));
        var source = new JsonMirrorSource(mirror, client);

        var entries = await source.GetVersionsAsync("linux", "x64", "release");

        Assert.Collection(
            entries,
            first =>
            {
                Assert.Equal(42, first.Version);
                Assert.Equal("2026.09.08-e1d69dd", first.VersionName);
                Assert.Equal("https://mirror.example.test/builds/42.pwr", first.PwrUrl);
            },
            second =>
            {
                Assert.Equal(41, second.Version);
                Assert.Equal("2026.09.01-abc1234", second.VersionName);
            });
    }

    [Fact]
    public async Task JsonApiDiscoverySupportsExplicitBuildPath()
    {
        var mirror = new MirrorMeta
        {
            Id = "test-mirror-explicit-build",
            Pattern = new MirrorPatternConfig
            {
                BaseUrl = "https://mirror.example.test",
                VersionDiscovery = new VersionDiscoveryConfig
                {
                    Method = "json-api",
                    Url = "{base}/versions",
                    JsonPath = "versions[].name",
                    BuildJsonPath = "versions[].buildNumber"
                }
            }
        };

        using var client = new HttpClient(new StubHandler(
            "{\"versions\":[{\"name\":\"2026.08.30-abcdef0\",\"buildNumber\":39}]}"));
        var source = new JsonMirrorSource(mirror, client);

        var entry = Assert.Single(await source.GetVersionsAsync("linux", "x64", "release"));

        Assert.Equal(39, entry.Version);
        Assert.Equal("2026.08.30-abcdef0", entry.VersionName);
    }

    [Fact]
    public async Task ManifestDiscoveryReadsNamesFromVersionsAndFileMetadata()
    {
        var mirror = new MirrorMeta
        {
            Id = "test-manifest-mirror",
            Pattern = new MirrorPatternConfig
            {
                BaseUrl = "https://mirror.example.test/patches",
                FullBuildUrl = "{base}/{os}/{arch}/{branch}/0_to_{version}.pwr",
                VersionDiscovery = new VersionDiscoveryConfig
                {
                    Method = "manifest",
                    Url = "{base}/manifest.json"
                }
            }
        };

        using var client = new HttpClient(new StubHandler(
            """
            {
              "versions": {
                "release": {
                  "27": { "version": "0.6.4" }
                }
              },
              "files": {
                "linux/x64/release/0_to_27.pwr": { "gameVersion": "0.6.4" },
                "linux/x64/release/0_to_26.pwr": { "gameVersion": "0.6.0" }
              }
            }
            """));
        var source = new JsonMirrorSource(mirror, client);

        var entries = await source.GetVersionsAsync("linux", "x64", "release");

        Assert.Collection(
            entries,
            latest =>
            {
                Assert.Equal(27, latest.Version);
                Assert.Equal("0.6.4", latest.VersionName);
            },
            previous =>
            {
                Assert.Equal(26, previous.Version);
                Assert.Equal("0.6.0", previous.VersionName);
            });
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }
}
