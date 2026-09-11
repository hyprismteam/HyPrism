// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Concurrent;

namespace HyPrism.Core.Infrastructure;

/// <summary>
/// Appends timestamped records to one file in the current log session.
/// </summary>
public sealed class SessionLogWriter
{
    private static readonly ConcurrentDictionary<string, object> FileLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _fileLock;

    /// <summary>Creates a writer for a session log file</summary>
    /// <param name="filePath">Path to the log file</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filePath"/> is not a valid path</exception>
    public SessionLogWriter(string filePath)
    {
        FilePath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        _fileLock = FileLocks.GetOrAdd(FilePath, static _ => new object());
    }

    /// <summary>Absolute path to the file receiving log records</summary>
    public string FilePath { get; }

    /// <summary>Appends a timestamped log record to the file</summary>
    /// <param name="level">Log severity label</param>
    /// <param name="source">Component that produced the record</param>
    /// <param name="message">Message text</param>
    public void Write(string level, string source, string message)
    {
        var line = $"{DateTimeOffset.Now:O} {level} {source}: {message}{Environment.NewLine}";
        lock (_fileLock)
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
}
