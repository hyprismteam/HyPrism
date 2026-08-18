// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Models;
using HyPrism.Core.Application.Ports;

namespace HyPrism.Core.Application.Progress;

/// <summary>
/// Manages progress notifications for downloads and installations.
/// Coordinates with Discord Rich Presence to reflect current activity
/// </summary>
public sealed class ProgressReporter : IProgressReporter
{
    private const int BroadcastIntervalMilliseconds = 100;

    private readonly IDiscordPresence _discord;
    private readonly object _broadcastGate = new();
    private long _lastBroadcastAtMs = long.MinValue;
    private string? _lastBroadcastStage;

    /// <inheritdoc/>
    public event Action<ProgressUpdateMessage>? DownloadProgressChanged;

    /// <inheritdoc/>
    public event Action<string, string, string?>? ErrorOccurred;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgressReporter"/> class
    /// </summary>
    /// <param name="discord">The Discord service for Rich Presence updates</param>
    public ProgressReporter(IDiscordPresence discord)
    {
        _discord = discord;
    }

    /// <inheritdoc/>
    public void SendProgress(string stage, int progress, string messageKey, object[]? args, long downloaded, long total)
    {
        var msg = new ProgressUpdateMessage
        {
            State = stage,
            Progress = progress,
            MessageKey = messageKey,
            Args = args,
            DownloadedBytes = downloaded,
            TotalBytes = total
        };

        DownloadProgressChanged?.Invoke(msg);

        // Don't update Discord during download/install to avoid showing extraction messages
        // Only update on complete or idle
        if (stage == "complete")
        {
            _discord.SetPresence(PresenceState.Idle);
        }
    }

    /// <inheritdoc/>
    public void ReportDownloadProgress(string stage, int progress, string messageKey, object[]? args = null, long downloaded = 0, long total = 0)
    {
        // Download loops report on every buffer read; broadcast at most ~10 updates
        // per second per stage, always letting stage changes and completion through
        if (!ShouldBroadcast(stage, progress))
            return;

        SendProgress(stage, progress, messageKey, args, downloaded, total);
    }

    private bool ShouldBroadcast(string stage, int progress)
    {
        lock (_broadcastGate)
        {
            var nowMs = Environment.TickCount64;
            var stageChanged = !string.Equals(stage, _lastBroadcastStage, StringComparison.Ordinal);
            var isTerminal = progress >= 100;
            var intervalElapsed = _lastBroadcastAtMs == long.MinValue ||
                                  nowMs - _lastBroadcastAtMs >= BroadcastIntervalMilliseconds;

            if (!stageChanged && !isTerminal && !intervalElapsed)
                return false;

            _lastBroadcastStage = stage;
            _lastBroadcastAtMs = nowMs;
            return true;
        }
    }

    /// <summary>
    /// Sends an error notification to subscribed listeners
    /// </summary>
    /// <param name="type">The error category</param>
    /// <param name="message">The user-facing error message</param>
    /// <param name="technical">Optional diagnostic details</param>
    public void SendErrorEvent(string type, string message, string? technical = null)
    {
        ErrorOccurred?.Invoke(type, message, technical);
    }

    /// <inheritdoc/>
    public void ReportError(string type, string message, string? technical = null)
        => SendErrorEvent(type, message, technical);
}
