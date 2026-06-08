using System.Text.Encodings.Web;
using System.Text.Json;
using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.Game.Sources;

/// <summary>
/// Loads mirror definitions from JSON meta files in the Mirrors directory.
/// On first launch (directory missing or empty), generates default built-in mirror definitions.
/// Users can add custom mirror files to appDir/Mirrors/ — the launcher reads them on startup.
/// </summary>
public static class MirrorLoaderService
{
    private const string MirrorsDirName = "Mirrors";
    private const string MirrorFileExtension = ".mirror.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Loads all mirror sources from the Mirrors directory.
    /// Generates default mirror files if directory is missing or empty.
    /// </summary>
    /// <param name="appDir">Application data directory.</param>
    /// <param name="httpClient">Shared HTTP client.</param>
    /// <returns>List of IVersionSource instances created from mirror meta files.</returns>
    public static List<IVersionSource> LoadAll(string appDir, HttpClient httpClient)
    {
        var mirrorsDir = Path.Combine(appDir, MirrorsDirName);

        // Generate defaults on first launch (directory missing or empty)
        if (!Directory.Exists(mirrorsDir) || !Directory.EnumerateFiles(mirrorsDir, $"*{MirrorFileExtension}").Any())
        {
            Logger.Info("MirrorLoader", "No mirror definitions found, generating defaults...");
            GenerateDefaults(mirrorsDir);
        }

        var sources = new List<IVersionSource>();
        var files = Directory.GetFiles(mirrorsDir, $"*{MirrorFileExtension}");

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var meta = JsonSerializer.Deserialize<MirrorMeta>(json, JsonOptions);

                if (meta == null)
                {
                    Logger.Warning("MirrorLoader", $"Failed to deserialize: {Path.GetFileName(file)}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(meta.Id))
                {
                    Logger.Warning("MirrorLoader", $"Mirror file has no id: {Path.GetFileName(file)}");
                    continue;
                }

                if (!meta.Enabled)
                {
                    Logger.Info("MirrorLoader", $"Mirror '{meta.Id}' is disabled, skipping");
                    continue;
                }

                // Validate sourceType has matching config
                if (meta.SourceType == "pattern" && meta.Pattern == null)
                {
                    Logger.Warning("MirrorLoader", $"Mirror '{meta.Id}' has sourceType 'pattern' but no pattern config");
                    continue;
                }
                if (meta.SourceType == "json-index" && meta.JsonIndex == null)
                {
                    Logger.Warning("MirrorLoader", $"Mirror '{meta.Id}' has sourceType 'json-index' but no jsonIndex config");
                    continue;
                }

                var source = new JsonMirrorSource(meta, httpClient);
                sources.Add(source);

                Logger.Info("MirrorLoader", $"Loaded mirror: {meta.Name} ({meta.Id}) [priority={meta.Priority}, type={meta.SourceType}]");
            }
            catch (JsonException ex)
            {
                Logger.Warning("MirrorLoader", $"Invalid JSON in {Path.GetFileName(file)}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Logger.Warning("MirrorLoader", $"Error loading {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        // Sort by priority
        sources.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        Logger.Success("MirrorLoader", $"Loaded {sources.Count} mirror source(s) from {mirrorsDir}");
        return sources;
    }

    /// <summary>
    /// Gets the path to the Mirrors directory.
    /// </summary>
    public static string GetMirrorsDirectory(string appDir)
        => Path.Combine(appDir, MirrorsDirName);

    /// <summary>
    /// Gets a list of all mirror metadata (without creating sources).
    /// </summary>
    public static List<MirrorMeta> GetAllMirrorMetas(string appDir)
    {
        var mirrorsDir = GetMirrorsDirectory(appDir);
        var metas = new List<MirrorMeta>();

        if (!Directory.Exists(mirrorsDir))
            return metas;

        var files = Directory.GetFiles(mirrorsDir, $"*{MirrorFileExtension}");
        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var meta = JsonSerializer.Deserialize<MirrorMeta>(json, JsonOptions);
                if (meta != null && !string.IsNullOrWhiteSpace(meta.Id))
                {
                    metas.Add(meta);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("MirrorLoader", $"Failed to read mirror meta from {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return metas.OrderBy(m => m.Priority).ToList();
    }

    /// <summary>
    /// Saves a mirror metadata to a .mirror.json file.
    /// </summary>
    public static void SaveMirror(string appDir, MirrorMeta meta)
    {
        if (meta == null || string.IsNullOrWhiteSpace(meta.Id))
            throw new ArgumentException("Mirror must have a valid ID");

        var mirrorsDir = GetMirrorsDirectory(appDir);
        Directory.CreateDirectory(mirrorsDir);

        var fileName = $"{meta.Id}{MirrorFileExtension}";
        var filePath = Path.Combine(mirrorsDir, fileName);

        var json = JsonSerializer.Serialize(meta, JsonOptions);
        File.WriteAllText(filePath, json);

        Logger.Info("MirrorLoader", $"Saved mirror: {fileName}");
    }

    /// <summary>
    /// Deletes a mirror by ID.
    /// </summary>
    public static bool DeleteMirror(string appDir, string mirrorId)
    {
        if (string.IsNullOrWhiteSpace(mirrorId))
            return false;

        var mirrorsDir = GetMirrorsDirectory(appDir);
        var fileName = $"{mirrorId}{MirrorFileExtension}";
        var filePath = Path.Combine(mirrorsDir, fileName);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            Logger.Info("MirrorLoader", $"Deleted mirror: {fileName}");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a mirror with the given ID exists.
    /// </summary>
    public static bool MirrorExists(string appDir, string mirrorId)
    {
        if (string.IsNullOrWhiteSpace(mirrorId))
            return false;

        var mirrorsDir = GetMirrorsDirectory(appDir);
        var fileName = $"{mirrorId}{MirrorFileExtension}";
        var filePath = Path.Combine(mirrorsDir, fileName);

        return File.Exists(filePath);
    }

    /// <summary>
    /// Generates default mirror JSON files for the built-in community mirrors.
    /// </summary>
    private static void GenerateDefaults(string mirrorsDir)
    {
        Directory.CreateDirectory(mirrorsDir);

        var defaults = GetDefaultMirrors();
        foreach (var meta in defaults)
        {
            try
            {
                var fileName = $"{meta.Id}{MirrorFileExtension}";
                var filePath = Path.Combine(mirrorsDir, fileName);

                var json = JsonSerializer.Serialize(meta, JsonOptions);
                File.WriteAllText(filePath, json);

                Logger.Info("MirrorLoader", $"Generated default mirror: {fileName}");
            }
            catch (Exception ex)
            {
                Logger.Warning("MirrorLoader", $"Failed to generate default mirror '{meta.Id}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Returns the built-in mirror definitions that were previously hardcoded.
    /// NOTE: As of this version, no default mirrors are shipped. Users must add mirrors manually
    /// through Settings > Downloads or by placing .mirror.json files in the Mirrors folder.
    /// </summary>
    private static List<MirrorMeta> GetDefaultMirrors()
    {
        // No preset mirrors - users must add them manually via Settings > Downloads
        // or by placing .mirror.json files in the Mirrors folder
        return new List<MirrorMeta>();
    }
}
