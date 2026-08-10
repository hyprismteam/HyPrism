// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Services.Core.Ipc.Responses;

/// <summary>Information about an available launcher update.</summary>
public record LauncherUpdateInfo(
    string CurrentVersion,
    string LatestVersion,
    string? Changelog = null,
    string? DownloadUrl = null,
    string? AssetName = null,
    string? ReleaseUrl = null,
    bool? IsBeta = null);

/// <summary>Push event: launcher self-update download/install progress.</summary>
public record LauncherUpdateProgress(
    string Stage,
    double Progress,
    string Message,
    long? DownloadedBytes = null,
    long? TotalBytes = null,
    string? DownloadedFilePath = null,
    bool? HasDownloadedFile = null);
