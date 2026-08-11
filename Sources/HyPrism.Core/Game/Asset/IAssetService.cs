// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Services.Game.Asset;

/// <summary>
/// Manages game asset files including Assets.zip extraction and cosmetic item parsing
/// </summary>
public interface IAssetService
{
    /// <summary>
    /// Checks if Assets.zip exists for the specified instance
    /// </summary>
    /// <param name="versionPath">Absolute path to the game version directory</param>
    /// <returns><see langword="true"/> when the asset archive exists</returns>
    bool HasAssetsZip(string versionPath);

    /// <summary>
    /// Gets the path to Assets.zip if it exists
    /// </summary>
    /// <param name="versionPath">Absolute path to the game version directory</param>
    /// <returns>The asset archive path, or <see langword="null"/> when it is absent</returns>
    string? GetAssetsZipPathIfExists(string versionPath);

    /// <summary>
    /// Gets the available cosmetics from the Assets.zip file
    /// </summary>
    /// <param name="versionPath">Absolute path to the game version directory</param>
    /// <returns>Cosmetics grouped by type, or <see langword="null"/> when the archive cannot provide a list</returns>
    Dictionary<string, List<string>>? GetCosmeticsList(string versionPath);

    /// <summary>
    /// Extracts Assets.zip if it exists and hasn't been extracted yet
    /// </summary>
    /// <param name="versionPath">Absolute path to the game version directory</param>
    /// <param name="progressCallback">Callback receiving percentage and user-facing progress text</param>
    /// <returns>A task that completes after extraction or after determining that no extraction is needed</returns>
    /// <exception cref="IOException">Thrown when the archive or destination files cannot be read or written</exception>
    Task ExtractAssetsIfNeededAsync(string versionPath, Action<int, string> progressCallback);
}
