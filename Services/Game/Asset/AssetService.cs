using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Game.Instance;
using HyPrism.Models;

namespace HyPrism.Services.Game.Asset;

/// <summary>
/// Manages game asset files including Assets.zip extraction and cosmetic item parsing.
/// Handles reading cosmetic definitions from the game's asset archive.
/// </summary>
/// <remarks>
/// Cosmetics are parsed from JSON files within Assets.zip and mapped to
/// category names used by the authentication server.
/// </remarks>
public class AssetService : IAssetService
{
    private readonly IInstanceService _instanceService;
    private readonly string _appDir;
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    
    // Cosmetic category file mappings (matching auth server structure)
    private static readonly Dictionary<string, string> CosmeticCategoryMap = new()
    {
        { "BodyCharacteristics.json", "bodyCharacteristic" },
        { "Capes.json", "cape" },
        { "EarAccessory.json", "earAccessory" },
        { "Ears.json", "ears" },
        { "Eyebrows.json", "eyebrows" },
        { "Eyes.json", "eyes" },
        { "Faces.json", "face" },
        { "FaceAccessory.json", "faceAccessory" },
        { "FacialHair.json", "facialHair" },
        { "Gloves.json", "gloves" },
        { "Haircuts.json", "haircut" },
        { "HeadAccessory.json", "headAccessory" },
        { "Mouths.json", "mouth" },
        { "Overpants.json", "overpants" },
        { "Overtops.json", "overtop" },
        { "Pants.json", "pants" },
        { "Shoes.json", "shoes" },
        { "SkinFeatures.json", "skinFeature" },
        { "Undertops.json", "undertop" },
        { "Underwear.json", "underwear" }
    };
    
    /// <summary>
    /// Initializes a new instance of the <see cref="AssetService"/> class.
    /// </summary>
    /// <param name="instanceService">The instance service for path resolution.</param>
    /// <param name="appDir">The application data directory path.</param>
    public AssetService(IInstanceService instanceService, string appDir)
    {
        _instanceService = instanceService;
        _appDir = appDir;
    }
    
    /// <summary>
    /// Checks if Assets.zip exists for the specified instance.
    /// </summary>
    /// <param name="versionPath">The path to the game version directory.</param>
    /// <returns><c>true</c> if Assets.zip exists; otherwise, <c>false</c>.</returns>
    public bool HasAssetsZip(string versionPath)
    {
        var assetsZipPath = GetAssetsZipPath(versionPath);
        bool exists = File.Exists(assetsZipPath);
        Logger.Info("Assets", $"HasAssetsZip: path={assetsZipPath}, exists={exists}");
        return exists;
    }
    
    /// <summary>
    /// Gets the path to Assets.zip if it exists.
    /// </summary>
    /// <param name="versionPath">The path to the game version directory.</param>
    /// <returns>The path to Assets.zip if it exists; otherwise, <c>null</c>.</returns>
    public string? GetAssetsZipPathIfExists(string versionPath)
    {
        var assetsZipPath = GetAssetsZipPath(versionPath);
        return File.Exists(assetsZipPath) ? assetsZipPath : null;
    }
    
