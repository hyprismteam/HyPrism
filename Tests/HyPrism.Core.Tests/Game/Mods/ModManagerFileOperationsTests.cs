// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Application.Progress;
using HyPrism.Core.Game.Instances;
using HyPrism.Core.Game.Mods;
using HyPrism.Core.Infrastructure;
using HyPrism.Core.Models;
using Moq;
using System.Net;

namespace HyPrism.Core.Tests.Game.Mods;

public class ModManagerFileOperationsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _instancePath;
    private readonly string _modsPath;
    private readonly ModManager _manager;

    public ModManagerFileOperationsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HyPrismModManagerTests_" + Guid.NewGuid());
        _instancePath = Path.Combine(_tempDir, "instance");
        _modsPath = Path.Combine(_instancePath, "UserData", "Mods");
        Directory.CreateDirectory(_modsPath);

        _manager = new ModManager(
            new HttpClient(),
            _tempDir,
            new JsonConfigStore(_tempDir),
            new Mock<IInstanceRepository>().Object,
            new Mock<IProgressReporter>().Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task SetModEnabledAsync_DisablesAndReEnablesTheModFile()
    {
        await WriteInstalledModAsync("example-mod", "example-mod-1.0.jar");

        Assert.True(await _manager.SetModEnabledAsync(_instancePath, "example-mod", false));
        Assert.True(File.Exists(Path.Combine(_modsPath, "example-mod-1.0.disabled")));
        Assert.False(File.Exists(Path.Combine(_modsPath, "example-mod-1.0.jar")));
        var disabled = Assert.Single(_manager.GetInstanceInstalledMods(_instancePath));
        Assert.False(disabled.Enabled);
        Assert.Equal(".jar", disabled.DisabledOriginalExtension);

        Assert.True(await _manager.SetModEnabledAsync(_instancePath, "example-mod", true));
        Assert.True(File.Exists(Path.Combine(_modsPath, "example-mod-1.0.jar")));
        Assert.False(File.Exists(Path.Combine(_modsPath, "example-mod-1.0.disabled")));
        var enabled = Assert.Single(_manager.GetInstanceInstalledMods(_instancePath));
        Assert.True(enabled.Enabled);
    }

    [Fact]
    public async Task SetModEnabledAsync_ReturnsFalseForUnknownMod()
    {
        await WriteInstalledModAsync("known-mod", "known-mod-1.0.jar");

        Assert.False(await _manager.SetModEnabledAsync(_instancePath, "missing-mod", false));
    }

    [Fact]
    public async Task SearchModsAsync_MapsAuthorAvatarFromCurseForgeResponse()
    {
        const string avatarUrl =
            "https://media.forgecdn.net/avatars/1625/902/639044029153803750.jpeg";
        var configStore = new JsonConfigStore(_tempDir);
        configStore.Configuration.CurseForgeKey = "test-key";
        using var httpClient = new HttpClient(new StaticJsonHandler($$"""
            {
              "data": [
                {
                  "id": 1430352,
                  "name": "BetterMap",
                  "authors": [
                    {
                      "id": 136575006,
                      "name": "Paralaxe",
                      "url": "https://www.curseforge.com/members/paralaxe",
                      "avatarUrl": "{{avatarUrl}}"
                    }
                  ]
                }
              ],
              "pagination": { "totalCount": 1 }
            }
            """));
        var manager = new ModManager(
            httpClient,
            _tempDir,
            configStore,
            new Mock<IInstanceRepository>().Object,
            new Mock<IProgressReporter>().Object);

        var result = await manager.SearchModsAsync("BetterMap", 0, 1, [], 2, 1);

        var mod = Assert.Single(result.Mods);
        Assert.Equal("Paralaxe", mod.Author);
        Assert.Equal(avatarUrl, mod.AuthorAvatarUrl);
    }

    [Fact]
    public async Task RemoveInstalledModAsync_DeletesFileAndManifestEntry()
    {
        await WriteInstalledModAsync("doomed-mod", "doomed-mod-1.0.jar");

        Assert.True(await _manager.RemoveInstalledModAsync(_instancePath, "doomed-mod"));
        Assert.False(File.Exists(Path.Combine(_modsPath, "doomed-mod-1.0.jar")));
        Assert.Empty(_manager.GetInstanceInstalledMods(_instancePath));
    }

    [Fact]
    public async Task RemoveInstalledModAsync_DeletesDisabledFile()
    {
        await WriteInstalledModAsync("disabled-mod", "disabled-mod-1.0.jar");
        Assert.True(await _manager.SetModEnabledAsync(_instancePath, "disabled-mod", false));

        Assert.True(await _manager.RemoveInstalledModAsync(_instancePath, "disabled-mod"));
        Assert.False(File.Exists(Path.Combine(_modsPath, "disabled-mod-1.0.disabled")));
        Assert.Empty(_manager.GetInstanceInstalledMods(_instancePath));
    }

    private async Task WriteInstalledModAsync(string modId, string fileName)
    {
        await File.WriteAllTextAsync(Path.Combine(_modsPath, fileName), "not a real jar");
        await _manager.SaveInstanceModsAsync(_instancePath,
        [
            new InstalledMod
            {
                Id = modId,
                Name = modId,
                FileName = fileName,
                Enabled = true,
                Version = "1.0",
                Author = "Test Author"
            }
        ]);
    }

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json),
                RequestMessage = request
            });
    }
}
