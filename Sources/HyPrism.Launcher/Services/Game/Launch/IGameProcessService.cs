using System.Diagnostics;

namespace HyPrism.Services.Game.Launch;

/// <summary>
/// Manages the game process lifecycle, including tracking, monitoring, and termination.
/// </summary>
public interface IGameProcessService
{
    /// <summary>
    /// Raised when the tracked game process has exited.
    /// </summary>
    event EventHandler? ProcessExited;

    /// <summary>
    /// Sets the current game process reference.
    /// </summary>
    /// <param name="p">The game process, or <c>null</c> to clear the reference.</param>
    void SetGameProcess(Process? p);

    /// <summary>
    /// Gets the current game process reference.
    /// </summary>
    /// <returns>The current game process, or <c>null</c> if no game is tracked.</returns>
    Process? GetGameProcess();

    /// <summary>
    /// Checks if the tracked game process is currently running.
    /// </summary>
    /// <returns><c>true</c> if the game process is running; otherwise, <c>false</c>.</returns>
    bool IsGameRunning();

    /// <summary>
    /// Scans the system for any running Hytale game processes.
    /// </summary>
    /// <returns><c>true</c> if a Hytale process is found running; otherwise, <c>false</c>.</returns>
    bool CheckForRunningGame();

    /// <summary>
    /// Terminates the current game process if it is running.
    /// </summary>
    /// <returns><c>true</c> if the game was successfully terminated; otherwise, <c>false</c>.</returns>
    bool ExitGame();
}
