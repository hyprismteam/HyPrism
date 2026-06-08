using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO.Compression;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.Game.Launch;

/// <summary>
/// Patches the HytaleClient binary to replace hytale.com domain references
/// 10 characters (same as hytale.com) for direct replacement to work.
/// Example: hytale.com -> sanasol.ws
/// This allows the game to connect to custom authentication servers.
/// </summary>
public class ClientPatcher : IClientPatcher
{
    private const string OriginalDomain = "hytale.com";
    private const string DefaultNewDomain = "sanasol.ws"; // Must be 10 chars like hytale.com
    private const int MinDomainLength = 4;
    private const int MaxDomainLength = 16;
    private const string PatchedFlag = ".patched_custom";

    private readonly string _targetDomain;

    public ClientPatcher(string? targetDomain = null)
    {
        _targetDomain = targetDomain ?? DefaultNewDomain;

        // Validate domain length
        if (_targetDomain.Length < MinDomainLength || _targetDomain.Length > MaxDomainLength)
        {
            Logger.Warning("Patcher", $"Domain '{_targetDomain}' length {_targetDomain.Length} outside bounds ({MinDomainLength}-{MaxDomainLength}), using default");
            _targetDomain = DefaultNewDomain;
        }
    }

    /// <summary>
    /// Get the flag file path for tracking patch status.
    /// On macOS, stores outside the app bundle to avoid breaking code signature.
    /// </summary>
    private static string GetFlagFilePath(string clientPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Store flag file outside the app bundle
            // e.g., /path/to/Client/Hytale.app/Contents/MacOS/HytaleClient -> /path/to/Client/HytaleClient.patched_custom
            string fileName = Path.GetFileName(clientPath);
            string clientDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(clientPath)!)!)!)!;
            return Path.Combine(clientDir, fileName + PatchedFlag);
        }
        return clientPath + PatchedFlag;
    }

    /// <summary>
    /// Get the backup file path for the original binary.
    /// On macOS, stores outside the app bundle to avoid breaking code signature.
    /// </summary>
    private static string GetBackupFilePath(string clientPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Store backup file outside the app bundle
            string fileName = Path.GetFileName(clientPath);
            string clientDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(clientPath)!)!)!)!;
            return Path.Combine(clientDir, fileName + ".original");
        }
        return clientPath + ".original";
    }

    /// <summary>
    /// Clean up old flag/backup files that were incorrectly placed inside the app bundle.
    /// This is needed for migration from old patching behavior.
    /// </summary>
    private static void CleanupLegacyFiles(string clientPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;

        try
        {
            // Remove old files inside the app bundle
            string oldFlagFile = clientPath + PatchedFlag;
            string oldBackupFile = clientPath + ".original";

            if (File.Exists(oldFlagFile))
            {
                File.Delete(oldFlagFile);
                Logger.Info("Patcher", "Removed legacy flag file from inside app bundle");
            }
            if (File.Exists(oldBackupFile))
            {
                // Move to new location instead of deleting
                string newBackupPath = GetBackupFilePath(clientPath);
                if (!File.Exists(newBackupPath))
                {
                    File.Move(oldBackupFile, newBackupPath);
                    Logger.Info("Patcher", "Moved legacy backup file outside app bundle");
                }
                else
                {
                    File.Delete(oldBackupFile);
                    Logger.Info("Patcher", "Removed duplicate legacy backup file from app bundle");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Patcher", $"Error cleaning up legacy files: {ex.Message}");
        }
    }

    /// <summary>
    /// Determine patching strategy based on domain length
    /// </summary>
    private (string mode, string mainDomain, string subdomainPrefix) GetDomainStrategy()
    {
        if (_targetDomain.Length <= 10)
        {
            // Direct replacement - subdomains will be stripped
            return ("direct", _targetDomain, "");
        }
        else
        {
            // Split mode: first 6 chars become subdomain prefix, rest replaces hytale.com
            string prefix = _targetDomain.Substring(0, 6);
            string suffix = _targetDomain.Substring(6);
            return ("split", suffix, prefix);
        }
    }

    /// <summary>
    /// Convert string to length-prefixed byte format used by .NET AOT compiled binaries.
    /// Format: [length byte] [00 00 00 padding] [char1] [00] [char2] [00] ... [lastChar]
    /// </summary>
    private static byte[] StringToLengthPrefixed(string str)
    {
        var result = new List<byte>();

        // Add length byte and 3 null padding bytes
        result.Add((byte)str.Length);
        result.Add(0);
        result.Add(0);
        result.Add(0);

        // Add UTF-16LE characters (each char is 2 bytes: char byte + 0x00)
        foreach (char c in str)
        {
            result.Add((byte)c);
            result.Add(0);
        }

        return result.ToArray();
    }

    /// <summary>
    /// Convert string to UTF-16LE bytes
    /// </summary>
    private static byte[] StringToUtf16LE(string str)
    {
        return Encoding.Unicode.GetBytes(str);
    }

    /// <summary>
    /// Convert string to UTF-8 bytes (for Java JAR files)
    /// </summary>
    private static byte[] StringToUtf8(string str)
    {
        return Encoding.UTF8.GetBytes(str);
    }

    /// <summary>
    /// Find all occurrences of a pattern in a byte array
    /// </summary>
    private static List<int> FindAllOccurrences(byte[] data, byte[] pattern)
    {
        var positions = new List<int>();
        for (int i = 0; i <= data.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                positions.Add(i);
            }
        }
        return positions;
    }

    /// <summary>
    /// Replace bytes at specified positions
    /// </summary>
    private static int ReplaceBytes(byte[] data, byte[] oldPattern, byte[] newPattern)
    {
        var positions = FindAllOccurrences(data, oldPattern);
        foreach (int pos in positions)
        {
            // Copy the new pattern
            int copyLen = Math.Min(newPattern.Length, oldPattern.Length);
            Array.Copy(newPattern, 0, data, pos, copyLen);
            
            // If new pattern is shorter, null out the remaining bytes
            // This ensures no leftover data from the old pattern
            if (newPattern.Length < oldPattern.Length)
            {
                for (int i = newPattern.Length; i < oldPattern.Length; i++)
                {
                    data[pos + i] = 0;
                }
            }
        }
        return positions.Count;
    }

    /// <summary>
    /// Check if client is already patched for this domain
    /// </summary>
    public bool IsPatchedAlready(string clientPath)
    {
        string flagFile = GetFlagFilePath(clientPath);
        
        // Also check legacy location for migration purposes
        string legacyFlagFile = clientPath + PatchedFlag;
        string actualFlagFile = File.Exists(flagFile) ? flagFile : (File.Exists(legacyFlagFile) ? legacyFlagFile : null!);
        
        if (actualFlagFile == null)
        {
            return false;
        }

        try
        {
            string flagContent = File.ReadAllText(actualFlagFile);
            var flagData = JsonSerializer.Deserialize<Dictionary<string, object>>(flagContent);
            if (flagData != null && flagData.TryGetValue("targetDomain", out var targetDomainObj))
            {
                string? savedDomain = targetDomainObj?.ToString();
                if (savedDomain == _targetDomain)
                {
                    // Verify the binary actually contains the patched domain
                    byte[] data = File.ReadAllBytes(clientPath);
                    var (_, mainDomain, _) = GetDomainStrategy();
                    byte[] pattern = StringToLengthPrefixed(mainDomain);

                    if (FindAllOccurrences(data, pattern).Count > 0)
                    {
                        return true;
                    }
                    else
                    {
                        Logger.Info("Patcher", "Flag exists but binary not patched (was updated?), re-patching...");
                        return false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Patcher", $"Error reading patch flag: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Mark client as patched
    /// </summary>
    private void MarkAsPatched(string clientPath)
    {
        string flagFile = GetFlagFilePath(clientPath);
        var (mode, mainDomain, subdomainPrefix) = GetDomainStrategy();

        var flagData = new Dictionary<string, object>
        {
            ["patchedAt"] = DateTime.UtcNow.ToString("o"),
            ["originalDomain"] = OriginalDomain,
            ["targetDomain"] = _targetDomain,
            ["patchMode"] = mode,
            ["mainDomain"] = mainDomain,
            ["subdomainPrefix"] = subdomainPrefix,
            ["patcherVersion"] = "1.2.0"
        };

        File.WriteAllText(flagFile, JsonSerializer.Serialize(flagData, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Create a backup of the original client binary.
    /// On macOS, stores outside the app bundle to preserve code signature.
    /// </summary>
    private static void BackupClient(string clientPath)
    {
        string backupPath = GetBackupFilePath(clientPath);
        if (!File.Exists(backupPath))
        {
            File.Copy(clientPath, backupPath);
            Logger.Info("Patcher", $"Created backup at {backupPath}");
        }
    }

    /// <summary>
    /// Apply all domain patches using length-prefixed format
    /// </summary>
    private int ApplyDomainPatches(byte[] data, string protocol = "https://")
    {
        int totalCount = 0;
        var (mode, mainDomain, subdomainPrefix) = GetDomainStrategy();

        Logger.Info("Patcher", $"Patching strategy: {mode} mode", false);
        if (mode == "split")
        {
            Logger.Info("Patcher", $"  Subdomain prefix: {subdomainPrefix}", false);
            Logger.Info("Patcher", $"  Main domain: {mainDomain}", false);
        }

        // 1. Patch telemetry/sentry URL (optional, reduces telemetry)
        string oldSentry = "https://ca900df42fcf57d4dd8401a86ddd7da2@sentry.hytale.com/2";
        string newSentry = $"{protocol}t@{_targetDomain}/2";

        byte[] oldSentryBytes = StringToLengthPrefixed(oldSentry);
        byte[] newSentryBytes = StringToLengthPrefixed(newSentry);
        int sentryCount = ReplaceBytes(data, oldSentryBytes, newSentryBytes);
        if (sentryCount > 0)
        {
            Logger.Info("Patcher", $"  Patched {sentryCount} sentry occurrence(s)");
            totalCount += sentryCount;
        }

        // 2. Patch main domain (hytale.com -> mainDomain)
        Logger.Info("Patcher", $"  Patching domain: {OriginalDomain} -> {mainDomain}");
        byte[] oldDomainBytes = StringToLengthPrefixed(OriginalDomain);
        byte[] newDomainBytes = StringToLengthPrefixed(mainDomain);
        int domainCount = ReplaceBytes(data, oldDomainBytes, newDomainBytes);
        if (domainCount > 0)
        {
            Logger.Info("Patcher", $"  Patched {domainCount} domain occurrence(s)");
            totalCount += domainCount;
        }

        // 3. Patch subdomain prefixes (only in split mode)
        // In direct mode, we only replace hytale.com -> sanasol.ws
        // The subdomains like sessions., account-data., etc. stay intact
        if (mode == "split" && !string.IsNullOrEmpty(subdomainPrefix))
        {
            string[] subdomains = { "https://tools.", "https://sessions.", "https://account-data.", "https://telemetry." };
            string newSubdomainPrefix = protocol + subdomainPrefix;

            foreach (string sub in subdomains)
            {
                byte[] oldSubBytes = StringToLengthPrefixed(sub);
                byte[] newSubBytes = StringToLengthPrefixed(newSubdomainPrefix);
                int subCount = ReplaceBytes(data, oldSubBytes, newSubBytes);
                if (subCount > 0)
                {
                    Logger.Info("Patcher", $"  Patched {subCount} {sub} occurrence(s)");
                    totalCount += subCount;
                }
            }
        }

        return totalCount;
    }

    /// <summary>
    /// Patch Discord invite URL (optional)
    /// </summary>
    private static int PatchDiscordUrl(byte[] data)
    {
        string oldUrl = ".gg/hytale";
        string newUrl = ".gg/MHkEjepMQ7"; // HyPrism Discord

        // Try length-prefixed format
        byte[] oldBytes = StringToLengthPrefixed(oldUrl);
        byte[] newBytes = StringToLengthPrefixed(newUrl);
        int count = ReplaceBytes(data, oldBytes, newBytes);

        if (count == 0)
        {
            // Fallback to UTF-16LE
            byte[] oldUtf16 = StringToUtf16LE(oldUrl);
            byte[] newUtf16 = StringToUtf16LE(newUrl);
            count = ReplaceBytes(data, oldUtf16, newUtf16);
        }

        return count;
    }

    /// <summary>
    /// Patch the client binary to use custom domain
    /// </summary>
    public PatchResult PatchClient(string clientPath, Action<string, int?>? progressCallback = null)
    {
        var (_, mainDomain, _) = GetDomainStrategy();

        Logger.Info("Patcher", "=== Client Patcher v1.0 ===", false);
        Logger.Info("Patcher", $"Target: {clientPath}", false);
        Logger.Info("Patcher", $"Domain: {_targetDomain} ({_targetDomain.Length} chars)", false);

        if (!File.Exists(clientPath))
        {
            string error = $"Client binary not found: {clientPath}";
            Logger.Error("Patcher", error);
            return new PatchResult { Success = false, Error = error };
        }

        // Clean up legacy flag/backup files that were incorrectly placed inside the app bundle
        CleanupLegacyFiles(clientPath);

        if (IsPatchedAlready(clientPath))
        {
            Logger.Info("Patcher", $"Client already patched for {_targetDomain}, skipping", false);
            progressCallback?.Invoke("launch.detail.client_already_patched", 100);
            return new PatchResult { Success = true, AlreadyPatched = true, PatchCount = 0 };
        }

        progressCallback?.Invoke("launch.detail.reading_client_binary", 10);
        Logger.Info("Patcher", "Reading client binary...", false);
        byte[] data = File.ReadAllBytes(clientPath);
        Logger.Info("Patcher", $"Binary size: {data.Length / 1024.0 / 1024.0:F2} MB", false);

        progressCallback?.Invoke("launch.detail.patching_domain_refs", 30);
        Logger.Info("Patcher", "Applying domain patches (length-prefixed format)...", false);
        int domainCount = ApplyDomainPatches(data);

        Logger.Info("Patcher", "Patching Discord URLs...", false);
        int discordCount = PatchDiscordUrl(data);

        int totalCount = domainCount + discordCount;

        if (totalCount == 0)
        {
            Logger.Warning("Patcher", "No occurrences found with length-prefixed format - trying UTF-8...", false); // Silent warning

            // Try UTF-8 format (most common for native binaries)
            byte[] oldDomainUtf8 = StringToUtf8(OriginalDomain);
            byte[] newDomainUtf8 = StringToUtf8(mainDomain);
            int utf8Count = ReplaceBytes(data, oldDomainUtf8, newDomainUtf8);

            if (utf8Count > 0)
            {
                Logger.Info("Patcher", $"Found {utf8Count} occurrences with UTF-8 format", false);
                
                // Also patch common URL patterns with UTF-8
                string[] urlPatterns = {
                    "sessions.hytale.com",
                    "tools.hytale.com", 
                    "account-data.hytale.com",
                    "telemetry.hytale.com",
                    "api.hytale.com"
                };
                
                foreach (var pattern in urlPatterns)
                {
                    byte[] oldUrl = StringToUtf8(pattern);
                    // Replace subdomain with our target (e.g., sessions.hytale.com -> sessions.sanasol.ws)
                    string newPattern = pattern.Replace("hytale.com", mainDomain);
                    byte[] newUrl = StringToUtf8(newPattern);
                    int urlCount = ReplaceBytes(data, oldUrl, newUrl);
                    if (urlCount > 0)
                    {
                        Logger.Info("Patcher", $"  Patched {urlCount} {pattern} occurrence(s)", false);
                        utf8Count += urlCount;
                    }
                }
                
                Logger.Info("Patcher", "Creating backup before writing...", false);
                BackupClient(clientPath);
                File.WriteAllBytes(clientPath, data);
                MarkAsPatched(clientPath);
                progressCallback?.Invoke("launch.detail.patching_complete", 100);
                return new PatchResult { Success = true, PatchCount = utf8Count };
            }

            Logger.Warning("Patcher", "No UTF-8 occurrences - trying legacy UTF-16LE format...", false);

            // Fallback to direct UTF-16LE replacement
            // IMPORTANT: The base domain (4th occurrence) has 0x89 instead of 0x00 after the last char
            // So we search for the pattern WITHOUT the final high byte (first 19 of 20 bytes)
            
            // First, try the full UTF-16LE pattern (catches 3 URL occurrences)
            byte[] oldDomain = StringToUtf16LE(OriginalDomain);
            byte[] newDomain = StringToUtf16LE(mainDomain);
            int legacyCount = ReplaceBytes(data, oldDomain, newDomain);
            Logger.Info("Patcher", $"Full UTF-16LE pattern: found {legacyCount} occurrences", false);
            
            // Now search for partial pattern (19 bytes) to catch the base domain with 0x89 suffix
            // This catches: h.y.t.a.l.e...c.o.m (without the trailing 00)
            // Pattern: 68 00 79 00 74 00 61 00 6c 00 65 00 2e 00 63 00 6f 00 6d
            byte[] oldDomainPartial = new byte[OriginalDomain.Length * 2 - 1];  // 19 bytes
            byte[] newDomainPartial = new byte[mainDomain.Length * 2 - 1];
            
            for (int i = 0; i < OriginalDomain.Length; i++)
            {
                oldDomainPartial[i * 2] = (byte)OriginalDomain[i];
                if (i * 2 + 1 < oldDomainPartial.Length)
                    oldDomainPartial[i * 2 + 1] = 0;
            }
            
            for (int i = 0; i < mainDomain.Length; i++)
            {
                newDomainPartial[i * 2] = (byte)mainDomain[i];
                if (i * 2 + 1 < newDomainPartial.Length)
                    newDomainPartial[i * 2 + 1] = 0;
            }
            
            // Find positions with the partial pattern
            var partialPositions = FindAllOccurrences(data, oldDomainPartial);
            Logger.Info("Patcher", $"Partial UTF-16LE pattern (19 bytes): found {partialPositions.Count} occurrences");
            
            // Only replace those that weren't already replaced (check if byte after is NOT 00)
            int additionalCount = 0;
            foreach (int pos in partialPositions)
            {
                // Check the byte after the match
                int afterPos = pos + oldDomainPartial.Length;
                if (afterPos < data.Length && data[afterPos] != 0x00)
                {
                    // This is a non-standard occurrence (like the base domain with 0x89)
                    Array.Copy(newDomainPartial, 0, data, pos, newDomainPartial.Length);
                    additionalCount++;
                    Logger.Info("Patcher", $"  Patched base domain at offset {pos} (byte after: 0x{data[afterPos]:X2})");
                }
            }
            
            legacyCount += additionalCount;

            if (legacyCount > 0)
            {
                Logger.Info("Patcher", $"Found {legacyCount} occurrences with UTF-16LE format");
                Logger.Info("Patcher", "Creating backup before writing...");
                BackupClient(clientPath);
                File.WriteAllBytes(clientPath, data);
                MarkAsPatched(clientPath);
                progressCallback?.Invoke("launch.detail.patching_complete", 100);
                return new PatchResult { Success = true, PatchCount = legacyCount };
            }

            Logger.Warning("Patcher", "No occurrences found in any format - binary may already be patched or uses unknown encoding");
            // Don't write anything, don't backup - keep the original intact
            return new PatchResult { Success = true, PatchCount = 0, Warning = "No occurrences found - may already be patched" };
        }

        progressCallback?.Invoke("launch.detail.creating_backup", 70);
        Logger.Info("Patcher", "Creating backup before writing...");
        BackupClient(clientPath);
        
        progressCallback?.Invoke("launch.detail.writing_patched_binary", 80);
        Logger.Info("Patcher", "Writing patched binary...");
        File.WriteAllBytes(clientPath, data);

        MarkAsPatched(clientPath);

        progressCallback?.Invoke("launch.detail.patching_complete", 100);
        Logger.Success("Patcher", $"Successfully patched {totalCount} occurrences");
        Logger.Info("Patcher", "=== Patching Complete ===");

        return new PatchResult { Success = true, PatchCount = totalCount };
    }

    /// <summary>
    /// Find the client binary path based on platform
    /// </summary>
    public static string? FindClientPath(string gameDir)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string path = Path.Combine(gameDir, "Client", "Hytale.app", "Contents", "MacOS", "HytaleClient");
            return File.Exists(path) ? path : null;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string path = Path.Combine(gameDir, "Client", "HytaleClient.exe");
            return File.Exists(path) ? path : null;
        }
        else
        {
            // Linux
            string path = Path.Combine(gameDir, "Client", "HytaleClient");
            return File.Exists(path) ? path : null;
        }
    }

    /// <summary>
    /// Ensure client is patched before launching
    /// </summary>
    public PatchResult EnsureClientPatched(string gameDir, Action<string, int?>? progressCallback = null)
    {
        string? clientPath = FindClientPath(gameDir);
        if (clientPath == null)
        {
            return new PatchResult { Success = false, Error = "Client binary not found" };
        }

        return PatchClient(clientPath, progressCallback);
    }

    /// <summary>
    /// Check if the client binary in the given game directory is currently patched (any domain).
    /// </summary>
    public static bool IsClientPatched(string gameDir)
    {
        string? clientPath = FindClientPath(gameDir);
        if (clientPath == null) return false;

        string flagFile = GetFlagFilePath(clientPath);
        string legacyFlagFile = clientPath + PatchedFlag;
        return File.Exists(flagFile) || File.Exists(legacyFlagFile);
    }

    /// <summary>
    /// Check if the server JAR in the given game directory is currently patched.
    /// </summary>
    public static bool IsServerJarPatched(string gameDir)
    {
        string serverJarPath = Path.Combine(gameDir, "Server", "HytaleServer.jar");
        string patchFlag = serverJarPath + PatchedFlag;
        return File.Exists(patchFlag);
    }

    /// <summary>
    /// Restore the client binary from its .original backup, removing the patch.
    /// Used when switching to official servers where no patching is needed.
    /// </summary>
    public static PatchResult RestoreClientFromBackup(string gameDir, Action<string, int?>? progressCallback = null)
    {
        string? clientPath = FindClientPath(gameDir);
        if (clientPath == null)
        {
            Logger.Info("Patcher", "Client binary not found — nothing to restore");
            return new PatchResult { Success = true, PatchCount = 0 };
        }

        string backupPath = GetBackupFilePath(clientPath);
        string flagFile = GetFlagFilePath(clientPath);

        if (!File.Exists(backupPath))
        {
            Logger.Info("Patcher", "No client backup found — binary is likely already original");
            // Clean up flag file if it exists without a backup (shouldn't happen, but be safe)
            if (File.Exists(flagFile)) File.Delete(flagFile);
            return new PatchResult { Success = true, PatchCount = 0 };
        }

        try
        {
            progressCallback?.Invoke("launch.detail.restoring_client", 20);
            Logger.Info("Patcher", $"Restoring original client binary from {backupPath}");

            File.Copy(backupPath, clientPath, overwrite: true);

            // Remove the patch flag so the binary is considered unpatched
            if (File.Exists(flagFile)) File.Delete(flagFile);

            // Also clean up legacy flag file
            string legacyFlag = clientPath + PatchedFlag;
            if (legacyFlag != flagFile && File.Exists(legacyFlag)) File.Delete(legacyFlag);

            progressCallback?.Invoke("launch.detail.client_restored", 100);
            Logger.Success("Patcher", "Client binary restored to original (unpatched) state");

            return new PatchResult { Success = true, PatchCount = 0 };
        }
        catch (Exception ex)
        {
            Logger.Error("Patcher", $"Failed to restore client from backup: {ex.Message}");
            return new PatchResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Restore the server JAR from its .original backup, removing the patch.
    /// </summary>
    public static PatchResult RestoreServerJarFromBackup(string gameDir, Action<string, int?>? progressCallback = null)
    {
        string serverJarPath = Path.Combine(gameDir, "Server", "HytaleServer.jar");
        string backupPath = serverJarPath + ".original";
        string patchFlag = serverJarPath + PatchedFlag;

        if (!File.Exists(backupPath))
        {
            Logger.Info("Patcher", "No server JAR backup found — JAR is likely already original");
            if (File.Exists(patchFlag)) File.Delete(patchFlag);
            return new PatchResult { Success = true, PatchCount = 0 };
        }

        try
        {
            progressCallback?.Invoke("launch.detail.restoring_server", 20);
            Logger.Info("Patcher", $"Restoring original server JAR from {backupPath}");

            File.Copy(backupPath, serverJarPath, overwrite: true);

            if (File.Exists(patchFlag)) File.Delete(patchFlag);

            progressCallback?.Invoke("launch.detail.server_restored", 100);
            Logger.Success("Patcher", "Server JAR restored to original (unpatched) state");

            return new PatchResult { Success = true, PatchCount = 0 };
        }
        catch (Exception ex)
        {
            Logger.Error("Patcher", $"Failed to restore server JAR from backup: {ex.Message}");
            return new PatchResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Restore both client and server JAR from backups.
    /// Used when switching to official servers.
    /// </summary>
    public static PatchResult RestoreAllFromBackup(string gameDir, Action<string, int?>? progressCallback = null)
    {
        Logger.Info("Patcher", "=== Restoring originals (official mode) ===");

        var clientResult = RestoreClientFromBackup(gameDir, progressCallback);
        if (!clientResult.Success) return clientResult;

        // Re-sign on macOS after restoring the original binary
        if (clientResult.PatchCount == 0 && RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string? clientPath = FindClientPath(gameDir);
            if (clientPath != null && File.Exists(GetBackupFilePath(clientPath)))
            {
                // Backup existed, so we actually restored — re-sign the app
                string appBundle = Path.Combine(gameDir, "Client", "Hytale.app");
                if (Directory.Exists(appBundle))
                {
                    SignMacOSBinary(appBundle);
                }
            }
        }

        var serverResult = RestoreServerJarFromBackup(gameDir, progressCallback);
        if (!serverResult.Success) return serverResult;

        Logger.Info("Patcher", "=== Restore complete ===");
        return new PatchResult { Success = true, PatchCount = 0 };
    }

    /// <summary>
    /// Sign macOS binary after patching (ad-hoc signature)
    /// </summary>
    public static bool SignMacOSBinary(string binaryPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return true;
        }

        try
        {
            Logger.Info("Patcher", "Signing macOS binary with ad-hoc signature...");

            var signProcess = new ProcessStartInfo
            {
                FileName = "/usr/bin/codesign",
                Arguments = $"--force --deep --sign - \"{binaryPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var process = Process.Start(signProcess);
            if (process == null)
            {
                Logger.Error("Patcher", "Failed to start codesign process");
                return false;
            }

            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                Logger.Success("Patcher", "Binary signed successfully");
                return true;
            }
            else
            {
                string stderr = process.StandardError.ReadToEnd();
                Logger.Warning("Patcher", $"codesign returned {process.ExitCode}: {stderr}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Patcher", $"Failed to sign binary: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Patch the HytaleServer.jar to use custom auth domain.
    /// The server JAR contains sessions.hytale.com which needs to be changed to sessions.sanasol.ws
    /// for JWT validation to work with the custom auth server.
    /// JAR files are ZIP archives with compressed class files, so we need to extract, patch, and re-archive.
    /// </summary>
    public PatchResult PatchServerJar(string gameDir, Action<string, int?>? progressCallback = null)
    {
        var (_, mainDomain, _) = GetDomainStrategy();
        string serverJarPath = Path.Combine(gameDir, "Server", "HytaleServer.jar");
        string patchFlag = serverJarPath + PatchedFlag;

        Logger.Info("Patcher", "=== Server JAR Patcher v1.0 ===", false);
        Logger.Info("Patcher", $"Target: {serverJarPath}", false);
        Logger.Info("Patcher", $"Domain: {_targetDomain}", false);

        if (!File.Exists(serverJarPath))
        {
            string error = $"Server JAR not found: {serverJarPath}";
            Logger.Warning("Patcher", error, true);
            return new PatchResult { Success = true, PatchCount = 0, Warning = error };
        }

        // Define the patterns to patch
        string oldSessionsUrl = "sessions.hytale.com";
        string newSessionsUrl = $"sessions.{mainDomain}";
        
        // Ensure replacement is same length (required for class file patching)
        if (newSessionsUrl.Length != oldSessionsUrl.Length)
        {
            // Pad or truncate to match length
            if (newSessionsUrl.Length < oldSessionsUrl.Length)
            {
                // This shouldn't happen with sanasol.ws (20 chars each)
                Logger.Warning("Patcher", $"New URL is shorter, padding with nulls");
            }
            else
            {
                string error = $"New sessions URL '{newSessionsUrl}' ({newSessionsUrl.Length}) is longer than original '{oldSessionsUrl}' ({oldSessionsUrl.Length}) - cannot patch safely";
                Logger.Error("Patcher", error);
                return new PatchResult { Success = false, Error = error };
            }
        }

        // Check if already patched by looking for the patched URL in the JAR
        if (File.Exists(patchFlag))
        {
            try
            {
                string flagContent = File.ReadAllText(patchFlag);
                var flagData = JsonSerializer.Deserialize<Dictionary<string, object>>(flagContent);
                if (flagData != null && flagData.TryGetValue("targetDomain", out var targetDomainObj))
                {
                    string? savedDomain = targetDomainObj?.ToString();
                    if (savedDomain == _targetDomain)
                    {
                        // Verify by checking if the new URL exists in any class file
                        bool foundPatched = false;
                        using (var archive = ZipFile.OpenRead(serverJarPath))
                        {
                            foreach (var entry in archive.Entries)
                            {
                                if (!entry.FullName.EndsWith(".class")) continue;
                                
                                using var stream = entry.Open();
                                using var ms = new MemoryStream();
                                stream.CopyTo(ms);
                                byte[] classData = ms.ToArray();
                                
                                byte[] patchedPattern = StringToUtf8(newSessionsUrl);
                                if (FindAllOccurrences(classData, patchedPattern).Count > 0)
                                {
                                    foundPatched = true;
                                    break;
                                }
                            }
                        }
                        
                        if (foundPatched)
                        {
                            Logger.Info("Patcher", $"Server JAR already patched for {_targetDomain}, skipping", false);
                            progressCallback?.Invoke("launch.detail.server_already_patched", 100);
                            return new PatchResult { Success = true, AlreadyPatched = true, PatchCount = 0 };
                        }
                        else
                        {
                            Logger.Info("Patcher", "Flag exists but JAR not patched (was updated?), re-patching...");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Patcher", $"Error reading server patch flag: {ex.Message}");
            }
        }

        progressCallback?.Invoke("launch.detail.reading_server_jar", 10);
        Logger.Info("Patcher", "Extracting and patching server JAR (ZIP archive)...");
        
        // Create backup first
        string backupPath = serverJarPath + ".original";
        if (!File.Exists(backupPath))
        {
            File.Copy(serverJarPath, backupPath);
            Logger.Info("Patcher", $"Created backup at {backupPath}");
        }
        
        // Create a temporary path for the new JAR
        string tempJarPath = serverJarPath + ".patching";
        int totalPatched = 0;
        
        try
        {
            progressCallback?.Invoke("launch.detail.patching_class_files", 30);
            
            // Open the existing JAR and create a new one with patched content
            using (var sourceArchive = ZipFile.OpenRead(serverJarPath))
            using (var destArchive = ZipFile.Open(tempJarPath, ZipArchiveMode.Create))
            {
                byte[] oldUrlBytes = StringToUtf8(oldSessionsUrl);
                byte[] newUrlBytes = StringToUtf8(newSessionsUrl);
                
                // Also patch the full URL
                string oldFullUrl = "https://sessions.hytale.com";
                string newFullUrl = $"https://sessions.{mainDomain}";
                byte[] oldFullBytes = StringToUtf8(oldFullUrl);
                byte[] newFullBytes = StringToUtf8(newFullUrl);
                
                int entryCount = sourceArchive.Entries.Count;
                int processed = 0;
                
                foreach (var entry in sourceArchive.Entries)
                {
                    processed++;
                    if (processed % 1000 == 0)
                    {
                        int progress = 30 + (int)(50.0 * processed / entryCount);
                        progressCallback?.Invoke($"Processing {processed}/{entryCount}...", progress);
                    }
                    
                    // Create the same entry in destination
                    var destEntry = destArchive.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                    
                    using var sourceStream = entry.Open();
                    using var destStream = destEntry.Open();
                    
                    if (entry.FullName.EndsWith(".class"))
                    {
                        // Read class file content
                        using var ms = new MemoryStream();
                        sourceStream.CopyTo(ms);
                        byte[] classData = ms.ToArray();
                        
                        // Patch both URL patterns
                        int count1 = ReplaceBytes(classData, oldUrlBytes, newUrlBytes);
                        int count2 = ReplaceBytes(classData, oldFullBytes, newFullBytes);
                        
                        if (count1 > 0 || count2 > 0)
                        {
                            totalPatched += count1 + count2;
                            Logger.Info("Patcher", $"  Patched {count1 + count2} occurrence(s) in {entry.FullName}");
                        }
                        
                        // Write patched content
                        destStream.Write(classData, 0, classData.Length);
                    }
                    else
                    {
                        // Just copy the file as-is
                        sourceStream.CopyTo(destStream);
                    }
                }
            }
            
            progressCallback?.Invoke("launch.detail.replacing_original_jar", 80);
            
            // Replace the original JAR with the patched one
            File.Delete(serverJarPath);
            File.Move(tempJarPath, serverJarPath);
            
            Logger.Info("Patcher", $"Total occurrences patched: {totalPatched}");
        }
        catch (Exception ex)
        {
            // Clean up temp file if it exists
            if (File.Exists(tempJarPath))
            {
                try { File.Delete(tempJarPath); } catch { }
            }
            
            Logger.Error("Patcher", $"Error patching server JAR: {ex.Message}");
            return new PatchResult { Success = false, Error = ex.Message };
        }

        if (totalPatched == 0)
        {
            Logger.Warning("Patcher", "No sessions.hytale.com occurrences found in server JAR class files");
            return new PatchResult { Success = true, PatchCount = 0, Warning = "No occurrences found - may already be patched" };
        }

        // Write patch flag
        var flagData2 = new Dictionary<string, object>
        {
            ["patchedAt"] = DateTime.UtcNow.ToString("o"),
            ["originalDomain"] = oldSessionsUrl,
            ["targetDomain"] = _targetDomain,
            ["patchCount"] = totalPatched,
            ["patcherVersion"] = "1.0.0"
        };
        File.WriteAllText(patchFlag, JsonSerializer.Serialize(flagData2, new JsonSerializerOptions { WriteIndented = true }));

        progressCallback?.Invoke("launch.detail.server_jar_patched", 100);
        Logger.Success("Patcher", $"Successfully patched {totalPatched} occurrences in server JAR");
        
        return new PatchResult { Success = true, PatchCount = totalPatched };
    }

    /// <summary>
    /// Ensure both client and server are patched
    /// </summary>
    public PatchResult EnsureAllPatched(string gameDir, Action<string, int?>? progressCallback = null)
    {
        // Patch client first
        var clientResult = EnsureClientPatched(gameDir, progressCallback);
        if (!clientResult.Success)
        {
            return clientResult;
        }

        // Then patch server JAR
        var serverResult = PatchServerJar(gameDir, progressCallback);
        if (!serverResult.Success)
        {
            return serverResult;
        }

        return new PatchResult
        {
            Success = true,
            PatchCount = clientResult.PatchCount + serverResult.PatchCount,
            AlreadyPatched = clientResult.AlreadyPatched && serverResult.AlreadyPatched
        };
    }
}

/// <summary>
/// Result of a patching operation
/// </summary>
public class PatchResult
{
    public bool Success { get; set; }
    public bool AlreadyPatched { get; set; }
    public int PatchCount { get; set; }
    public string? Error { get; set; }
    public string? Warning { get; set; }
}
