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

    /// <summary>Creates paths for a new log session under the configured application directory</summary>
    /// <param name="appPath">Application path configuration</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="appPath"/> is <see langword="null"/></exception>
    public LogSessionPaths(AppPathConfiguration appPath)
        : this(appPath.AppDir, DateTimeOffset.Now)
    {
    }

    /// <summary>Creates paths for a log session with the supplied start time</summary>
    /// <param name="appDirectory">Application data directory</param>
    /// <param name="startedAt">Timestamp used to name the session directory</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="appDirectory"/> is not a valid path</exception>
    public LogSessionPaths(string appDirectory, DateTimeOffset startedAt)
    {
        StartedAt = startedAt;
        RootDirectory = Path.Combine(Path.GetFullPath(appDirectory), "Logs");
        SessionDirectory = Path.Combine(
            RootDirectory,
            startedAt.ToString("yyyy-MM-dd_HH-mm-ss.fff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(SessionDirectory);
    }

    /// <summary>Timestamp assigned to this log session</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>Root directory containing all launcher log sessions</summary>
    public string RootDirectory { get; }

    /// <summary>Directory containing files for this log session</summary>
    public string SessionDirectory { get; }

    /// <summary>Path to the launcher log file for this session</summary>
    public string LauncherLogPath => Path.Combine(SessionDirectory, "launcher.log");

    /// <summary>Gets the path to an instance log file</summary>
    /// <param name="instanceId">Instance identifier used in the file name</param>
    /// <returns>A sanitized instance log path inside the session directory</returns>
    public string GetInstanceLogPath(string instanceId)
        => Path.Combine(SessionDirectory, $"instance-{SanitizeIdentifier(instanceId)}.log");

    /// <summary>Gets the path to a Local Node log file</summary>
    /// <param name="port">Local Node port used in the file name</param>
    /// <returns>A Local Node log path inside the session directory</returns>
    public string GetLocalNodeLogPath(int port)
        => Path.Combine(SessionDirectory, $"local-node-{port}.log");

    /// <summary>Gets the path to a Local Node request journal</summary>
    /// <param name="port">Local Node port used in the file name</param>
    /// <returns>A request journal path inside the session directory</returns>
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
