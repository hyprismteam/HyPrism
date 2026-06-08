using HyPrism.Models;

namespace HyPrism.Services.Core.App;

/// <summary>
/// Provides a centralized notification service for reporting download progress, game state changes, and errors.
/// Acts as an event hub between backend services and UI ViewModels.
/// </summary>
public interface IProgressNotificationService
{
    /// <summary>
    /// Raised when download/update progress changes. Provides detailed progress information.
    /// </summary>
    event Action<ProgressUpdateMessage>? DownloadProgressChanged;
    
    /// <summary>
    /// Raised when the game state changes (e.g., launching, running, exited).
    /// </summary>
    event Action<string, int>? GameStateChanged;
    
    /// <summary>
    /// Raised when an error occurs during game operations.
    /// </summary>
    event Action<string, string, string?>? ErrorOccurred;
    
    /// <summary>
    /// Reports download or update progress to subscribed listeners.
    /// </summary>
    /// <param name="stage">The current operation stage identifier (e.g., "download", "extract", "update").</param>
    /// <param name="progress">The progress percentage (0-100).</param>
    /// <param name="messageKey">The localization key for the status message.</param>
    /// <param name="args">Optional format arguments for the message.</param>
    /// <param name="downloaded">The number of bytes downloaded so far.</param>
    /// <param name="total">The total number of bytes to download.</param>
    void ReportDownloadProgress(string stage, int progress, string messageKey, object[]? args = null, long downloaded = 0, long total = 0);
    
    /// <summary>
    /// Reports a game state change to subscribed listeners.
    /// </summary>
    /// <param name="state">The new game state (e.g., "launching", "running", "exited").</param>
    /// <param name="exitCode">Optional process exit code when state is "exited".</param>
    void ReportGameStateChanged(string state, int? exitCode = null);
    
    /// <summary>
    /// Reports an error that occurred during game operations.
    /// </summary>
    /// <param name="type">The error type category (e.g., "download", "launch", "patch").</param>
    /// <param name="message">The user-friendly error message.</param>
    /// <param name="technical">Optional technical details for debugging purposes.</param>
    void ReportError(string type, string message, string? technical = null);
}