    /// <summary>
    /// Gets the expected path to Assets.zip for the specified instance.
    /// </summary>
    private string GetAssetsZipPath(string versionPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return Path.Combine(versionPath, "Client", "Hytale.app", "Contents", "Assets.zip");
        }
        else
        {
            return Path.Combine(versionPath, "Client", "Assets.zip");
        }
    }
    
    /// <summary>
    /// Gets the available cosmetics from the Assets.zip file for the specified instance.
    /// Returns a dictionary where keys are category names and values are lists of cosmetic IDs.
    /// </summary>
    public Dictionary<string, List<string>>? GetCosmeticsList(string versionPath)
    {
        try
        {
            var assetsZipPath = GetAssetsZipPath(versionPath);
            
            if (!File.Exists(assetsZipPath))
            {
                Logger.Warning("Cosmetics", $"Assets.zip not found: {assetsZipPath}");
                return null;
            }
            
            var cosmetics = new Dictionary<string, List<string>>();
            
            using var zip = ZipFile.OpenRead(assetsZipPath);
            
            foreach (var (fileName, categoryName) in CosmeticCategoryMap)
            {
                var entryPath = $"Cosmetics/CharacterCreator/{fileName}";
                var entry = zip.GetEntry(entryPath);
                
                if (entry == null)
                {
                    Logger.Info("Cosmetics", $"Entry not found: {entryPath}");
                    continue;
                }
                
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                
                var items = JsonSerializer.Deserialize<List<CosmeticItem>>(json, JsonOptions);
                if (items != null)
                {
                    var ids = items
                        .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                        .Select(item => item.Id!)
                        .ToList();
                    
                    if (ids.Count > 0)
                    {
                        cosmetics[categoryName] = ids;
                        Logger.Info("Cosmetics", $"Loaded {ids.Count} {categoryName} items");
                    }
                }
            }
            
            Logger.Success("Cosmetics", $"Loaded cosmetics from {assetsZipPath}: {cosmetics.Count} categories");
            return cosmetics;
        }
        catch (Exception ex)
        {
            Logger.Error("Cosmetics", $"Failed to load cosmetics: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Extracts Assets.zip if it exists and hasn't been extracted yet.
    /// </summary>
    public async Task ExtractAssetsIfNeededAsync(string versionPath, Action<int, string> progressCallback)
    {
        // Check if Assets.zip exists
        string assetsZip = Path.Combine(versionPath, "Assets.zip");
        if (!File.Exists(assetsZip))
        {
            Logger.Info("Assets", "No Assets.zip found, skipping extraction");
            progressCallback(100, "No assets extraction needed");
            return;
        }
        
        // Determine target path based on OS
        string assetsDir;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            assetsDir = Path.Combine(versionPath, "Client", "Hytale.app", "Contents", "Assets");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            assetsDir = Path.Combine(versionPath, "Client", "Assets");
        }
        else
        {
            assetsDir = Path.Combine(versionPath, "Client", "Assets");
        }
        
        // Check if already extracted
        if (Directory.Exists(assetsDir) && Directory.GetFiles(assetsDir, "*", SearchOption.AllDirectories).Length > 0)
        {
            Logger.Info("Assets", "Assets already extracted");
            progressCallback(100, "Assets ready");
            return;
        }
        
        Logger.Info("Assets", $"Extracting Assets.zip to {assetsDir}...");
        progressCallback(0, "Extracting game assets...");
        
        try
        {
            Directory.CreateDirectory(assetsDir);
            
            // Extract using ZipFile
            await Task.Run(() =>
            {
                using var archive = ZipFile.OpenRead(assetsZip);
                var totalEntries = archive.Entries.Count;
                var extracted = 0;
                
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    
                    // Get relative path - Assets.zip may have "Assets/" prefix or not
                    var relativePath = entry.FullName;
                    if (relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = relativePath.Substring(7);
                    }
                    else if (relativePath.StartsWith("Assets\\", StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = relativePath.Substring(7);
                    }
                    
                    var destPath = Path.Combine(assetsDir, relativePath);
                    var destDir = Path.GetDirectoryName(destPath);
                    
                    if (!string.IsNullOrEmpty(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }
                    
                    entry.ExtractToFile(destPath, true);
                    extracted++;
                    
                    if (totalEntries > 0 && extracted % 100 == 0)
                    {
                        var progress = (int)((extracted * 100) / totalEntries);
                        progressCallback(progress, $"Extracting assets... {progress}%");
                    }
                }
            });
            
            // Optionally delete the zip after extraction to save space
            try { File.Delete(assetsZip); } catch { }
            
            // On macOS, create symlink at root level for game compatibility
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                string rootAssetsLink = Path.Combine(versionPath, "Assets");
                
                try
                {
                    // Remove existing symlink/directory if it exists
                    if (Directory.Exists(rootAssetsLink) || File.Exists(rootAssetsLink))
                    {
                        try 
                        { 
                            // Check if it's a symlink
                            FileAttributes attrs = File.GetAttributes(rootAssetsLink);
                            if ((attrs & FileAttributes.ReparsePoint) != 0)
                            {
                                // It's a symlink - delete it
                                File.Delete(rootAssetsLink);
                                Logger.Info("Assets", "Removed existing Assets symlink");
                            }
                            else if (Directory.Exists(rootAssetsLink))
                            {
                                // It's a real directory - delete it
                                Directory.Delete(rootAssetsLink, true);
                                Logger.Info("Assets", "Removed existing Assets directory");
                            }
                        } 
                        catch (Exception ex)
                        {
                            Logger.Warning("Assets", $"Could not remove existing Assets: {ex.Message}");
                        }
                    }
                    
                    // Use relative path for symlink so it works even if directory moves
                    string relativeAssetsPath = "Client/Hytale.app/Contents/Assets";
                    
                    // Create symlink using ln command - run from version directory
                    var lnAssets = new ProcessStartInfo("ln", new[] { "-s", relativeAssetsPath, "Assets" })
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        WorkingDirectory = versionPath
                    };
                    var lnProcess = Process.Start(lnAssets);
                    if (lnProcess != null)
                    {
                        string errors = await lnProcess.StandardError.ReadToEndAsync();
                        string output = await lnProcess.StandardOutput.ReadToEndAsync();
                        await lnProcess.WaitForExitAsync();
                        
                        if (lnProcess.ExitCode == 0)
                        {
                            Logger.Success("Assets", $"Created Assets symlink: {rootAssetsLink} -> {relativeAssetsPath}");
                            
                            // Verify the symlink works
                            if (Directory.Exists(rootAssetsLink))
                            {
                                Logger.Success("Assets", "Assets symlink verified - directory is accessible");
                            }
                            else
                            {
                                Logger.Error("Assets", "Assets symlink created but directory not accessible");
                            }
                        }
                        else
                        {
                            Logger.Error("Assets", $"Symlink creation failed with exit code {lnProcess.ExitCode}");
                            if (!string.IsNullOrEmpty(errors))
                            {
                                Logger.Error("Assets", $"Error output: {errors}");
                            }
                            if (!string.IsNullOrEmpty(output))
                            {
                                Logger.Info("Assets", $"Standard output: {output}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Assets", $"Failed to create Assets symlink: {ex.Message}");
                }
            }
            
            Logger.Success("Assets", "Assets extracted successfully");
            progressCallback(100, "Assets extracted");
        }
        catch (Exception ex)
        {
            Logger.Error("Assets", $"Failed to extract assets: {ex.Message}");
            throw;
        }
    }
}
