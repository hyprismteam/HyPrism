using System.Text.Json;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.User;

/// <summary>
/// Provides static helpers for persisting and loading <see cref="HytaleAuthSession"/> data
/// to/from profile-scoped or legacy root-level JSON files.
/// </summary>
internal static class TokenStore
{
    private const string SessionFileName = "hytale_session.json";

    /// <summary>
    /// Resolves the canonical session file path for the current profile.
    /// Falls back to app root when no profile folder is available.
    /// </summary>
    /// <param name="profileFolder">
    /// The resolved path of the active profile folder, or <c>null</c> if no profile is active.
    /// </param>
    /// <param name="appDir">The launcher application data directory.</param>
    /// <returns>Absolute path to the session JSON file.</returns>
    public static string GetSessionFilePath(string? profileFolder, string appDir)
    {
        if (profileFolder != null)
        {
            Directory.CreateDirectory(profileFolder);
            return Path.Combine(profileFolder, SessionFileName);
        }
        return GetLegacySessionFilePath(appDir);
    }

    /// <summary>
    /// Gets the legacy (pre-profile) session file path at the app root.
    /// </summary>
    public static string GetLegacySessionFilePath(string appDir) =>
        Path.Combine(appDir, SessionFileName);

    /// <summary>
    /// Loads a <see cref="HytaleAuthSession"/> from the specified file path.
    /// Returns <c>null</c> when the file does not exist or deserialisation fails.
    /// </summary>
    public static HytaleAuthSession? Load(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<HytaleAuthSession>(json);
        }
        catch (Exception ex)
        {
            Logger.Warning("TokenStore", $"Failed to load session from '{filePath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Persists a <see cref="HytaleAuthSession"/> to the specified file path.
    /// </summary>
    public static void Save(string filePath, HytaleAuthSession session)
    {
        try
        {
            var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            Logger.Warning("TokenStore", $"Failed to save session to '{filePath}': {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes the session file at the specified path (best-effort, ignores errors).
    /// </summary>
    public static void Delete(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (Exception ex)
        {
            Logger.Warning("TokenStore", $"Failed to delete session file '{filePath}': {ex.Message}");
        }
    }
}
