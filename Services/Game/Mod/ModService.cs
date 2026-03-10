using HyPrism.Services.Core.App;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Game.Instance;
using System.Text.Json;
using System.Text.Json.Serialization;
using HyPrism.Models;
using System.Text.RegularExpressions;
using System.Net.Http.Json;

namespace HyPrism.Services.Game.Mod;

/// <summary>
/// Manages game modifications including searching, installing, updating, and tracking.
/// Integrates with CurseForge API for mod discovery and downloading.
/// </summary>
public class ModService : IModService
{
    private readonly HttpClient _httpClient;
    private readonly string _appDir;
    private readonly CurseForgeClient _cfClient;

    // Lock for mod manifest operations to prevent concurrent writes
    private static readonly SemaphoreSlim _modManifestLock = new(1, 1);

    /// <summary>
    /// Ensures the <paramref name="modsPath"/> exists as a directory.
    /// The Hytale game sometimes creates a regular <b>file</b> named <c>Mods</c>
    /// inside <c>UserData/</c>.  <see cref="Directory.CreateDirectory"/> throws
    /// <see cref="IOException"/> when a file with the same name already exists,
    /// so we delete the conflicting file first.
    /// </summary>
    internal static void EnsureModsDirectory(string modsPath)
    {
        if (File.Exists(modsPath))
        {
            // A plain file blocks Directory.CreateDirectory – remove it.
            Logger.Warning("ModService",
                $"Found a file where the Mods directory should be ({modsPath}), removing it");
            File.Delete(modsPath);
        }

        Directory.CreateDirectory(modsPath);
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IConfigService _configService;
    private readonly IInstanceService _instanceService;
    private readonly IProgressNotificationService _progressNotificationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModService"/> class.
    /// </summary>
    public ModService(
        HttpClient httpClient,
        string appDir,
        IConfigService configService,
        IInstanceService instanceService,
        IProgressNotificationService progressNotificationService)
    {
        _httpClient = httpClient;
        _appDir = appDir;
        _configService = configService;
        _instanceService = instanceService;
        _progressNotificationService = progressNotificationService;
        _cfClient = new CurseForgeClient(httpClient, () => _configService.Configuration.CurseForgeKey);
    }
    
    /// <inheritdoc/>
    public async Task<ModSearchResult> SearchModsAsync(string query, int page, int pageSize, string[] categories, int sortField, int sortOrder)
    {
        if (!_cfClient.HasApiKey())
            return new ModSearchResult { Mods = new List<ModInfo>(), TotalCount = 0 };

        try
        {
            var index = page * pageSize;
            var sortOrderStr = sortOrder == 0 ? "asc" : "desc";
            var endpoint = $"/v1/mods/search?gameId={CurseForgeClient.HytaleGameId}" +
                           $"&searchFilter={Uri.EscapeDataString(query)}" +
                           $"&index={index}&pageSize={pageSize}" +
                           $"&sortField={sortField}&sortOrder={sortOrderStr}";
            
            if (categories is { Length: > 0 })
            {
                var catId = categories[0];
                if (int.TryParse(catId, out var categoryId) && categoryId > 0)
                    endpoint += $"&categoryId={categoryId}";
            }
            
            using var request = _cfClient.CreateRequest(HttpMethod.Get, endpoint);
            using var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("ModService", $"CurseForge search returned {response.StatusCode}");
                return new ModSearchResult { Mods = new List<ModInfo>(), TotalCount = 0 };
            }

            var json = await response.Content.ReadAsStringAsync();
            var cfResponse = JsonSerializer.Deserialize<CurseForgeSearchResponse>(json, _jsonOptions);
            
            if (cfResponse?.Data == null)
                return new ModSearchResult { Mods = new List<ModInfo>(), TotalCount = 0 };

            var mods = cfResponse.Data.Select(MapToModInfo).ToList();
            
            return new ModSearchResult
            {
                Mods = mods,
                TotalCount = cfResponse.Pagination?.TotalCount ?? mods.Count
            };
        }
        catch (Exception ex)
        {
            Logger.Error("ModService", $"Search failed: {ex.Message}");
            return new ModSearchResult { Mods = new List<ModInfo>(), TotalCount = 0 };
        }
    }

