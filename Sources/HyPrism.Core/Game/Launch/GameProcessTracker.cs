// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Text.Json;
using HyPrism.Core.Infrastructure;

namespace HyPrism.Core.Game.Launch;

/// <summary>
/// Tracks every game process started by HyPrism and restores still-live entries after restart.
/// Persisted entries are verified by PID and process start time before they are trusted, which
/// prevents stale files and PID reuse from being reported as running games.
/// </summary>
public sealed class GameProcessTracker : IGameProcessTracker, IDisposable
{
    private const string RegistryFileName = "game-processes.json";
    private readonly object _processLock = new();
    private readonly Dictionary<int, TrackedProcess> _processes = [];
    private readonly string? _registryPath;
    private bool _disposed;

    /// <summary>
    /// Creates an in-memory tracker. This overload is intended for tests and hosts without app storage.
    /// </summary>
    public GameProcessTracker()
    {
    }

    /// <summary>
    /// Creates a tracker backed by the launcher runtime registry.
    /// </summary>
    public GameProcessTracker(AppPathConfiguration appPath)
    {
        ArgumentNullException.ThrowIfNull(appPath);
        _registryPath = Path.Combine(appPath.AppDir, "Runtime", RegistryFileName);
        RestoreTrackedProcesses();
    }

    /// <inheritdoc/>
    public event EventHandler? ProcessExited;

    /// <inheritdoc/>
    public event EventHandler<GameProcessStartedEventArgs>? GameProcessStarted;

    /// <inheritdoc/>
    public event EventHandler<GameProcessExitedEventArgs>? GameProcessExited;

    /// <inheritdoc/>
    public void SetGameProcess(Process? process)
    {
        if (process is null)
            return;

        TrackGameProcess(process, "legacy", "legacy");
    }

    /// <inheritdoc/>
    public void TrackGameProcess(
        Process process,
        string instanceId,
        string profileId,
        string? officialAccountId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(process);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var info = new GameProcessInfo(
            process.Id,
            GetProcessStartTimeUtc(process),
            instanceId,
            profileId,
            officialAccountId,
            DateTime.UtcNow);

        lock (_processLock)
        {
            RemoveProcessLocked(process.Id, disposeProcess: false);
            process.EnableRaisingEvents = true;
            process.Exited += OnGameProcessExited;
            _processes.Add(process.Id, new TrackedProcess(process, info));
            SaveRegistryLocked();
        }

        GameProcessStarted?.Invoke(this, new GameProcessStartedEventArgs(info));
    }

