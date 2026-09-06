// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text;

namespace HyPrism.Core.Infrastructure;

/// <summary>
/// Resolves all diagnostic files produced during one HyPrism process lifetime.
/// </summary>
public sealed class LogSessionPaths
{
    private const int MaximumIdentifierLength = 64;

    public LogSessionPaths(AppPathConfiguration appPath)
        : this(appPath.AppDir, DateTimeOffset.Now)
    {
    }

    public LogSessionPaths(string appDirectory, DateTimeOffset startedAt)
    {
        StartedAt = startedAt;
        RootDirectory = Path.Combine(Path.GetFullPath(appDirectory), "Logs");
        SessionDirectory = Path.Combine(
            RootDirectory,
            startedAt.ToString("yyyy-MM-dd_HH-mm-ss.fff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(SessionDirectory);
    }

    public DateTimeOffset StartedAt { get; }

    public string RootDirectory { get; }

    public string SessionDirectory { get; }

    public string LauncherLogPath => Path.Combine(SessionDirectory, "launcher.log");

    public string GetInstanceLogPath(string instanceId)
        => Path.Combine(SessionDirectory, $"instance-{SanitizeIdentifier(instanceId)}.log");

    public string GetLocalNodeLogPath(int port)
        => Path.Combine(SessionDirectory, $"local-node-{port}.log");

    public string GetLocalNodeRequestJournalPath(int port)
        => Path.Combine(SessionDirectory, $"local-node-requests-{port}.ndjson");

    private static string SanitizeIdentifier(string identifier)
    {
        var builder = new StringBuilder(Math.Min(identifier.Length, MaximumIdentifierLength));
        foreach (var character in identifier)
        {
            if (builder.Length == MaximumIdentifierLength)
                break;

            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'
                ? character
                : '_');
        }

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }
}