    /// <inheritdoc/>
    public async Task<List<ModCategory>> GetModCategoriesAsync()
    {
        if (!_cfClient.HasApiKey())
            return GetFallbackCategories();
        
        try
        {
            var endpoint = $"/v1/categories?gameId={CurseForgeClient.HytaleGameId}";
            using var request = _cfClient.CreateRequest(HttpMethod.Get, endpoint);
            using var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("ModService", $"Categories request returned {response.StatusCode}");
                return GetFallbackCategories();
            }
            
            var json = await response.Content.ReadAsStringAsync();
            var cfResponse = JsonSerializer.Deserialize<CurseForgeCategoriesResponse>(json, _jsonOptions);
            
            if (cfResponse?.Data == null || cfResponse.Data.Count == 0)
                return GetFallbackCategories();
            
            // Find the "Mods" class category dynamically (matching original repo)
            var modsClass = cfResponse.Data.FirstOrDefault(c => c.IsClass == true &&
                string.Equals(c.Name, "mods", StringComparison.OrdinalIgnoreCase));
            int modsClassId = modsClass?.Id ?? 0;
            
            var categories = new List<ModCategory>
            {
                new ModCategory { Id = 0, Name = "All Mods", Slug = "all" }
            };
            
            // Get subcategories under the Mods class
            var modCategories = cfResponse.Data
                .Where(c => c.ParentCategoryId == modsClassId && c.IsClass != true)
                .Select(c => new ModCategory
                {
                    Id = c.Id,
                    Name = c.Name ?? "",
                    Slug = c.Slug ?? ""
                })
                .OrderBy(c => c.Name)
                .ToList();
            
            // Fallback: if no subcategories found, return all non-class categories
            if (modCategories.Count == 0)
            {
                modCategories = cfResponse.Data
                    .Where(c => c.IsClass != true)
                    .Select(c => new ModCategory
                    {
                        Id = c.Id,
                        Name = c.Name ?? "",
                        Slug = c.Slug ?? ""
                    })
                    .OrderBy(c => c.Name)
                    .ToList();
            }
            
            categories.AddRange(modCategories);
            
            return categories;
        }
        catch (Exception ex)
        {
            Logger.Warning("ModService", $"Failed to load categories: {ex.Message}");
            return GetFallbackCategories();
        }
    }
    
    private static List<ModCategory> GetFallbackCategories()
    {
        return new List<ModCategory>
        {
            new ModCategory { Id = 0, Name = "All Mods", Slug = "all" },
            new ModCategory { Id = 2, Name = "World Gen", Slug = "world-gen" },
            new ModCategory { Id = 3, Name = "Magic", Slug = "magic" },
            new ModCategory { Id = 4, Name = "Tech", Slug = "tech" }
        };
    }

    /// <inheritdoc/>
    public async Task<bool> InstallModFileToInstanceAsync(string slugOrId, string fileIdOrVersion, string instancePath, Action<string, string>? onProgress = null)
    {
        if (!_cfClient.HasApiKey()) return false;

        try
        {
            var requestedFileId = string.IsNullOrWhiteSpace(fileIdOrVersion) ? null : fileIdOrVersion.Trim();
            var cfFile = await _cfClient.ResolveFileAsync(slugOrId, requestedFileId);

            if (cfFile == null)
            {
                Logger.Warning("ModService", $"File info missing for mod {slugOrId} file '{fileIdOrVersion}'");
                return false;
            }

            var numericModId = cfFile.ModId > 0 ? cfFile.ModId.ToString() : slugOrId;
            var resolvedFileId = cfFile.Id > 0 ? cfFile.Id.ToString() : requestedFileId;

            if (string.IsNullOrWhiteSpace(resolvedFileId))
            {
                Logger.Warning("ModService", $"Could not resolve fileId for mod {slugOrId}");
                return false;
            }

            var downloadUrl = await _cfClient.ResolveDownloadUrlAsync(numericModId, resolvedFileId, cfFile.DownloadUrl, cfFile.FileName);
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                Logger.Warning("ModService", $"File info missing or no download URL for mod {numericModId} file {resolvedFileId}");
                return false;
            }
            
            onProgress?.Invoke("downloading", cfFile.FileName ?? "mod file");
            
            // Download the file to UserData/Mods folder (correct Hytale mod location)
            var modsPath = Path.Combine(instancePath, "UserData", "Mods");
            EnsureModsDirectory(modsPath);
            
            var fallbackName = !string.IsNullOrWhiteSpace(resolvedFileId)
                ? $"mod_{resolvedFileId}.jar"
                : $"mod_{cfFile.Id}.jar";
            var filePath = Path.Combine(modsPath, cfFile.FileName ?? fallbackName);

            const int maxDownloadAttempts = 3;
            var downloaded = false;
            for (var attempt = 1; attempt <= maxDownloadAttempts; attempt++)
            {
                using var downloadResponse = await _httpClient.GetAsync(downloadUrl);
                if (downloadResponse.IsSuccessStatusCode)
                {
                    await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                    await downloadResponse.Content.CopyToAsync(fs);
                    downloaded = true;
                    break;
                }

                Logger.Warning("ModService", $"Download returned {downloadResponse.StatusCode} for mod {numericModId} file {resolvedFileId} (attempt {attempt}/{maxDownloadAttempts})");
                if (attempt < maxDownloadAttempts)
                {
                    await Task.Delay(300 * attempt);
                }
            }

            if (!downloaded)
            {
                return false;
            }
            
            onProgress?.Invoke("installing", cfFile.FileName ?? "mod file");
            
            // Also get mod info for the manifest
            CurseForgeMod? modInfo = null;
            try
            {
                // Use numeric ID for mod info request
                var modEndpoint = $"/v1/mods/{numericModId}";
                using var modRequest = _cfClient.CreateRequest(HttpMethod.Get, modEndpoint);
                using var modResponse = await _httpClient.SendAsync(modRequest);
                if (modResponse.IsSuccessStatusCode)
                {
                    var modJson = await modResponse.Content.ReadAsStringAsync();
                    var modResp = JsonSerializer.Deserialize<CurseForgeModResponse>(modJson, _jsonOptions);
                    modInfo = modResp?.Data;
                }
            }
            catch { /* Non-critical */ }
            
            // Add to manifest
            var mods = GetInstanceInstalledMods(instancePath);
            
            // Find and remove old mod files before updating manifest
            var oldMods = mods.Where(m => 
                m.CurseForgeId == numericModId || 
                m.CurseForgeId == slugOrId || 
                m.Id == $"cf-{numericModId}" || 
                m.Id == $"cf-{slugOrId}").ToList();
            
            foreach (var oldMod in oldMods)
            {
                // Delete old mod file if it's different from the new one
                if (!string.IsNullOrWhiteSpace(oldMod.FileName) && oldMod.FileName != cfFile.FileName)
                {
                    var oldFilePath = Path.Combine(modsPath, oldMod.FileName);
                    var oldDisabledFilePath = Path.Combine(modsPath, oldMod.FileName + ".disabled");
                    
                    if (File.Exists(oldFilePath))
                    {
                        try
                        {
                            File.Delete(oldFilePath);
                            Logger.Info("ModService", $"Deleted old mod file: {oldMod.FileName}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning("ModService", $"Failed to delete old mod file {oldMod.FileName}: {ex.Message}");
                        }
                    }
                    else if (File.Exists(oldDisabledFilePath))
                    {
                        try
                        {
                            File.Delete(oldDisabledFilePath);
                            Logger.Info("ModService", $"Deleted old disabled mod file: {oldMod.FileName}.disabled");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning("ModService", $"Failed to delete old disabled mod file {oldMod.FileName}.disabled: {ex.Message}");
                        }
                    }
                }
            }
            
            // Remove existing entry for this mod if any (check both numeric ID and old slug-based ID)
            mods.RemoveAll(m => m.CurseForgeId == numericModId || m.CurseForgeId == slugOrId || m.Id == $"cf-{numericModId}" || m.Id == $"cf-{slugOrId}");
            
            var installedMod = new InstalledMod
            {
                Id = $"cf-{numericModId}",
                Name = modInfo?.Name ?? cfFile.DisplayName ?? cfFile.FileName ?? "Unknown Mod",
                Slug = modInfo?.Slug ?? "",
                Version = ExtractVersion(cfFile.DisplayName, cfFile.FileName),
                FileId = resolvedFileId,
                FileName = cfFile.FileName ?? "",
                Enabled = true,
                Author = modInfo?.Authors?.FirstOrDefault()?.Name ?? "",
                Description = modInfo?.Summary ?? "",
                IconUrl = modInfo?.Logo?.ThumbnailUrl ?? "",
                CurseForgeId = numericModId,  // Always save numeric ID
                FileDate = cfFile.FileDate ?? "",
                ReleaseType = cfFile.ReleaseType,
                Screenshots = modInfo?.Screenshots?.Select(s => new CurseForgeScreenshot
                {
                    Id = s.Id,
                    Title = s.Title,
                    ThumbnailUrl = s.ThumbnailUrl,
                    Url = s.Url
                }).ToList() ?? new List<CurseForgeScreenshot>()
            };
            
            mods.Add(installedMod);
            await SaveInstanceModsAsync(instancePath, mods);
            
            onProgress?.Invoke("complete", cfFile.FileName ?? "mod file");
            Logger.Success("ModService", $"Installed mod {installedMod.Name} (ID: {numericModId}) to {instancePath}");
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("ModService", $"Install failed: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public List<InstalledMod> GetInstanceInstalledMods(string instancePath)
    {
        var modsPath = Path.Combine(instancePath, "UserData", "Mods");
        var manifestPath = Path.Combine(modsPath, "manifest.json");
        var legacyManifestPath = Path.Combine(instancePath, "Client", "mods", "manifest.json");

        EnsureModsDirectory(modsPath);

        List<InstalledMod> mods;
        
        try
        {
            if (File.Exists(manifestPath))
            {
                var json = File.ReadAllText(manifestPath);
                mods = JsonSerializer.Deserialize<List<InstalledMod>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<InstalledMod>();
            }
            else if (File.Exists(legacyManifestPath))
            {
                var json = File.ReadAllText(legacyManifestPath);
                mods = JsonSerializer.Deserialize<List<InstalledMod>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<InstalledMod>();
            }
            else
            {
                mods = new List<InstalledMod>();
            }
        }
        catch
        {
            mods = new List<InstalledMod>();
        }

        // Ensure mods are discoverable even when manifest is missing or stale
        var diskFiles = Directory.EnumerateFiles(modsPath)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Where(name => !name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            .Where(name =>
                name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            .ToList();

        static string NormalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var chars = value.Where(char.IsLetterOrDigit).ToArray();
            return new string(chars).ToLowerInvariant();
        }

        // Keep existing metadata in sync with files when names drift (e.g. .jar -> .disabled)
        foreach (var mod in mods)
        {
            if (string.IsNullOrWhiteSpace(mod.FileName))
                continue;

            if (diskFiles.Any(f => string.Equals(f, mod.FileName, StringComparison.OrdinalIgnoreCase)))
                continue;

            var baseStem = mod.FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(mod.FileName)
                : Path.GetFileNameWithoutExtension(mod.FileName);

            var candidate = diskFiles.FirstOrDefault(f =>
                string.Equals(Path.GetFileNameWithoutExtension(f), baseStem, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(f)), baseStem, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(candidate))
            {
                mod.FileName = candidate;
                mod.Enabled = !candidate.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                if (!mod.Enabled && string.IsNullOrWhiteSpace(mod.DisabledOriginalExtension))
                {
                    var stem = Path.GetFileNameWithoutExtension(candidate);
                    var ext = Path.GetExtension(stem).ToLowerInvariant();
                    if (ext is ".jar" or ".zip")
                    {
                        mod.DisabledOriginalExtension = ext;
                    }
                }
            }
        }

        // Second pass: metadata-first matching by mod name/slug to avoid duplicate "local" rows
        foreach (var mod in mods)
        {
            if (!string.IsNullOrWhiteSpace(mod.FileName) &&
                diskFiles.Any(f => string.Equals(f, mod.FileName, StringComparison.OrdinalIgnoreCase)))
                continue;

            var modNameKey = NormalizeName(mod.Name);
            var slugKey = NormalizeName(mod.Slug);

            var candidate = diskFiles.FirstOrDefault(file =>
            {
                var stem = file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file))
                    : Path.GetFileNameWithoutExtension(file);

                var fileKey = NormalizeName(stem);
                if (string.IsNullOrEmpty(fileKey)) return false;

                var nameMatch = !string.IsNullOrEmpty(modNameKey) &&
                                (fileKey.StartsWith(modNameKey) || modNameKey.StartsWith(fileKey));
                var slugMatch = !string.IsNullOrEmpty(slugKey) &&
                                (fileKey.Contains(slugKey) || slugKey.Contains(fileKey));

                return nameMatch || slugMatch;
            });

            if (!string.IsNullOrEmpty(candidate))
            {
                mod.FileName = candidate;
                mod.Enabled = !candidate.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                if (!mod.Enabled && string.IsNullOrWhiteSpace(mod.DisabledOriginalExtension))
                {
                    var stem = Path.GetFileNameWithoutExtension(candidate);
                    var ext = Path.GetExtension(stem).ToLowerInvariant();
                    if (ext is ".jar" or ".zip")
                    {
                        mod.DisabledOriginalExtension = ext;
                    }
                }
            }
        }

        foreach (var fileName in diskFiles)
        {
            if (mods.Any(m => string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
                continue;

            var enabled = !fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            var displayName = enabled
                ? Path.GetFileNameWithoutExtension(fileName)
                : Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(fileName));

            var disabledOriginalExtension = "";
            if (!enabled)
            {
                var stem = Path.GetFileNameWithoutExtension(fileName);
                var ext = Path.GetExtension(stem).ToLowerInvariant();
                if (ext is ".jar" or ".zip")
                {
                    disabledOriginalExtension = ext;
                }
            }

            mods.Add(new InstalledMod
            {
                Id = $"local-{fileName}",
                Name = displayName,
                FileName = fileName,
                Enabled = enabled,
                Version = "local",
                Author = "Local file",
                DisabledOriginalExtension = disabledOriginalExtension
            });
        }

        // Final pass: if a synthetic local entry and a metadata-backed entry represent the same mod,
        // keep only the metadata-backed entry.
        bool IsSyntheticLocal(InstalledMod mod)
        {
            var isLocalId = mod.Id?.StartsWith("local-", StringComparison.OrdinalIgnoreCase) == true;
            var isLocalVersion = string.Equals(mod.Version, "local", StringComparison.OrdinalIgnoreCase);
            var isLocalAuthor = string.Equals(mod.Author, "Local file", StringComparison.OrdinalIgnoreCase);
            return isLocalId || (isLocalVersion && isLocalAuthor);
        }

        var metadataMods = mods.Where(m => !IsSyntheticLocal(m)).ToList();
        mods = mods
            .Where(local =>
            {
                if (!IsSyntheticLocal(local)) return true;

                var localNameKey = NormalizeName(local.Name);
                var localFileStem = local.FileName?.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) == true
                    ? Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(local.FileName))
                    : Path.GetFileNameWithoutExtension(local.FileName ?? "");
                var localFileKey = NormalizeName(localFileStem ?? "");

                var hasMetadataTwin = metadataMods.Any(meta =>
                {
                    var metaNameKey = NormalizeName(meta.Name);
                    var metaSlugKey = NormalizeName(meta.Slug);
                    var metaFileStem = meta.FileName?.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) == true
                        ? Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(meta.FileName))
                        : Path.GetFileNameWithoutExtension(meta.FileName ?? "");
                    var metaFileKey = NormalizeName(metaFileStem ?? "");

                    if (!string.IsNullOrEmpty(local.FileName) && !string.IsNullOrEmpty(meta.FileName) &&
                        string.Equals(local.FileName, meta.FileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (!string.IsNullOrEmpty(localNameKey) &&
                        ((!string.IsNullOrEmpty(metaNameKey) && localNameKey == metaNameKey) ||
                         (!string.IsNullOrEmpty(metaSlugKey) && localNameKey == metaSlugKey)))
                    {
                        return true;
                    }

                    if (!string.IsNullOrEmpty(localFileKey) &&
                        ((!string.IsNullOrEmpty(metaFileKey) && localFileKey == metaFileKey) ||
                         (!string.IsNullOrEmpty(metaNameKey) && (localFileKey.Contains(metaNameKey) || metaNameKey.Contains(localFileKey))) ||
                         (!string.IsNullOrEmpty(metaSlugKey) && (localFileKey.Contains(metaSlugKey) || metaSlugKey.Contains(localFileKey)))))
                    {
                        return true;
                    }

                    return false;
                });

                return !hasMetadataTwin;
            })
            .ToList();

        return mods;
    }
    
    /// <inheritdoc/>
    public async Task SaveInstanceModsAsync(string instancePath, List<InstalledMod> mods)
    {
        await _modManifestLock.WaitAsync();
        try
        {
            var modsPath = Path.Combine(instancePath, "UserData", "Mods");
            EnsureModsDirectory(modsPath);
            var manifestPath = Path.Combine(modsPath, "manifest.json");
            
            var json = JsonSerializer.Serialize(mods, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(manifestPath, json);
        }
        finally
        {
            _modManifestLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<ModFilesResult> GetModFilesAsync(string modId, int page, int pageSize)
    {
        if (!_cfClient.HasApiKey())
            return new ModFilesResult();

        try
        {
            var index = page * pageSize;
            var endpoint = $"/v1/mods/{modId}/files?index={index}&pageSize={pageSize}";
            using var request = _cfClient.CreateRequest(HttpMethod.Get, endpoint);
            using var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("ModService", $"Get mod files returned {response.StatusCode}");
                return new ModFilesResult();
            }
            
            var json = await response.Content.ReadAsStringAsync();
            var cfResponse = JsonSerializer.Deserialize<CurseForgeFilesResponse>(json, _jsonOptions);
            
            if (cfResponse?.Data == null)
                return new ModFilesResult();
            
            return new ModFilesResult
            {
                Files = cfResponse.Data.Select(f => new ModFileInfo
                {
                    Id = f.Id.ToString(),
                    ModId = f.ModId.ToString(),
                    FileName = f.FileName ?? "",
                    DisplayName = f.DisplayName ?? "",
                    DownloadUrl = f.DownloadUrl ?? "",
                    FileLength = f.FileLength,
                    FileDate = f.FileDate ?? "",
                    ReleaseType = f.ReleaseType,
                    GameVersions = f.GameVersions ?? new List<string>(),
                    DownloadCount = f.DownloadCount
                }).ToList(),
                TotalCount = cfResponse.Pagination?.TotalCount ?? cfResponse.Data.Count
            };
        }
        catch (Exception ex)
        {
            Logger.Error("ModService", $"Get mod files failed: {ex.Message}");
            return new ModFilesResult();
        }
    }

    /// <inheritdoc/>
    public async Task<ModInfo?> GetModAsync(string modIdOrSlug)
    {
        if (!_cfClient.HasApiKey() || string.IsNullOrWhiteSpace(modIdOrSlug))
            return null;

        try
        {
            CurseForgeMod? cfMod = null;
            var token = modIdOrSlug.Trim();

            if (int.TryParse(token, out _))
            {
                var endpoint = $"/v1/mods/{token}";
                using var request = _cfClient.CreateRequest(HttpMethod.Get, endpoint);
                using var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var modResponse = JsonSerializer.Deserialize<CurseForgeModResponse>(json, _jsonOptions);
                    cfMod = modResponse?.Data;
                }
            }
            else
            {
                var slugEndpoint = $"/v1/mods/search?gameId={CurseForgeClient.HytaleGameId}&slug={Uri.EscapeDataString(token)}&index=0&pageSize=1";
                using var slugRequest = _cfClient.CreateRequest(HttpMethod.Get, slugEndpoint);
                using var slugResponse = await _httpClient.SendAsync(slugRequest);
                if (slugResponse.IsSuccessStatusCode)
                {
                    var slugJson = await slugResponse.Content.ReadAsStringAsync();
                    var searchResponse = JsonSerializer.Deserialize<CurseForgeSearchResponse>(slugJson, _jsonOptions);
                    cfMod = searchResponse?.Data?.FirstOrDefault();
                }

                if (cfMod == null)
                {
                    var fallbackEndpoint = $"/v1/mods/search?gameId={CurseForgeClient.HytaleGameId}&searchFilter={Uri.EscapeDataString(token)}&index=0&pageSize=20";
                    using var fallbackRequest = _cfClient.CreateRequest(HttpMethod.Get, fallbackEndpoint);
                    using var fallbackResponse = await _httpClient.SendAsync(fallbackRequest);
                    if (fallbackResponse.IsSuccessStatusCode)
                    {
                        var fallbackJson = await fallbackResponse.Content.ReadAsStringAsync();
                        var fallbackSearch = JsonSerializer.Deserialize<CurseForgeSearchResponse>(fallbackJson, _jsonOptions);
                        cfMod = fallbackSearch?.Data?.FirstOrDefault(m =>
                            string.Equals(m.Slug, token, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(m.Name, token, StringComparison.OrdinalIgnoreCase));
                    }
                }
            }

            return cfMod == null ? null : MapToModInfo(cfMod);
        }
        catch (Exception ex)
        {
            Logger.Warning("ModService", $"Get mod failed for '{modIdOrSlug}': {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<string> GetModFileChangelogAsync(string modId, string fileId)
    {
        if (!_cfClient.HasApiKey() || string.IsNullOrWhiteSpace(modId) || string.IsNullOrWhiteSpace(fileId))
            return string.Empty;

        try
        {
            var endpoint = $"/v1/mods/{modId}/files/{fileId}/changelog";
            using var request = _cfClient.CreateRequest(HttpMethod.Get, endpoint);
            using var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("ModService", $"Get changelog returned {response.StatusCode} for mod={modId} file={fileId}");
                return string.Empty;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            var body = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(body))
                return string.Empty;

            if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("data", out var dataElement) &&
                    dataElement.ValueKind == JsonValueKind.String)
                {
                    return dataElement.GetString() ?? string.Empty;
                }

                if (doc.RootElement.ValueKind == JsonValueKind.String)
                {
                    return doc.RootElement.GetString() ?? string.Empty;
                }
            }

            return body;
        }
        catch (Exception ex)
        {
            Logger.Warning("ModService", $"Get changelog failed for mod={modId} file={fileId}: {ex.Message}");
            return string.Empty;
        }
    }

    /// <inheritdoc/>
    public async Task<List<InstalledMod>> CheckInstanceModUpdatesAsync(string instancePath)
    {
        if (!_cfClient.HasApiKey())
            return new List<InstalledMod>();
            
        var installedMods = GetInstanceInstalledMods(instancePath);
        var modsWithUpdates = new List<InstalledMod>();
        
        foreach (var mod in installedMods)
        {
            if (string.IsNullOrEmpty(mod.CurseForgeId)) continue;
            
            try
            {
                var endpoint = $"/v1/mods/{mod.CurseForgeId}/files?pageSize=1";
                using var request = _cfClient.CreateRequest(HttpMethod.Get, endpoint);
                using var response = await _httpClient.SendAsync(request);
                
                if (!response.IsSuccessStatusCode) continue;
                
                var json = await response.Content.ReadAsStringAsync();
                var cfResponse = JsonSerializer.Deserialize<CurseForgeFilesResponse>(json, _jsonOptions);
                
                var latestFile = cfResponse?.Data?.FirstOrDefault();
                if (latestFile == null) continue;
                
                // If we have a newer file than what's installed
                if (!string.IsNullOrEmpty(mod.FileId) && latestFile.Id.ToString() != mod.FileId)
                {
                    mod.LatestFileId = latestFile.Id.ToString();
                    mod.LatestVersion = latestFile.DisplayName ?? "";
                    modsWithUpdates.Add(mod);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("ModService", $"Update check failed for {mod.Name}: {ex.Message}");
            }
        }
        
        return modsWithUpdates;
    }

    /// <inheritdoc/>
    public async Task<bool> InstallLocalModFile(string sourcePath, string instancePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                Logger.Warning("ModService", $"Source mod file not found: {sourcePath}");
                return false;
            }
            
            var modsPath = Path.Combine(instancePath, "UserData", "Mods");
            EnsureModsDirectory(modsPath);
            
            var fileName = Path.GetFileName(sourcePath);
            var destPath = Path.Combine(modsPath, fileName);
            
            File.Copy(sourcePath, destPath, true);
            
            // Add to manifest
            var mods = GetInstanceInstalledMods(instancePath);
            
            // Remove existing entry with same filename
            mods.RemoveAll(m => m.FileName == fileName);
            
            mods.Add(new InstalledMod
            {
                Id = $"local-{Guid.NewGuid():N}",
                Name = Path.GetFileNameWithoutExtension(fileName),
                FileName = fileName,
                Enabled = true,
                Version = "local",
                Author = "Local file"
            });
            
            await SaveInstanceModsAsync(instancePath, mods);
            Logger.Success("ModService", $"Installed local mod: {fileName}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("ModService", $"Install local mod failed: {ex.Message}");
            return false;
        }
    }
    
    /// <inheritdoc/>
    public async Task<bool> InstallModFromBase64(string fileName, string base64Content, string instancePath)
    {
        try
        {
            var modsPath = Path.Combine(instancePath, "UserData", "Mods");
            EnsureModsDirectory(modsPath);
            
            var destPath = Path.Combine(modsPath, fileName);
            var bytes = Convert.FromBase64String(base64Content);
            await File.WriteAllBytesAsync(destPath, bytes);
            
            // Add to manifest
            var mods = GetInstanceInstalledMods(instancePath);
            mods.RemoveAll(m => m.FileName == fileName);
            
            mods.Add(new InstalledMod
            {
                Id = $"local-{Guid.NewGuid():N}",
                Name = Path.GetFileNameWithoutExtension(fileName),
                FileName = fileName,
                Enabled = true,
                Version = "local",
                Author = "Imported file"
            });
            
            await SaveInstanceModsAsync(instancePath, mods);
            Logger.Success("ModService", $"Installed mod from base64: {fileName}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("ModService", $"Install mod from base64 failed: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Tries to extract a semver-like version string from a display name or filename.
    /// Looks for semver-like patterns (e.g., "1.2.7", "0.3.1-beta") and returns the first match.
    /// Falls back to DisplayName or FileName if no version pattern is found.
    /// </summary>
    private static string ExtractVersion(string? displayName, string? fileName)
    {
        // Try to extract a semver-like version from displayName first, then fileName
        var versionRegex = new Regex(@"(\d+\.\d+(?:\.\d+)?(?:[-.]\w+)*)");
        
        if (!string.IsNullOrEmpty(displayName))
        {
            var match = versionRegex.Match(displayName);
            if (match.Success) return match.Groups[1].Value;
        }
        
        if (!string.IsNullOrEmpty(fileName))
        {
            // Strip extension first
            var name = fileName;
            if (name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];
            else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];
                
            var match = versionRegex.Match(name);
            if (match.Success) return match.Groups[1].Value;
        }
        
        return displayName ?? fileName ?? "";
    }

    /// <summary>
    /// Maps a CurseForge API mod to the normalized ModInfo.
    /// </summary>
    private static ModInfo MapToModInfo(CurseForgeMod cfMod)
    {
        return new ModInfo
        {
            Id = cfMod.Id.ToString(),
            Name = cfMod.Name ?? "",
            Slug = cfMod.Slug ?? "",
            Summary = cfMod.Summary ?? "",
            Author = cfMod.Authors?.FirstOrDefault()?.Name ?? "",
            DownloadCount = cfMod.DownloadCount,
            IconUrl = cfMod.Logo?.ThumbnailUrl ?? "",
            ThumbnailUrl = cfMod.Logo?.Url ?? "",
            Categories = cfMod.Categories?.Select(c => c.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
            DateUpdated = cfMod.DateModified ?? "",
            LatestFileId = cfMod.LatestFiles?.FirstOrDefault()?.Id.ToString() ?? "",
            Screenshots = cfMod.Screenshots ?? new List<CurseForgeScreenshot>()
        };
    }
}
