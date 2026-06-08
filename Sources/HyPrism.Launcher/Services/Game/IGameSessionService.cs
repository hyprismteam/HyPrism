using HyPrism.Models;

namespace HyPrism.Services.Game;

/// <summary>
/// Orchestrates the download/update/launch workflow.
/// Coordinates between IPatchManager, IGameLauncher, and other services.
/// </summary>
public interface IGameSessionService : IDisposable
{
    /// <summary>
    /// Downloads/updates the game and optionally launches it upon completion.
    /// </summary>
    /// <param name="launchAfterDownloadProvider">Optional function that returns whether to launch the game after download completes.</param>
    /// <returns>A <see cref="DownloadProgress"/> object for tracking download state and progress.</returns>
    Task<DownloadProgress> DownloadAndLaunchAsync(Func<bool>? launchAfterDownloadProvider = null);

    /// <summary>
    /// Cancels any ongoing download operation.
    /// </summary>
    void CancelDownload();
}
