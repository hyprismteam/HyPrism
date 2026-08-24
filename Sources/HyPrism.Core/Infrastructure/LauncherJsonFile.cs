// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Core.Infrastructure;

/// <summary>
/// Resolves launcher-owned JSON files to their canonical PascalCase names and migrates legacy names
/// </summary>
internal static class LauncherJsonFile
{
    private static readonly Lock MigrationLock = new();

    /// <summary>
    /// Returns the canonical file path after moving a legacy file when one is present
    /// </summary>
    public static string GetPath(string directory, string canonicalFileName, params string[] legacyFileNames)
    {
        lock (MigrationLock)
        {
            return GetPathCore(directory, canonicalFileName, legacyFileNames);
        }
    }

    private static string GetPathCore(string directory, string canonicalFileName, string[] legacyFileNames)
    {
        var canonicalPath = Path.Combine(directory, canonicalFileName);
        if (!Directory.Exists(directory))
            return canonicalPath;

        var files = Directory.EnumerateFiles(directory).ToArray();
        var exactCanonicalPath = files.FirstOrDefault(path =>
            string.Equals(Path.GetFileName(path), canonicalFileName, StringComparison.Ordinal));
        if (exactCanonicalPath is not null)
            return exactCanonicalPath;

        var legacyPath = files.FirstOrDefault(path => legacyFileNames.Any(legacyFileName =>
            string.Equals(Path.GetFileName(path), legacyFileName, StringComparison.Ordinal)));
        legacyPath ??= files.FirstOrDefault(path =>
            string.Equals(Path.GetFileName(path), canonicalFileName, StringComparison.OrdinalIgnoreCase));
        if (legacyPath is null)
            return canonicalPath;

        try
        {
            MoveWithCanonicalCasing(legacyPath, canonicalPath);
            Logger.Info(
                "Storage",
                $"Migrated JSON file '{Path.GetFileName(legacyPath)}' to '{canonicalFileName}'");
            return canonicalPath;
        }
        catch (Exception ex)
        {
            if (Directory.EnumerateFiles(directory).Any(path =>
                string.Equals(Path.GetFileName(path), canonicalFileName, StringComparison.Ordinal)))
            {
                return canonicalPath;
            }

            Logger.Warning(
                "Storage",
                $"Could not migrate JSON file '{legacyPath}' to '{canonicalPath}': {ex.Message}");
            return legacyPath;
        }
    }

    private static void MoveWithCanonicalCasing(string sourcePath, string destinationPath)
    {
        if (!string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Move(sourcePath, destinationPath);
            return;
        }

        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(sourcePath)!,
            $".{Guid.NewGuid():N}.json-migration");
        File.Move(sourcePath, temporaryPath);
        try
        {
            File.Move(temporaryPath, destinationPath);
        }
        catch
        {
            File.Move(temporaryPath, sourcePath);
            throw;
        }
    }
}
