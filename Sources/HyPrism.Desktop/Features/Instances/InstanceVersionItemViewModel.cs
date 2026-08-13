// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Desktop.Features.Instances;

/// <summary>
/// Represents a version offered by the instance creation surface.
/// </summary>
/// <param name="Version">Numeric Hytale version</param>
/// <param name="IsSelected">Whether this version is selected for the new instance</param>
public sealed record InstanceVersionItemViewModel(int Version, bool IsSelected)
{
    /// <summary>
    /// Gets the display label for the version.
    /// </summary>
    public string Label => $"v{Version}";
}
