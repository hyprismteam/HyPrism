namespace HyPrism.Services.Game.Asset;

/// <summary>
/// Manages game asset files including Assets.zip extraction and cosmetic item parsing.
/// </summary>
public interface IAssetService
{
    /// <summary>
    /// Checks if Assets.zip exists for the specified instance.
    /// </summary>
    bool HasAssetsZip(string versionPath);

    /// <summary>
    /// Gets the path to Assets.zip if it exists.
    /// </summary>
    string? GetAssetsZipPathIfExists(string versionPath);

    /// <summary>
    /// Gets the available cosmetics from the Assets.zip file.
    /// </summary>
    Dictionary<string, List<string>>? GetCosmeticsList(string versionPath);

    /// <summary>
    /// Extracts Assets.zip if it exists and hasn't been extracted yet.
    /// </summary>
    Task ExtractAssetsIfNeededAsync(string versionPath, Action<int, string> progressCallback);
}
