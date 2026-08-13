// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.LocalNode;

/// <summary>
/// Writes Local Node diagnostics without sharing the launcher logging pipeline
/// </summary>
public sealed class LocalNodeLog
{
    private const long MaximumLogSize = 5 * 1024 * 1024;
    private readonly object _gate = new();

    /// <summary>
    /// Creates a file logger below the Local Node data directory
    /// </summary>
    public LocalNodeLog(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        FilePath = Path.Combine(dataDirectory, "local-node.log");
        RotateIfNeeded();
    }

    /// <summary>
    /// Gets the log file used by this Local Node process
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Writes an informational event
    /// </summary>
    public void Info(string message) => Write("INF", message);

    /// <summary>
    /// Writes a warning event
    /// </summary>
    public void Warning(string message) => Write("WRN", message);

    /// <summary>
    /// Writes an error event
    /// </summary>
    public void Error(string message) => Write("ERR", message);

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:O} {level} {message}{Environment.NewLine}";
        lock (_gate)
        {
            try
            {
                File.AppendAllText(FilePath, line);
            }
            catch
            {
            }
        }
    }

    private void RotateIfNeeded()
    {
        try
        {
            var file = new FileInfo(FilePath);
            if (!file.Exists || file.Length <= MaximumLogSize)
                return;

            File.Move(FilePath, Path.Combine(file.DirectoryName!, "local-node.previous.log"), overwrite: true);
        }
        catch
        {
        }
    }
}
