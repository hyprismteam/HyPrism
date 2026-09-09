// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Desktop.Features.Instances;

/// <summary>
/// Represents a version offered by the instance creation surface.
/// </summary>
/// <param name="Version">Numeric Hytale build identifier</param>
/// <param name="IsSelected">Whether this version is selected for the new instance</param>
public sealed record InstanceVersionItemViewModel(int Version, bool IsSelected)
{
    /// <summary>
    /// Creates a version item with a human-readable version name and its numeric build identifier
    /// </summary>
    public InstanceVersionItemViewModel(int version, string? versionName, bool isSelected)
        : this(version, isSelected)
    {
        VersionName = versionName;
    }

    /// <summary>
    /// Gets the human-readable version name returned by the source
    /// </summary>
    public string? VersionName { get; init; }

    /// <summary>
    /// Gets the display label for the version.
    /// </summary>
    public string Label => string.IsNullOrWhiteSpace(VersionName) ? Version.ToString() : VersionName;
}
