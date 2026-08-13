// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Compression;
using System.Text.Json;

namespace HyPrism.LocalNode;

/// <summary>
/// Loads the character creator item IDs exposed by the installed game assets
/// </summary>
public sealed class LocalCosmeticsCatalog
{
    private const long MaximumCatalogEntrySize = 10 * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> CategoryFiles =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["bodyCharacteristic"] = "BodyCharacteristics.json",
            ["cape"] = "Capes.json",
            ["earAccessory"] = "EarAccessory.json",
            ["ears"] = "Ears.json",
            ["eyebrows"] = "Eyebrows.json",
            ["eyes"] = "Eyes.json",
            ["face"] = "Faces.json",
            ["faceAccessory"] = "FaceAccessory.json",
            ["facialHair"] = "FacialHair.json",
            ["gloves"] = "Gloves.json",
            ["haircut"] = "Haircuts.json",
            ["headAccessory"] = "HeadAccessory.json",
            ["mouth"] = "Mouths.json",
            ["overpants"] = "Overpants.json",
            ["overtop"] = "Overtops.json",
            ["pants"] = "Pants.json",
            ["shoes"] = "Shoes.json",
            ["skinFeature"] = "SkinFeatures.json",
            ["undertop"] = "Undertops.json",
            ["underwear"] = "Underwear.json"
        };

    private static readonly IReadOnlyDictionary<string, string[]> FallbackCosmetics =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["bodyCharacteristic"] = ["Default", "Muscular"],
            ["cape"] = ["Cape_Royal_Emissary", "Cape_New_Beginning", "Cape_Forest_Guardian", "Cape_PopStar"],
            ["earAccessory"] = [],
            ["ears"] = [],
            ["eyebrows"] = [],
            ["eyes"] = [],
            ["face"] = [],
            ["faceAccessory"] = [],
            ["facialHair"] = [],
            ["gloves"] = [],
            ["haircut"] = [],
            ["headAccessory"] = [],
            ["mouth"] = [],
            ["overpants"] = [],
            ["overtop"] = [],
            ["pants"] = [],
            ["shoes"] = [],
            ["skinFeature"] = [],
            ["undertop"] = [],
            ["underwear"] = []
        };

    private readonly object _gate = new();
    private readonly LocalNodeLog? _log;
    private IReadOnlyDictionary<string, string[]> _cosmetics = FallbackCosmetics;
    private string? _loadedAssetsPath;
    private long _loadedAssetsLength;
    private DateTime _loadedAssetsWriteTimeUtc;

    /// <summary>
    /// Creates a catalog with an optional explicit Assets.zip path
    /// </summary>
    public LocalCosmeticsCatalog(string? assetsPath = null, LocalNodeLog? log = null)
    {
        _log = log;
        if (!string.IsNullOrWhiteSpace(assetsPath))
            ConfigureAssetsPath(assetsPath);
    }

    /// <summary>
    /// Selects Assets.zip from the game instance being launched
    /// </summary>
    public void ConfigureGameDirectory(string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);

        var candidates = new[]
        {
            Path.Combine(gameDirectory, "Assets.zip"),
            Path.Combine(gameDirectory, "Client", "Assets.zip")
        };
        ConfigureAssetsPath(candidates.FirstOrDefault(File.Exists));
    }

    /// <summary>
    /// Gets every character creator item ID grouped by the category names expected by the client
    /// </summary>
    public IReadOnlyDictionary<string, string[]> GetUnlockedCosmetics()
    {
        lock (_gate)
            return _cosmetics;
    }

    private void ConfigureAssetsPath(string? assetsPath)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(assetsPath) || !File.Exists(assetsPath))
            {
                _cosmetics = FallbackCosmetics;
                _loadedAssetsPath = null;
                _loadedAssetsLength = 0;
                _loadedAssetsWriteTimeUtc = default;
                _log?.Warning("Assets.zip was not found, using the fallback cosmetics catalog");
                return;
            }

            var fullPath = Path.GetFullPath(assetsPath);
            var file = new FileInfo(fullPath);
            if (string.Equals(_loadedAssetsPath, fullPath, StringComparison.Ordinal)
                && _loadedAssetsLength == file.Length
                && _loadedAssetsWriteTimeUtc == file.LastWriteTimeUtc)
            {
                return;
            }

            var loadedCosmetics = LoadFromArchive(fullPath);
            _cosmetics = loadedCosmetics;
            _loadedAssetsPath = fullPath;
            _loadedAssetsLength = file.Length;
            _loadedAssetsWriteTimeUtc = file.LastWriteTimeUtc;

            if (!ReferenceEquals(loadedCosmetics, FallbackCosmetics))
            {
                var itemCount = loadedCosmetics.Values.Sum(items => items.Length);
                _log?.Info(
                    $"Loaded {itemCount} cosmetics in {loadedCosmetics.Count} categories from '{fullPath}'");
            }
        }
    }

    private IReadOnlyDictionary<string, string[]> LoadFromArchive(string assetsPath)
    {
        try
        {
            using var stream = new FileStream(
                assetsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var cosmetics = new Dictionary<string, string[]>(StringComparer.Ordinal);

            foreach (var (category, fileName) in CategoryFiles)
            {
                var entry = archive.GetEntry($"Cosmetics/CharacterCreator/{fileName}");
                cosmetics[category] = entry is null || entry.Length > MaximumCatalogEntrySize
                    ? []
                    : ReadItemIds(entry);
            }

            return cosmetics.Values.Any(items => items.Length > 0)
                ? cosmetics
                : FallbackCosmetics;
        }
        catch (Exception exception) when (exception is IOException
                                          or InvalidDataException
                                          or UnauthorizedAccessException
                                          or JsonException)
        {
            _log?.Error($"Could not load cosmetics from '{assetsPath}': {exception.Message}");
            return FallbackCosmetics;
        }
    }

    private static string[] ReadItemIds(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        return document.RootElement.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object
                           && item.TryGetProperty("Id", out var id)
                           && id.ValueKind == JsonValueKind.String
                           && !string.IsNullOrWhiteSpace(id.GetString()))
            .Select(item => item.GetProperty("Id").GetString()!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
