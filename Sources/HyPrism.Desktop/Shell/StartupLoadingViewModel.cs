// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Desktop.Localization;

namespace HyPrism.Desktop.Shell;

/// <summary>
/// Provides the existing startup screen while Core migrates local data.
/// </summary>
public sealed class StartupLoadingViewModel : IStartupLoadingState
{
    /// <summary>Creates the loading-screen state from the current localization.</summary>
    public StartupLoadingViewModel(StringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        StartupLoadingTitle = localizer["startup.loading.title"];
        StartupLoadingStatus = localizer["startup.loading.content"];
    }

    /// <inheritdoc/>
    public bool IsStartupLoading => true;

    /// <inheritdoc/>
    public string StartupLoadingTitle { get; }

    /// <inheritdoc/>
    public string StartupLoadingStatus { get; }
}
