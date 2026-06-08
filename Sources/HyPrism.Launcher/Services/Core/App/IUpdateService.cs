using System.Text.Json;

namespace HyPrism.Services.Core.App;

/// <summary>
/// Manages launcher and game updates, including version checking, downloading, and installation.
/// Supports both stable (release) and beta update channels via GitHub Releases.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Raised when a new launcher update is available. Payload contains version info.
    /// </summary>
    event Action<object>? LauncherUpdateAvailable;

    /// <summary>
    /// Raised during launcher update download/install to report progress to the UI.
    /// Payload is an anonymous object compatible with the IPC type LauncherUpdateProgress.
    /// </summary>
    event Action<object>? LauncherUpdateProgress;
    
    /// <summary>
    /// Gets the current launcher version string (e.g., "2.0.3").
    /// </summary>
    /// <returns>The current launcher version.</returns>
    string GetLauncherVersion();
    
    /// <summary>
    /// Gets the current launcher update branch ("release" or "beta").
    /// </summary>
    /// <returns>The current update branch.</returns>
    string GetLauncherBranch();
    
    /// <summary>
    /// Checks GitHub for available launcher updates and raises <see cref="LauncherUpdateAvailable"/> if found.
    /// </summary>
    /// <returns>A task representing the asynchronous check operation.</returns>
    Task CheckForLauncherUpdatesAsync();
    
    /// <summary>
    /// Downloads and installs an available update.
    /// </summary>
    /// <param name="args">Optional JSON arguments containing update metadata.</param>
    /// <returns><c>true</c> if the update was successful; otherwise, <c>false</c>.</returns>
    Task<bool> UpdateAsync(JsonElement[]? args);
    
    /// <summary>
    /// Forces a fresh download of the latest game version for the specified branch.
    /// </summary>
    /// <param name="branch">The game branch to update ("release" or "pre-release").</param>
    /// <returns><c>true</c> if the force update was successful; otherwise, <c>false</c>.</returns>
    Task<bool> ForceUpdateLatestAsync(string branch);
    
    /// <summary>
    /// Creates a duplicate of the latest game version without re-downloading.
    /// </summary>
    /// <param name="branch">The game branch to duplicate.</param>
    /// <returns><c>true</c> if duplication was successful; otherwise, <c>false</c>.</returns>
    Task<bool> DuplicateLatestAsync(string branch);
    
    /// <summary>
    /// Gets the current wrapper/launcher status for external tools integration.
    /// </summary>
    /// <returns>A dictionary containing status information.</returns>
    Task<Dictionary<string, object>> WrapperGetStatus();
    
    /// <summary>
    /// Installs the latest game version via wrapper mode.
    /// </summary>
    /// <returns><c>true</c> if installation was successful; otherwise, <c>false</c>.</returns>
    Task<bool> WrapperInstallLatest();
    
    /// <summary>
    /// Launches the game via wrapper mode.
    /// </summary>
    /// <returns><c>true</c> if launch was successful; otherwise, <c>false</c>.</returns>
    Task<bool> WrapperLaunch();
}
