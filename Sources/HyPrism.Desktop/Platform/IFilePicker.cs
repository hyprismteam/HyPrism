// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Desktop.Platform;

/// <summary>
/// Provides native file and folder selection for Desktop view models
/// </summary>
public interface IFilePicker
{
    /// <summary>
    /// Lets the user select one directory
    /// </summary>
    /// <param name="initialPath">Optional initial directory</param>
    /// <returns>The selected local path, or <see langword="null"/> when cancelled</returns>
    Task<string?> BrowseFolderAsync(string? initialPath = null);

    /// <summary>
    /// Lets the user select a Java executable
    /// </summary>
    /// <returns>The selected local path, or <see langword="null"/> when cancelled</returns>
    Task<string?> BrowseJavaExecutableAsync();

    /// <summary>
    /// Lets the user select one or more mod archives
    /// </summary>
    /// <returns>The selected local paths, or an empty array when cancelled</returns>
    Task<string[]> BrowseModFilesAsync();

    /// <summary>
    /// Lets the user choose a destination file
    /// </summary>
    /// <param name="defaultFileName">Suggested file name</param>
    /// <param name="filter">Display name and glob patterns separated with a pipe</param>
    /// <param name="initialPath">Optional initial directory</param>
    /// <returns>The selected local path, or <see langword="null"/> when cancelled</returns>
    Task<string?> SaveFileAsync(string defaultFileName, string filter, string? initialPath = null);

    /// <summary>
    /// Lets the user select a ZIP or PWR instance archive
    /// </summary>
    /// <returns>The selected local path, or <see langword="null"/> when cancelled</returns>
    Task<string?> BrowseInstanceArchiveAsync();
}
