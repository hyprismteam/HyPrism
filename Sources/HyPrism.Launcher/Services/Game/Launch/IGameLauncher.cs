namespace HyPrism.Services.Game.Launch;

/// <summary>
/// Handles launching the game process, including client patching, 
/// authentication, and process lifecycle management.
/// </summary>
public interface IGameLauncher
{
    /// <summary>
    /// Launches the game from the specified version directory.
    /// </summary>
    /// <param name="versionPath">The path to the game version directory containing the client.</param>
    /// <param name="branch">The game branch ("release" or "pre-release").</param>
    /// <param name="ct">Token to cancel the launch operation.</param>
    /// <exception cref="InvalidOperationException">Thrown if the game is already running.</exception>
    /// <exception cref="FileNotFoundException">Thrown if the client executable is not found.</exception>
    Task LaunchGameAsync(string versionPath, string branch, CancellationToken ct = default);
}
