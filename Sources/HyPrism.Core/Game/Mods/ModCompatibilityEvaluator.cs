// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Compression;
using System.Text.RegularExpressions;
using HyPrism.Core.Models;

namespace HyPrism.Core.Game.Mods;

public enum ModCompatibilityStatus
{
    Unknown,
    Compatible,
    Incompatible
}

public static partial class ModCompatibilityEvaluator
{
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
