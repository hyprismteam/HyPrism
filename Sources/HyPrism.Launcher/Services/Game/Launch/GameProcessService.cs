using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.IO;

namespace HyPrism.Services.Game.Launch;

/// <summary>
/// Manages the game process lifecycle including tracking, monitoring, and termination.
/// Handles detection of running Hytale instances across different platforms.
/// </summary>
public class GameProcessService : IGameProcessService
{
    private Process? _gameProcess;

    /// <inheritdoc/>
    public event EventHandler? ProcessExited;

    /// <inheritdoc/>
    public void SetGameProcess(Process? p)
    {
        if (_gameProcess != null)
        {
            _gameProcess.Exited -= OnGameProcessExited;
            _gameProcess.Dispose();
        }

        _gameProcess = p;
        
        if (p != null)
        {
            p.EnableRaisingEvents = true;
            p.Exited += OnGameProcessExited;
        }
    }

    private void OnGameProcessExited(object? sender, EventArgs e)
    {
        if (_gameProcess != null)
        {
            _gameProcess.Exited -= OnGameProcessExited;
            _gameProcess.Dispose();
            _gameProcess = null;

            // Notify subscribers that the game process has exited.
            ProcessExited?.Invoke(this, EventArgs.Empty);
        }
    }
    
    /// <inheritdoc/>
    public Process? GetGameProcess() => _gameProcess;

    /// <inheritdoc/>
    public bool IsGameRunning()
    {
        return _gameProcess != null && !_gameProcess.HasExited;
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
                            _gameProcess = p;
                            return true;
                        }

                        // 2. Check Command Line (More reliable on Linux)
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                        {
                            var cmdLine = GetLinuxCommandLine(p.Id);
                            if (!string.IsNullOrEmpty(cmdLine) && cmdLine.Contains("Hytale"))
                            {
                                _gameProcess = p;
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
                    if (p != _gameProcess)
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

    public bool ExitGame()
    {
        var gameProcess = _gameProcess;
        if (gameProcess != null && !gameProcess.HasExited)
        {
            gameProcess.Kill();
            SetGameProcess(null);
            return true;
        }
        return false;
    }
}
