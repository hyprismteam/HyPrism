// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Migrations;

namespace HyPrism.Core.Infrastructure;

/// <summary>
/// Provides the canonical launcher cache locations and migrates legacy entries
/// </summary>
public static class LauncherCachePaths
{
    /// <summary>
    /// Gets the directory used for resumable game payload downloads
    /// </summary>
    public static string GetGameDownloadsDirectory(string appDirectory)
        => Path.Combine(appDirectory, "Cache", "Game", "Downloads");

    /// <summary>
    /// Compatibility entry point for callers compiled against the former cache migration helper.
    /// </summary>
    [Obsolete("Use GameDownloadCacheMigration.Migrate")]
    public static void MigrateLegacyGameDownloads(string appDirectory)
        => GameDownloadCacheMigration.Migrate(appDirectory);
}
