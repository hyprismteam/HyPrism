// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Infrastructure;

namespace HyPrism.Core.Tests.Infrastructure;

public sealed class LauncherCachePathsTests
{
    [Fact]
    public void LegacyGamePayloadsAreMovedToGameDownloadsDirectory()
    {
        var appDirectory = Path.Combine(
            Path.GetTempPath(),
            $"hyprism-game-cache-{Guid.NewGuid():N}");

        try
        {
            var cacheDirectory = Path.Combine(appDirectory, "Cache");
            Directory.CreateDirectory(cacheDirectory);
            File.WriteAllBytes(
                Path.Combine(cacheDirectory, "release_version_20.pwr.part"),
                [1, 2, 3]);
            File.WriteAllBytes(
                Path.Combine(cacheDirectory, "pre-release_version_61.pwr"),
                [4, 5, 6]);
            File.WriteAllText(Path.Combine(cacheDirectory, "unrelated.json"), "{}");

            LauncherCachePaths.MigrateLegacyGameDownloads(appDirectory);

            var downloadsDirectory = LauncherCachePaths.GetGameDownloadsDirectory(appDirectory);
            Assert.True(File.Exists(Path.Combine(
                downloadsDirectory,
                "release_version_20.pwr.part")));
            Assert.True(File.Exists(Path.Combine(
                downloadsDirectory,
                "pre-release_version_61.pwr")));
            Assert.False(File.Exists(Path.Combine(
                cacheDirectory,
                "release_version_20.pwr.part")));
            Assert.False(File.Exists(Path.Combine(
                cacheDirectory,
                "pre-release_version_61.pwr")));
            Assert.True(File.Exists(Path.Combine(cacheDirectory, "unrelated.json")));
        }
        finally
        {
            if (Directory.Exists(appDirectory))
                Directory.Delete(appDirectory, recursive: true);
        }
    }
}
