// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Models;

namespace HyPrism.Core.Application.Progress;

/// <summary>
/// Provides a centralized notification service for reporting download progress and errors.
/// Acts as an event hub between backend services and UI ViewModels. Game process lifecycle
/// is announced by <c>IGameProcessTracker</c> and <c>IGameLaunchCoordinator</c> instead
/// </summary>
public interface IProgressReporter
{
    /// <summary>
    /// Raised when download/update progress changes. Provides detailed progress information
    /// </summary>
    event Action<ProgressUpdateMessage>? DownloadProgressChanged;

    /// <summary>
    /// Raised when an error occurs during game operations
    /// </summary>
    event Action<string, string, string?>? ErrorOccurred;

    /// <summary>
    /// Reports download or update progress to subscribed listeners. Updates within
    /// the same stage are throttled; stage changes and 100% completion always pass through
    /// </summary>
    /// <param name="stage">The current operation stage identifier (e.g., "download", "extract", "update")</param>
    /// <param name="progress">The progress percentage (0-100)</param>
    /// <param name="messageKey">The localization key for the status message</param>
    /// <param name="args">Optional format arguments for the message</param>
    /// <param name="downloaded">The number of bytes downloaded so far</param>
    /// <param name="total">The total number of bytes expected for the download</param>
    void ReportDownloadProgress(string stage, int progress, string messageKey, object[]? args = null, long downloaded = 0, long total = 0);

    /// <summary>
    /// Reports an error that occurred during game operations
    /// </summary>
    /// <param name="type">The error type category (e.g., "download", "launch", "patch")</param>
    /// <param name="message">The user-friendly error message</param>
    /// <param name="technical">Optional technical details for debugging purposes</param>
    void ReportError(string type, string message, string? technical = null);
}
