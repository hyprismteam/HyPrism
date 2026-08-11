// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Models;
using HyPrism.Core.Application.Ports;
using HyPrism.Core.Application.Progress;

namespace HyPrism.Core.Tests.Application.Progress;

public class ProgressReporterTests
{
    private readonly Mock<IDiscordPresence> _discordMock = new();
    private readonly ProgressReporter _svc;

    public ProgressReporterTests()
    {
        // Moq auto-handles void calls on the mock (SetPresence, etc.)
        _discordMock.Setup(d => d.SetPresence(
            It.IsAny<PresenceState>(),
            It.IsAny<string>(),
            It.IsAny<int?>()));

        _svc = new ProgressReporter(_discordMock.Object);
    }


    [Fact]
    public void ReportDownloadProgress_FiresEvent()
    {
        ProgressUpdateMessage? received = null;
        _svc.DownloadProgressChanged += msg => received = msg;

        _svc.ReportDownloadProgress("download", 50, "downloading", null, 500, 1000);

        Assert.NotNull(received);
        Assert.Equal("download", received!.State);
        Assert.Equal(50.0, received.Progress);
        Assert.Equal(500L, received.DownloadedBytes);
        Assert.Equal(1000L, received.TotalBytes);
    }

    [Fact]
    public void ReportDownloadProgress_NoSubscribers_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            _svc.ReportDownloadProgress("patch", 100, "done"));
        Assert.Null(ex);
    }

    [Fact]
    public void ReportDownloadProgress_Complete_UpdatesDiscordToIdle()
    {
        _svc.ReportDownloadProgress("complete", 100, "complete");

        _discordMock.Verify(
            d => d.SetPresence(PresenceState.Idle, null, null),
            Times.Once);
    }


    [Fact]
    public void ReportGameStateChanged_Running_FiresEvent()
    {
        string? state = null;
        _svc.GameStateChanged += (s, _) => state = s;

        _svc.ReportGameStateChanged("running");

        Assert.Equal("running", state);
    }

    [Fact]
    public void ReportGameStateChanged_Stopped_PassesExitCode()
    {
        int capturedCode = -1;
        _svc.GameStateChanged += (_, c) => capturedCode = c;

        _svc.ReportGameStateChanged("stopped", 42);

        Assert.Equal(42, capturedCode);
    }

    [Fact]
    public void ReportGameStateChanged_Started_SetsPlayingPresence()
    {
        _svc.ReportGameStateChanged("started");

        _discordMock.Verify(
            d => d.SetPresence(PresenceState.Playing, null, null),
            Times.Once);
    }


    [Fact]
    public void ReportError_FiresEvent()
    {
        string? errorType = null;
        string? errorMsg = null;
        _svc.ErrorOccurred += (t, m, _) => { errorType = t; errorMsg = m; };

        _svc.ReportError("launch", "Game failed to start");

        Assert.Equal("launch", errorType);
        Assert.Equal("Game failed to start", errorMsg);
    }

    [Fact]
    public void ReportError_WithTechnicalDetails_PassesThroughToEvent()
    {
        string? technical = null;
        _svc.ErrorOccurred += (_, _, t) => technical = t;

        _svc.ReportError("download", "Download failed", "Connection timeout");

        Assert.Equal("Connection timeout", technical);
    }
}
