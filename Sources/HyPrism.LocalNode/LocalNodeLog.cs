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
    /// Creates a file logger at an explicit path or below the Local Node data directory
    /// </summary>
    /// <param name="dataDirectory">Fallback directory used when no explicit path is supplied</param>
    /// <param name="filePath">Optional central log file path for the current launcher session</param>
    public LocalNodeLog(string dataDirectory, string? filePath = null)
    {
        FilePath = Path.GetFullPath(filePath ?? Path.Combine(dataDirectory, "local-node.log"));
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
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

            var previousPath = Path.Combine(
                file.DirectoryName!,
                $"{Path.GetFileNameWithoutExtension(file.Name)}.previous{file.Extension}");
            File.Move(FilePath, previousPath, overwrite: true);
        }
        catch
        {
        }
    }
}
