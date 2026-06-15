namespace HyPrism.Services.Game.Butler;

/// <summary>
/// Provides functionality for managing the Butler patching tool,
/// including installation and applying differential patches.
/// </summary>
public interface IButlerService
{
    /// <summary>
    /// Gets the filesystem path to the Butler executable.
    /// </summary>
    /// <returns>The absolute path to the Butler binary.</returns>
    string GetButlerPath();

    /// <summary>
    /// Checks whether Butler is installed on the system.
    /// </summary>
    /// <returns><c>true</c> if Butler is installed and accessible; otherwise, <c>false</c>.</returns>
    bool IsButlerInstalled();

    /// <summary>
    /// Ensures Butler is installed, downloading it if necessary.
    /// </summary>
    /// <param name="progressCallback">Optional callback for reporting progress (percentage, status message).</param>
    /// <returns>The path to the installed Butler executable.</returns>
    Task<string> EnsureButlerInstalledAsync(Action<int, string>? progressCallback = null);

    /// <summary>
    /// Applies a PWR (patch) file to a target directory using Butler.
    /// </summary>
    /// <param name="pwrFile">The path to the PWR patch file.</param>
    /// <param name="targetDir">The target directory to apply the patch to.</param>
    /// <param name="progressCallback">Optional callback for reporting progress (percentage, status message).</param>
    /// <param name="externalCancellationToken">Token to cancel the operation.</param>
    Task ApplyPwrAsync(string pwrFile, string targetDir, Action<int, string>? progressCallback = null, CancellationToken externalCancellationToken = default);
}
