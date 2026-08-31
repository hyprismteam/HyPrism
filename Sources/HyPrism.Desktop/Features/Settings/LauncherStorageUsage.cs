// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Infrastructure;

namespace HyPrism.Desktop.Features.Settings;

/// <summary>
/// Describes disk space used by launcher and instance files
/// </summary>
public sealed record LauncherStorageUsage(
    long SystemFilesBytes,
    long ImageBytes,
    long ModBytes,
    long NewsBytes,
    long LogBytes,
    long OtherBytes)
{
    public long TotalBytes => SystemFilesBytes + ImageBytes + ModBytes + NewsBytes + LogBytes + OtherBytes;
}

internal static class LauncherStorageUsageAnalyzer
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avif", ".bmp", ".gif", ".ico", ".jpeg", ".jpg", ".png", ".svg", ".webp"
    };

    private static readonly HashSet<string> SystemExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bin", ".cfg", ".dat", ".db", ".dll", ".dylib", ".exe", ".json", ".so", ".toml", ".xml", ".yaml", ".yml"
    };

    public static Task<LauncherStorageUsage> MeasureAsync(
        string launcherDirectory,
        string instanceDirectory,
        CancellationToken cancellationToken)
        => Task.Run(
            () => Measure(launcherDirectory, instanceDirectory, cancellationToken),
            cancellationToken);

    private static LauncherStorageUsage Measure(
        string launcherDirectory,
        string instanceDirectory,
        CancellationToken cancellationToken)
    {
        var totals = new long[6];
        var roots = GetDistinctRoots(launcherDirectory, instanceDirectory);
        foreach (var root in roots)
            MeasureRoot(root, totals, cancellationToken);

        return new LauncherStorageUsage(
            totals[0],
            totals[1],
            totals[2],
            totals[3],
            totals[4],
            totals[5]);
    }

    private static IReadOnlyList<string> GetDistinctRoots(string launcherDirectory, string instanceDirectory)
    {
        var launcherRoot = Path.GetFullPath(launcherDirectory);
        var instanceRoot = Path.GetFullPath(instanceDirectory);
        if (DirectoriesEqual(launcherRoot, instanceRoot) || IsNestedDirectory(launcherRoot, instanceRoot))
            return [launcherRoot];
        if (IsNestedDirectory(instanceRoot, launcherRoot))
            return [instanceRoot];
        return [launcherRoot, instanceRoot];
    }

    private static void MeasureRoot(string root, long[] totals, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
            return;

        var directories = new Stack<string>();
        directories.Push(root);
        while (directories.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var file in EnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                totals[(int)Classify(file)] += GetFileLength(file);
            }

            foreach (var child in EnumerateDirectories(directory))
            {
                if (!IsReparsePoint(child))
                    directories.Push(child);
            }
        }
    }

    private static IReadOnlyList<string> EnumerateFiles(string directory)
    {
        try
        {
            return Directory.GetFiles(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.Debug("Settings", $"Unable to inspect storage files in {directory}: {exception.Message}");
            return [];
        }
    }

    private static IReadOnlyList<string> EnumerateDirectories(string directory)
    {
        try
        {
            return Directory.GetDirectories(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.Debug("Settings", $"Unable to inspect storage directories in {directory}: {exception.Message}");
            return [];
        }
    }

    private static long GetFileLength(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.Debug("Settings", $"Unable to measure storage file {file}: {exception.Message}");
            return 0;
        }
    }

    private static StorageFileCategory Classify(string file)
    {
        var segments = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(segment => segment.Equals("Mods", StringComparison.OrdinalIgnoreCase)))
            return StorageFileCategory.Mods;
        if (ImageExtensions.Contains(Path.GetExtension(file)) || ContainsPath(segments, "Cache", "Images"))
            return StorageFileCategory.Images;
        if (ContainsPath(segments, "Cache", "News"))
            return StorageFileCategory.News;
        if (segments.Any(segment => segment.Equals("Logs", StringComparison.OrdinalIgnoreCase)) ||
            Path.GetExtension(file).Equals(".log", StringComparison.OrdinalIgnoreCase))
        {
            return StorageFileCategory.Logs;
        }
        if (segments.Any(segment => segment.Equals("Runtime", StringComparison.OrdinalIgnoreCase)) ||
            SystemExtensions.Contains(Path.GetExtension(file)))
        {
            return StorageFileCategory.SystemFiles;
        }
        return StorageFileCategory.Other;
    }

    private static bool ContainsPath(IReadOnlyList<string> segments, string parent, string child)
    {
        for (var index = 0; index < segments.Count - 1; index++)
        {
            if (segments[index].Equals(parent, StringComparison.OrdinalIgnoreCase) &&
                segments[index + 1].Equals(child, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReparsePoint(string directory)
    {
        try
        {
            return File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.Debug("Settings", $"Unable to inspect storage directory attributes for {directory}: {exception.Message}");
            return true;
        }
    }

    private static bool IsNestedDirectory(string parent, string candidate)
    {
        var parentWithSeparator = Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(parentWithSeparator, PathComparison);
    }

    private static bool DirectoriesEqual(string first, string second)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(first),
            Path.TrimEndingDirectorySeparator(second),
            PathComparison);

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private enum StorageFileCategory
    {
        SystemFiles,
        Images,
        Mods,
        News,
        Logs,
        Other
    }
}
