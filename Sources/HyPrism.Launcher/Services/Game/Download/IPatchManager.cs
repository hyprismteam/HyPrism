namespace HyPrism.Services.Game.Download;

/// <summary>
/// Manages differential game updates by downloading and applying Butler patches.
/// </summary>
public interface IPatchManager
{
    /// <summary>
    /// Applies differential patches from installedVersion to latestVersion.
    /// Downloads and applies patch files sequentially to update the game.
    /// </summary>
    /// <param name="versionPath">The path to the game version directory.</param>
    /// <param name="branch">The game branch ("release" or "pre-release").</param>
    /// <param name="installedVersion">The currently installed version number.</param>
    /// <param name="latestVersion">The target version number to update to.</param>
    /// <param name="ct">Token to cancel the update operation.</param>
    /// <exception cref="OperationCanceledException">Thrown if the operation is cancelled.</exception>
    /// <exception cref="InvalidOperationException">Thrown if a patch file is not found or cannot be applied.</exception>
    Task ApplyDifferentialUpdateAsync(
        string versionPath, 
        string branch,
        int installedVersion, 
        int latestVersion,
        CancellationToken ct = default);
}
