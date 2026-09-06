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
    public void ReportError_FiresEvent()
    {
        OperationErrorMessage? error = null;
        _svc.OperationErrorOccurred += update => error = update;

        _svc.ReportError("launch", "Game failed to start");

        Assert.Equal("launch", error?.Type);
        Assert.Equal("Game failed to start", error?.Message);
    }

    [Fact]
    public void ReportError_WithTechnicalDetails_PassesThroughToEvent()
    {
        OperationErrorMessage? error = null;
        _svc.OperationErrorOccurred += update => error = update;

        _svc.ReportError("download", "Download failed", "Connection timeout");

        Assert.Equal("Connection timeout", error?.Technical);
    }

    [Fact]
    public void OperationScope_AssignsInstanceToProgressAndErrors()
    {
        ProgressUpdateMessage? progress = null;
        OperationErrorMessage? error = null;
        _svc.DownloadProgressChanged += update => progress = update;
        _svc.OperationErrorOccurred += update => error = update;

        using (_svc.BeginOperation("release-instance"))
        {
            _svc.ReportDownloadProgress("download", 10, "downloading");
            _svc.ReportError("download", "Download failed");
        }

        Assert.Equal("release-instance", progress?.InstanceId);
        Assert.Equal("release-instance", error?.InstanceId);
    }

    [Fact]
    public void ProgressScopes_DoNotThrottleSeparateInstancesTogether()
    {
        var updates = new List<ProgressUpdateMessage>();
        _svc.DownloadProgressChanged += updates.Add;

        using (_svc.BeginOperation("release-instance"))
            _svc.ReportDownloadProgress("download", 10, "downloading");
        using (_svc.BeginOperation("pre-release-instance"))
            _svc.ReportDownloadProgress("download", 10, "downloading");

        Assert.Equal(["release-instance", "pre-release-instance"],
            updates.Select(update => update.InstanceId));
    }

    [Fact]
    public void ReportDownloadProgress_SameStageWithinInterval_IsThrottled()
    {
        var received = new List<ProgressUpdateMessage>();
        _svc.DownloadProgressChanged += received.Add;

        _svc.ReportDownloadProgress("download", 10, "downloading");
        _svc.ReportDownloadProgress("download", 11, "downloading");
        _svc.ReportDownloadProgress("download", 12, "downloading");

        var update = Assert.Single(received);
        Assert.Equal(10, update.Progress);
    }

    [Fact]
    public void ReportDownloadProgress_StageChange_BypassesThrottle()
    {
        var received = new List<ProgressUpdateMessage>();
        _svc.DownloadProgressChanged += received.Add;

        _svc.ReportDownloadProgress("download", 65, "downloading");
        _svc.ReportDownloadProgress("install", 5, "installing");

        Assert.Equal(2, received.Count);
        Assert.Equal("install", received[1].State);
    }

    [Fact]
    public void ReportDownloadProgress_Completion_BypassesThrottle()
    {
        var received = new List<ProgressUpdateMessage>();
        _svc.DownloadProgressChanged += received.Add;

        _svc.ReportDownloadProgress("download", 99, "downloading");
        _svc.ReportDownloadProgress("download", 100, "downloading");

        Assert.Equal(2, received.Count);
        Assert.Equal(100, received[1].Progress);
    }

    [Fact]
    public async Task ReportDownloadProgress_AfterInterval_BroadcastsAgain()
    {
        var received = new List<ProgressUpdateMessage>();
        _svc.DownloadProgressChanged += received.Add;

        _svc.ReportDownloadProgress("download", 10, "downloading");
        await Task.Delay(150);
        _svc.ReportDownloadProgress("download", 20, "downloading");

        Assert.Equal(2, received.Count);
        Assert.Equal(20, received[1].Progress);
    }
}
