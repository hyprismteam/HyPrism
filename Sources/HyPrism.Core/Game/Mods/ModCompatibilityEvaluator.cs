// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Compression;
using System.Text.RegularExpressions;
using HyPrism.Core.Models;

namespace HyPrism.Core.Game.Mods;

/// <summary>
/// Compatibility status of a mod for a game version
/// </summary>
public enum ModCompatibilityStatus
{
    /// <summary>Compatibility could not be determined</summary>
    Unknown,
    /// <summary>The mod declares support for the game version</summary>
    Compatible,
    /// <summary>The mod declares no support for the game version</summary>
    Incompatible
}

/// <summary>
/// Compares mod version declarations with an installed game version
/// </summary>
public static partial class ModCompatibilityEvaluator
{
    /// <summary>Reads the game version from an instance server archive</summary>
    /// <param name="instancePath">Path to the game instance directory</param>
    /// <returns>The detected game version, or <see langword="null"/> when it cannot be read</returns>
    public static string? DetectInstanceGameVersion(string instancePath)
    {
        var serverJar = Path.Combine(instancePath, "Server", "HytaleServer.jar");
        if (!File.Exists(serverJar))
            return null;

        try
        {
            using var archive = ZipFile.OpenRead(serverJar);
            var manifest = archive.GetEntry("META-INF/MANIFEST.MF");
            if (manifest is null)
                return null;

            using var reader = new StreamReader(manifest.Open());
            while (reader.ReadLine() is { } line)
            {
                const string prefix = "Implementation-Version:";
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return line[prefix.Length..].Trim() is { Length: > 0 } version ? version : null;
            }
        }
        catch (InvalidDataException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return null;
    }

    /// <summary>Evaluates whether a mod supports the installed game version</summary>
    /// <param name="instanceGameVersion">Installed game version</param>
    /// <param name="supportedGameVersions">Versions declared by the mod</param>
    /// <returns>The compatibility status derived from the major and minor versions</returns>
    public static ModCompatibilityStatus Evaluate(
        string? instanceGameVersion,
        IReadOnlyCollection<string> supportedGameVersions)
    {
        if (!TryGetMajorMinor(instanceGameVersion, out var instanceVersion))
            return ModCompatibilityStatus.Unknown;

        HashSet<(int Major, int Minor)> declaredVersions = [];
        foreach (var supportedVersion in supportedGameVersions)
        {
            if (TryGetMajorMinor(supportedVersion, out var parsed))
                declaredVersions.Add(parsed);
        }

        if (declaredVersions.Count == 0)
            return ModCompatibilityStatus.Unknown;

        return declaredVersions.Contains(instanceVersion)
            ? ModCompatibilityStatus.Compatible
            : ModCompatibilityStatus.Incompatible;
    }

    /// <summary>Selects the first compatible file, or the first file with unknown compatibility</summary>
    /// <param name="files">Candidate mod files in preference order</param>
    /// <param name="instanceGameVersion">Installed game version</param>
    /// <returns>A recommended file, or <see langword="null"/> when no candidate is available</returns>
    public static ModFileInfo? SelectRecommendedFile(
        IEnumerable<ModFileInfo> files,
        string? instanceGameVersion)
    {
        ModFileInfo? unknown = null;
        foreach (var file in files)
        {
            switch (Evaluate(instanceGameVersion, file.GameVersions))
            {
                case ModCompatibilityStatus.Compatible:
                    return file;
                case ModCompatibilityStatus.Unknown when unknown is null:
                    unknown = file;
                    break;
            }
        }

        return unknown;
    }

    private static bool TryGetMajorMinor(string? value, out (int Major, int Minor) version)
    {
        var match = GameVersionRegex().Match(value ?? string.Empty);
        if (match.Success &&
            int.TryParse(match.Groups["major"].Value, out var major) &&
            int.TryParse(match.Groups["minor"].Value, out var minor))
        {
            version = (major, minor);
            return true;
        }

        version = default;
        return false;
    }

    [GeneratedRegex(@"(?<!\d)(?<major>\d+)\.(?<minor>\d+)(?!\d)")]
    private static partial Regex GameVersionRegex();
}
