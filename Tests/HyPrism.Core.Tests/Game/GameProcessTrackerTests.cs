// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using HyPrism.Core.Game.Launch;

namespace HyPrism.Core.Tests.Game;

public sealed class GameProcessTrackerTests
{
    [Fact]
    public async Task ExitGameRaisesProcessExitedAndClearsTrackedProcess()
    {
        var process = StartLongRunningProcess();
        var tracker = new GameProcessTracker();
        var processExited = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.ProcessExited += (_, _) => processExited.TrySetResult();

        try
        {
            tracker.SetGameProcess(process);

            Assert.True(tracker.IsGameRunning());
            Assert.True(tracker.ExitGame());
            await processExited.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(tracker.IsGameRunning());
            Assert.Null(tracker.GetGameProcess());
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            process.Dispose();
        }
    }

    private static Process StartLongRunningProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (OperatingSystem.IsWindows())
        {
            startInfo.FileName = "cmd.exe";
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("ping -n 30 127.0.0.1 > nul");
        }
        else
        {
            startInfo.FileName = "/bin/sh";
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("sleep 30");
        }

        return Process.Start(startInfo)
               ?? throw new InvalidOperationException("Could not start the test process");
    }
}