    /// <inheritdoc/>
    public Process? GetGameProcess()
    {
        lock (_processLock)
        {
            return _processes.Values
                .OrderByDescending(tracked => tracked.Info.RegisteredAtUtc)
                .Select(tracked => tracked.Process)
                .FirstOrDefault(IsAlive);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<GameProcessInfo> GetRunningProcesses()
    {
        lock (_processLock)
        {
            RemoveExitedProcessesLocked();
            return _processes.Values
                .Select(tracked => tracked.Info)
                .OrderBy(tracked => tracked.RegisteredAtUtc)
                .ToArray();
        }
    }

    /// <inheritdoc/>
    public bool IsInstanceRunning(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        lock (_processLock)
        {
            RemoveExitedProcessesLocked();
            return _processes.Values.Any(tracked =>
                string.Equals(tracked.Info.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <inheritdoc/>
    public bool IsGameRunning()
    {
        lock (_processLock)
        {
            RemoveExitedProcessesLocked();
            return _processes.Count > 0;
        }
    }

    /// <inheritdoc/>
    public bool CheckForRunningGame()
    {
        if (IsGameRunning())
            return true;

        return ScanForOrphanedGameProcess();
    }

    /// <inheritdoc/>
    public bool ExitGame()
    {
        Process? process;
        lock (_processLock)
        {
            process = _processes.Values
                .OrderByDescending(tracked => tracked.Info.RegisteredAtUtc)
                .Select(tracked => tracked.Process)
                .FirstOrDefault(IsAlive);
        }

        return StopProcess(process);
    }

    /// <inheritdoc/>
    public bool ExitGame(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        Process? process;
        lock (_processLock)
        {
            process = _processes.Values
                .Where(tracked => string.Equals(
                    tracked.Info.InstanceId,
                    instanceId,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(tracked => tracked.Info.RegisteredAtUtc)
                .Select(tracked => tracked.Process)
                .FirstOrDefault(IsAlive);
        }

        return StopProcess(process);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_processLock)
        {
            foreach (var tracked in _processes.Values)
            {
                tracked.Process.Exited -= OnGameProcessExited;
                tracked.Process.Dispose();
            }

            _processes.Clear();
            _disposed = true;
        }
    }

    private void RestoreTrackedProcesses()
    {
        if (string.IsNullOrWhiteSpace(_registryPath) || !File.Exists(_registryPath))
            return;

        try
        {
            var records = JsonSerializer.Deserialize<List<GameProcessInfo>>(File.ReadAllText(_registryPath)) ?? [];
            foreach (var record in records)
            {
                var process = TryRestoreProcess(record);
                if (process is null)
                    continue;

                process.EnableRaisingEvents = true;
                process.Exited += OnGameProcessExited;
                _processes[process.Id] = new TrackedProcess(process, record);
            }

            lock (_processLock)
                SaveRegistryLocked();

            if (_processes.Count > 0)
                Logger.Info("Game", $"Restored {_processes.Count} running game process(es) from the launch registry");
        }
        catch (Exception exception)
        {
            Logger.Warning("Game", $"Could not restore the game process registry: {exception.Message}");
        }
    }

    private Process? TryRestoreProcess(GameProcessInfo record)
    {
        try
        {
            var process = Process.GetProcessById(record.ProcessId);
            if (!IsAlive(process) || GetProcessStartTimeUtc(process) != record.ProcessStartedAtUtc)
            {
                process.Dispose();
                return null;
            }

            return process;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private void OnGameProcessExited(object? sender, EventArgs eventArgs)
    {
        if (sender is not Process exitedProcess)
            return;

        GameProcessInfo? info;
        int exitCode;
        lock (_processLock)
        {
            if (!_processes.TryGetValue(exitedProcess.Id, out var tracked)
                || !ReferenceEquals(tracked.Process, exitedProcess))
            {
                return;
            }

            info = tracked.Info;
            exitCode = ReadExitCode(exitedProcess);
            RemoveProcessLocked(exitedProcess.Id, disposeProcess: true);
            SaveRegistryLocked();
        }

        GameProcessExited?.Invoke(this, new GameProcessExitedEventArgs(info, exitCode));
        ProcessExited?.Invoke(this, EventArgs.Empty);
    }

    private static int ReadExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch
        {
            // The exit code is unavailable for a process that is still tearing down.
            return 0;
        }
    }

    private void RemoveExitedProcessesLocked()
    {
        var exited = _processes.Values
            .Where(tracked => !IsAlive(tracked.Process))
            .Select(tracked => tracked.Process.Id)
            .ToArray();
        if (exited.Length == 0)
            return;

        foreach (var processId in exited)
            RemoveProcessLocked(processId, disposeProcess: true);
        SaveRegistryLocked();
    }

    private void RemoveProcessLocked(int processId, bool disposeProcess)
    {
        if (!_processes.Remove(processId, out var tracked))
            return;

        tracked.Process.Exited -= OnGameProcessExited;
        if (disposeProcess)
            tracked.Process.Dispose();
    }

    private void SaveRegistryLocked()
    {
        if (string.IsNullOrWhiteSpace(_registryPath))
            return;

        try
        {
            var directory = Path.GetDirectoryName(_registryPath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _registryPath + ".tmp";
            var content = JsonSerializer.Serialize(
                _processes.Values.Select(tracked => tracked.Info).ToArray(),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temporaryPath, content);
            File.Move(temporaryPath, _registryPath, overwrite: true);
        }
        catch (Exception exception)
        {
            Logger.Warning("Game", $"Could not save the game process registry: {exception.Message}");
        }
    }

    private bool ScanForOrphanedGameProcess()
    {
        try
        {
            var potentialProcesses = Process.GetProcessesByName("java")
                .Concat(Process.GetProcessesByName("javaw"))
                .Concat(Process.GetProcessesByName("java.real"))
                .Concat(Process.GetProcessesByName("HytaleClient"))
                .ToArray();

            try
            {
                foreach (var process in potentialProcesses)
                {
                    try
                    {
                        if ((!string.IsNullOrEmpty(process.MainWindowTitle)
                                && process.MainWindowTitle.Contains("Hytale", StringComparison.OrdinalIgnoreCase))
                            || ProcessCommandLineContainsHytale(process.Id))
                        {
                            TrackGameProcess(process, "external", "external");
                            return true;
                        }
                    }
                    catch
                    {
                        // Process inspection can fail for a process that is exiting or belongs to another user.
                    }
                }
            }
            finally
            {
                foreach (var process in potentialProcesses)
                {
                    lock (_processLock)
                    {
                        if (!_processes.Values.Any(tracked => ReferenceEquals(tracked.Process, process)))
                            process.Dispose();
                    }
                }
            }
        }
        catch
        {
            // Process enumeration is best effort only.
        }

        return false;
    }

    private static bool ProcessCommandLineContainsHytale(int processId)
    {
        if (!OperatingSystem.IsLinux())
            return false;

        try
        {
            var commandLinePath = $"/proc/{processId}/cmdline";
            return File.Exists(commandLinePath)
                && File.ReadAllText(commandLinePath).Contains("Hytale", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static DateTime GetProcessStartTimeUtc(Process process)
        => process.StartTime.ToUniversalTime();

    private static bool IsAlive(Process? process)
    {
        if (process is null)
            return false;

        try
        {
            return !process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool StopProcess(Process? process)
    {
        if (!IsAlive(process))
            return false;

        try
        {
            process!.Kill(entireProcessTree: true);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private sealed record TrackedProcess(Process Process, GameProcessInfo Info);
}
