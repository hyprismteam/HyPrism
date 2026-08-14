// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.IO;

namespace HyPrism.Core.Game.Launch;

/// <summary>
/// Manages the game process lifecycle including tracking, monitoring, and termination.
/// Handles detection of running Hytale instances across different platforms
/// </summary>
public class GameProcessTracker : IGameProcessTracker
{
    private readonly object _processLock = new();
    private Process? _gameProcess;

    /// <inheritdoc/>
    public event EventHandler? ProcessExited;

    /// <inheritdoc/>
    public void SetGameProcess(Process? p)
    {
        Process? previousProcess;
        lock (_processLock)
        {
            if (ReferenceEquals(_gameProcess, p))
                return;

            previousProcess = _gameProcess;
            if (previousProcess != null)
                previousProcess.Exited -= OnGameProcessExited;

            _gameProcess = p;
            if (p != null)
            {
                p.Exited += OnGameProcessExited;
                p.EnableRaisingEvents = true;
            }
        }

        previousProcess?.Dispose();
    }

    private void OnGameProcessExited(object? sender, EventArgs e)
    {
        if (sender is not Process exitedProcess)
            return;

        lock (_processLock)
        {
            if (!ReferenceEquals(_gameProcess, exitedProcess))
                return;

            exitedProcess.Exited -= OnGameProcessExited;
            _gameProcess = null;
        }

        exitedProcess.Dispose();
        ProcessExited?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public Process? GetGameProcess()
    {
        lock (_processLock)
            return _gameProcess;
    }

    /// <inheritdoc/>
    public bool IsGameRunning()
    {
        var gameProcess = GetGameProcess();
        if (gameProcess is null)
            return false;

        try
        {
            return !gameProcess.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public bool CheckForRunningGame()
    {
        if (IsGameRunning()) return true;

        return ScanForOrphanedGameProcess();
    }

    private bool ScanForOrphanedGameProcess()
    {
        try
        {
            // Scan for java processes that look like Hytale
            // Common names: "java", "javaw", "HytaleClient", "java.real" (wrapper)
            var potentialProcesses = Process.GetProcessesByName("java")
                .Concat(Process.GetProcessesByName("javaw"))
                .Concat(Process.GetProcessesByName("java.real")) // Wrapper script target
                .Concat(Process.GetProcessesByName("HytaleClient"))
                .ToArray();

            try
            {
                foreach (var p in potentialProcesses)
                {
                    try
                    {
                        // 1. Check Window Title (Works well on Windows, sometime on Linux/macOS)
                        if (!string.IsNullOrEmpty(p.MainWindowTitle) &&
                            p.MainWindowTitle.Contains("Hytale"))
                        {
                            SetGameProcess(p);
                            return true;
                        }

                        // 2. Check Command Line (More reliable on Linux)
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                        {
                            var cmdLine = GetLinuxCommandLine(p.Id);
                            if (!string.IsNullOrEmpty(cmdLine) && cmdLine.Contains("Hytale"))
                            {
                                SetGameProcess(p);
                                return true;
                            }
                        }
                    }
                    catch { /* Ignore access denied / exited process */ }
                }
            }
            finally
            {
                // Dispose all processes that we didn't keep
                foreach (var p in potentialProcesses)
                {
                    if (p != GetGameProcess())
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
            }
        }
        catch { /* Ignore enumeration errors */ }

        return false;
    }

    private string? GetLinuxCommandLine(int pid)
    {
        try
        {
            string path = $"/proc/{pid}/cmdline";
            if (File.Exists(path))
            {
                // cmdline arguments are null-terminated strings
                var text = File.ReadAllText(path);
                return text.Replace("\0", " ");
            }
        }
        catch { }
        return null;
    }

    /// <inheritdoc/>
    public bool ExitGame()
    {
        var gameProcess = GetGameProcess();
        if (gameProcess is null)
            return false;

        try
        {
            if (gameProcess.HasExited)
                return false;

            gameProcess.Kill(entireProcessTree: true);
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
}
