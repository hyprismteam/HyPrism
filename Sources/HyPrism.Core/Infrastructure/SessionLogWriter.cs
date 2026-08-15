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

    public SessionLogWriter(string filePath)
    {
        FilePath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        _fileLock = FileLocks.GetOrAdd(FilePath, static _ => new object());
    }

    public string FilePath { get; }

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
