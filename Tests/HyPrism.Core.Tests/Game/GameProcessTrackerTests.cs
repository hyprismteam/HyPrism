// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using HyPrism.Core;
using HyPrism.Core.Game.Launch;

namespace HyPrism.Core.Tests.Game;

public sealed class GameProcessTrackerTests
{
    [Fact]
    public async Task PersistentRegistry_RestoresLiveProcessAndRemovesItAfterExit()
    {
        var appDirectory = Path.Combine(Path.GetTempPath(), "HyPrismProcessTrackerTests_" + Guid.NewGuid());
        var process = StartLongRunningProcess();
        var processId = process.Id;
        try
        {
            using (var tracker = new GameProcessTracker(new AppPathConfiguration(appDirectory)))
            {
                tracker.TrackGameProcess(
                    process,
                    "release-instance",
                    "profile-id",
                    "official-account-owner");
                Assert.True(tracker.IsInstanceRunning("release-instance"));
            }

            using (var restoredTracker = new GameProcessTracker(new AppPathConfiguration(appDirectory)))
            {
                var restored = Assert.Single(restoredTracker.GetRunningProcesses());
                Assert.Equal(processId, restored.ProcessId);
                Assert.Equal("release-instance", restored.InstanceId);
                Assert.Equal("official-account-owner", restored.OfficialAccountId);
                Assert.True(restoredTracker.IsInstanceRunning("release-instance"));
                Assert.True(restoredTracker.ExitGame("release-instance"));
            }

            await WaitForProcessExitAsync(processId);

            using var cleanedTracker = new GameProcessTracker(new AppPathConfiguration(appDirectory));
            Assert.Empty(cleanedTracker.GetRunningProcesses());
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
            if (Directory.Exists(appDirectory))
                Directory.Delete(appDirectory, recursive: true);
        }
    }

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

    [Fact]
    public void TrackGameProcessRaisesGameProcessStartedWithProcessInfo()
    {
        var process = StartLongRunningProcess();
        var tracker = new GameProcessTracker();
        GameProcessStartedEventArgs? started = null;
        tracker.GameProcessStarted += (_, args) => started = args;

        try
        {
            tracker.TrackGameProcess(
                process,
                "release-instance",
                "profile-id",
                "official-account-owner");

            Assert.NotNull(started);
            Assert.Equal(process.Id, started!.Process.ProcessId);
            Assert.Equal("release-instance", started.Process.InstanceId);
            Assert.Equal("profile-id", started.Process.ProfileId);
            Assert.Equal("official-account-owner", started.Process.OfficialAccountId);
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

    [Fact]
    public async Task GameProcessExitedCarriesProcessExitCode()
    {
        var process = StartProcessExitingWithCode(7);
        var tracker = new GameProcessTracker();
        var exited = new TaskCompletionSource<GameProcessExitedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.GameProcessExited += (_, args) => exited.TrySetResult(args);

        try
        {
            tracker.TrackGameProcess(process, "release-instance", "profile-id");

            var args = await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(7, args.ExitCode);
            Assert.Equal("release-instance", args.Process.InstanceId);
        }
        finally
        {
            process.Dispose();
        }
    }

    private static Process StartProcessExitingWithCode(int exitCode)
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
            startInfo.ArgumentList.Add($"ping -n 2 127.0.0.1 > nul & exit {exitCode}");
        }
        else
        {
            startInfo.FileName = "/bin/sh";
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add($"sleep 1; exit {exitCode}");
        }

        return Process.Start(startInfo)
               ?? throw new InvalidOperationException("Could not start the test process");
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

    private static async Task WaitForProcessExitAsync(int processId)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < timeout)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                    return;
            }
            catch (ArgumentException)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("The test game process did not exit");
    }
}
