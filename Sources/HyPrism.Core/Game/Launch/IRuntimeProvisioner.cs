// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Core.Game.Launch;

/// <summary>
/// Manages launch prerequisites including Java Runtime Environment and Visual C++ Redistributable
/// </summary>
public interface IRuntimeProvisioner
{
    /// <summary>
    /// Ensures that a compatible Java Runtime Environment is installed
    /// </summary>
    /// <param name="progressCallback">Callback for reporting progress (percentage, status message)</param>
    /// <param name="cancellationToken">Token used to cancel runtime download and installation</param>
    /// <returns>A task that completes when a compatible runtime is available</returns>
    Task EnsureJREInstalledAsync(
        Action<int, string> progressCallback,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the Java feature version number from a Java binary
    /// </summary>
    /// <param name="javaBin">The path to the Java executable</param>
    /// <returns>The Java feature version (e.g., 21 for Java 21)</returns>
    Task<int> GetJavaFeatureVersionAsync(string javaBin);

    /// <summary>
    /// Checks if the specified Java binary supports the Shenandoah garbage collector
    /// </summary>
    /// <param name="javaBin">The path to the Java executable</param>
    /// <returns><c>true</c> if Shenandoah is supported; otherwise, <c>false</c></returns>
    Task<bool> SupportsShenandoahAsync(string javaBin);

    /// <summary>
    /// Gets the path to the Java executable
    /// </summary>
    /// <returns>The absolute path to the Java binary</returns>
    string GetJavaPath();

    /// <summary>
    /// Checks if the Visual C++ Redistributable is installed (Windows only)
    /// </summary>
    /// <returns><c>true</c> if VC++ Redist is installed; otherwise, <c>false</c></returns>
    bool IsVCRedistInstalled();

    /// <summary>
    /// Ensures that the Visual C++ Redistributable is installed (Windows only)
    /// </summary>
    /// <param name="progressCallback">Callback for reporting progress (percentage, status message)</param>
    /// <param name="cancellationToken">Token used to cancel the download and installer wait</param>
    /// <returns>A task that completes when the redistributable is available</returns>
    Task EnsureVCRedistInstalledAsync(
        Action<int, string> progressCallback,
        CancellationToken cancellationToken = default);
}
