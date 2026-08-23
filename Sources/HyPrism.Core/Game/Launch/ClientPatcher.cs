// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Diagnostics;
using HyPrism.Core.Infrastructure;

namespace HyPrism.Core.Game.Launch;

/// <summary>
/// Patches the HytaleClient binary to replace hytale.com domain references
/// 10 characters (same as hytale.com) for direct replacement to work.
/// Example: hytale.com -> sanasol.ws
/// This allows the game to connect to custom authentication servers
/// </summary>
public class ClientPatcher
{
    private const string OriginalDomain = "hytale.com";
    private const string DefaultNewDomain = "sanasol.ws";
    private const int MinDomainLength = 4;
    private const int MaxDomainLength = 16;
    private const string PatchedFlag = ".patched_custom";

    private readonly string _targetDomain;

    /// <summary>
    /// Creates a client patcher for the selected authentication domain
    /// </summary>
    /// <param name="targetDomain">The replacement domain, or <see langword="null"/> to use the default</param>
    public ClientPatcher(string? targetDomain = null)
    {
        _targetDomain = targetDomain ?? DefaultNewDomain;

        if (_targetDomain.Length < MinDomainLength || _targetDomain.Length > MaxDomainLength)
        {
            Logger.Warning("Patcher", $"Domain '{_targetDomain}' length {_targetDomain.Length} outside bounds ({MinDomainLength}-{MaxDomainLength}), using default");
            _targetDomain = DefaultNewDomain;
        }
    }

    /// <summary>
    /// Get the flag file path for tracking patch status.
    /// On macOS, stores outside the app bundle to avoid breaking code signature
    /// </summary>
    private static string GetFlagFilePath(string clientPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string fileName = Path.GetFileName(clientPath);
            string clientDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(clientPath)!)!)!)!;
            return Path.Combine(clientDir, fileName + PatchedFlag);
        }
        return clientPath + PatchedFlag;
    }

    /// <summary>
    /// Get the backup file path for the original binary.
    /// On macOS, stores outside the app bundle to avoid breaking code signature
    /// </summary>
    private static string GetBackupFilePath(string clientPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string fileName = Path.GetFileName(clientPath);
            string clientDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(clientPath)!)!)!)!;
            return Path.Combine(clientDir, fileName + ".original");
        }
        return clientPath + ".original";
    }

    /// <summary>
    /// Clean up old flag/backup files that were incorrectly placed inside the app bundle.
    /// This is needed for migration from old patching behavior
    /// </summary>
    private static void CleanupLegacyFiles(string clientPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;

        try
        {
            string oldFlagFile = clientPath + PatchedFlag;
            string oldBackupFile = clientPath + ".original";

            if (File.Exists(oldFlagFile))
            {
                File.Delete(oldFlagFile);
                Logger.Info("Patcher", "Removed legacy flag file from inside app bundle");
            }
            if (File.Exists(oldBackupFile))
            {
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
            return ("direct", _targetDomain, "");
        }
        else
        {
            // Split mode stores six characters in the subdomain and ten in the base domain
            string prefix = _targetDomain[..6];
            string suffix = _targetDomain[6..];
            return ("split", suffix, prefix);
        }
    }

    /// <summary>
    /// Convert string to length-prefixed byte format used by .NET AOT compiled binaries.
    /// Format: [length byte] [00 00 00 padding] [char1] [00] [char2] [00] ... [lastChar]
    /// </summary>
    private static byte[] StringToLengthPrefixed(string str)
    {
        var result = new List<byte>
        {
            (byte)str.Length,
            0,
            0,
            0
        };

        foreach (char c in str)
        {
            result.Add((byte)c);
            result.Add(0);
        }

        return [.. result];
    }

    /// <summary>
    /// Builds the searchable part of a NativeAOT frozen string
    /// </summary>
    private static byte[] StringToFrozenPattern(string value)
    {
        var utf16 = StringToUtf16LE(value);
        var result = new byte[4 + utf16.Length - 1];
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0, 4), value.Length);
        utf16.AsSpan(0, utf16.Length - 1).CopyTo(result.AsSpan(4));
        return result;
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
            int copyLen = Math.Min(newPattern.Length, oldPattern.Length);
            Array.Copy(newPattern, 0, data, pos, copyLen);

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
    /// Replaces NativeAOT frozen strings while preserving the metadata byte that
    /// immediately follows the low byte of the final ASCII character
    /// </summary>
    private static int ReplaceFrozenString(byte[] data, string oldValue, string newValue)
    {
        if (newValue.Length > oldValue.Length)
            return 0;

        var oldPattern = StringToFrozenPattern(oldValue);
        var positions = FindAllOccurrences(data, oldPattern);
        var newUtf16 = StringToUtf16LE(newValue);
        foreach (var position in positions)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(position, 4), newValue.Length);

            var payload = data.AsSpan(position + 4, oldValue.Length * 2 - 1);
            payload.Clear();
            var writableLength = newValue.Length == oldValue.Length
                ? newUtf16.Length - 1
                : newUtf16.Length;
            newUtf16.AsSpan(0, writableLength).CopyTo(payload);
        }

        return positions.Count;
    }

    private static bool ContainsPatchedString(byte[] data, string value)
        => FindAllOccurrences(data, StringToLengthPrefixed(value)).Count > 0
            || FindAllOccurrences(data, StringToFrozenPattern(value)).Count > 0;

    /// <summary>
    /// Check if client is already patched for this domain
    /// </summary>
    public bool IsPatchedAlready(string clientPath)
    {
        string flagFile = GetFlagFilePath(clientPath);

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
                    byte[] data = File.ReadAllBytes(clientPath);
                    var (mode, mainDomain, subdomainPrefix) = GetDomainStrategy();
                    var hasDomain = ContainsPatchedString(data, mainDomain);
                    var hasSplitPrefix = mode != "split"
                        || ContainsPatchedString(data, "https://" + subdomainPrefix);
                    if (hasDomain && hasSplitPrefix)
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

        File.WriteAllText(flagFile, JsonSerializer.Serialize(flagData, JsonDefaults.Indented));
    }

    /// <summary>
    /// Create a backup of the original client binary.
    /// On macOS, stores outside the app bundle to preserve code signature
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

        Logger.Info("Patcher", $"  Patching domain: {OriginalDomain} -> {mainDomain}");
        byte[] oldDomainBytes = StringToLengthPrefixed(OriginalDomain);
        byte[] newDomainBytes = StringToLengthPrefixed(mainDomain);
        int domainCount = ReplaceBytes(data, oldDomainBytes, newDomainBytes)
            + ReplaceFrozenString(data, OriginalDomain, mainDomain);
        if (domainCount > 0)
        {
            Logger.Info("Patcher", $"  Patched {domainCount} domain occurrence(s)");
            totalCount += domainCount;
        }

        // Direct mode replaces only the base domain and preserves service subdomains
        if (mode == "split" && !string.IsNullOrEmpty(subdomainPrefix))
        {
            (string Source, string Destination)[] subdomains =
            [
                ("https://tools.", protocol + subdomainPrefix),
                ("https://sessions.", protocol + subdomainPrefix),
                ("https://account-data.", protocol + subdomainPrefix),
                ("https://telemetry.", protocol + subdomainPrefix),
                ("https://api.", protocol + subdomainPrefix),
                ("https://liveconfig.", protocol + subdomainPrefix),
                ("https://server-discovery.", protocol + subdomainPrefix),
                ("https://social.", protocol + subdomainPrefix),
                ("wss://socket-gateway.", "wss://" + subdomainPrefix)
            ];

            foreach (var (source, destination) in subdomains)
            {
                byte[] oldSubBytes = StringToLengthPrefixed(source);
                byte[] newSubBytes = StringToLengthPrefixed(destination);
                int subCount = ReplaceBytes(data, oldSubBytes, newSubBytes)
                    + ReplaceFrozenString(data, source, destination);
                if (subCount > 0)
                {
                    Logger.Info("Patcher", $"  Patched {subCount} {source} occurrence(s)");
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
        string newUrl = ".gg/MHkEjepMQ7";

        byte[] oldBytes = StringToLengthPrefixed(oldUrl);
        byte[] newBytes = StringToLengthPrefixed(newUrl);
        int count = ReplaceBytes(data, oldBytes, newBytes);

        if (count == 0)
        {
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
        var (mode, mainDomain, subdomainPrefix) = GetDomainStrategy();

        Logger.Info("Patcher", "=== Client Patcher v1.0 ===", false);
        Logger.Info("Patcher", $"Target: {clientPath}", false);
        Logger.Info("Patcher", $"Domain: {_targetDomain} ({_targetDomain.Length} chars)", false);

        if (!File.Exists(clientPath))
        {
            string error = $"Client binary not found: {clientPath}";
            Logger.Error("Patcher", error);
            return new PatchResult { Success = false, Error = error };
        }

        CleanupLegacyFiles(clientPath);

        if (IsPatchedAlready(clientPath))
        {
            Logger.Info("Patcher", $"Client already patched for {_targetDomain}, skipping", false);
            progressCallback?.Invoke("launch.detail.client_already_patched", 100);
            return new PatchResult { Success = true, AlreadyPatched = true, PatchCount = 0 };
        }

        // Restore the stable backup before switching an already-patched authentication target
        string currentFlagFile = GetFlagFilePath(clientPath);
        string legacyFlagFile = clientPath + PatchedFlag;
        if (File.Exists(currentFlagFile) || File.Exists(legacyFlagFile))
        {
            string backupPath = GetBackupFilePath(clientPath);
            if (!File.Exists(backupPath))
            {
                return new PatchResult
                {
                    Success = false,
                    Error = "The client is patched for another target and its original backup is missing"
                };
            }

            File.Copy(backupPath, clientPath, overwrite: true);
            if (File.Exists(currentFlagFile)) File.Delete(currentFlagFile);
            if (legacyFlagFile != currentFlagFile && File.Exists(legacyFlagFile)) File.Delete(legacyFlagFile);
            Logger.Info("Patcher", "Restored the original client before changing the authentication target");
        }

        progressCallback?.Invoke("launch.detail.reading_client_binary", 10);
        Logger.Info("Patcher", "Reading client binary...", false);
        byte[] data = File.ReadAllBytes(clientPath);
        Logger.Info("Patcher", $"Binary size: {data.Length / 1024.0 / 1024.0:F2} MB", false);

        progressCallback?.Invoke("launch.detail.patching_domain_refs", 30);
        Logger.Info("Patcher", "Applying domain patches (length-prefixed format)...", false);
        int domainCount = ApplyDomainPatches(data);

        if (mode == "split"
            && (!ContainsPatchedString(data, mainDomain)
                || !ContainsPatchedString(data, "https://" + subdomainPrefix)))
        {
            return new PatchResult
            {
                Success = false,
                Error = "This client build does not expose the length-prefixed service URLs required by the Local Node patch"
            };
        }

        Logger.Info("Patcher", "Patching Discord URLs...", false);
        int discordCount = PatchDiscordUrl(data);

        int totalCount = domainCount + discordCount;

        if (totalCount == 0)
        {
            if (mode == "split")
            {
                return new PatchResult
                {
                    Success = false,
                    Error = "This client build does not support a split authentication endpoint"
                };
            }

            Logger.Warning("Patcher", "No occurrences found with length-prefixed format - trying UTF-8...", false);

            byte[] oldDomainUtf8 = StringToUtf8(OriginalDomain);
            byte[] newDomainUtf8 = StringToUtf8(mainDomain);
            int utf8Count = ReplaceBytes(data, oldDomainUtf8, newDomainUtf8);

            if (utf8Count > 0)
            {
                Logger.Info("Patcher", $"Found {utf8Count} occurrences with UTF-8 format", false);

                string[] urlPatterns = [
                    "sessions.hytale.com",
                    "tools.hytale.com",
                    "account-data.hytale.com",
                    "telemetry.hytale.com",
                    "api.hytale.com"
                ];

                foreach (var pattern in urlPatterns)
                {
                    byte[] oldUrl = StringToUtf8(pattern);
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

            // One legacy occurrence ends in 0x89, so its UTF-16 pattern omits the final high byte

            byte[] oldDomain = StringToUtf16LE(OriginalDomain);
            byte[] newDomain = StringToUtf16LE(mainDomain);
            int legacyCount = ReplaceBytes(data, oldDomain, newDomain);
            Logger.Info("Patcher", $"Full UTF-16LE pattern: found {legacyCount} occurrences", false);

            byte[] oldDomainPartial = new byte[OriginalDomain.Length * 2 - 1];
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

            var partialPositions = FindAllOccurrences(data, oldDomainPartial);
            Logger.Info("Patcher", $"Partial UTF-16LE pattern (19 bytes): found {partialPositions.Count} occurrences");

            int additionalCount = 0;
            foreach (int pos in partialPositions)
            {
                int afterPos = pos + oldDomainPartial.Length;
                if (afterPos < data.Length && data[afterPos] != 0x00)
                {
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
    /// Check if the client binary in the given game directory is currently patched (any domain)
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
    /// Check if the server JAR in the given game directory is currently patched
    /// </summary>
    public static bool IsServerJarPatched(string gameDir)
    {
        string serverJarPath = Path.Combine(gameDir, "Server", "HytaleServer.jar");
        string patchFlag = serverJarPath + PatchedFlag;
        return File.Exists(patchFlag);
    }

    /// <summary>
    /// Restore the client binary from its .original backup, removing the patch.
    /// Used when switching to official servers where no patching is needed
    /// </summary>
    public static PatchResult RestoreClientFromBackup(string gameDir, Action<string, int?>? progressCallback = null)
    {
        string? clientPath = FindClientPath(gameDir);
        if (clientPath == null)
        {
            Logger.Info("Patcher", "Client binary not found. Nothing to restore");
            return new PatchResult { Success = true, PatchCount = 0 };
        }

        string backupPath = GetBackupFilePath(clientPath);
        string flagFile = GetFlagFilePath(clientPath);

        if (!File.Exists(backupPath))
        {
            Logger.Info("Patcher", "No client backup found. The binary is likely already original");
            if (File.Exists(flagFile)) File.Delete(flagFile);
            return new PatchResult { Success = true, PatchCount = 0 };
        }

        try
        {
            progressCallback?.Invoke("launch.detail.restoring_client", 20);
            Logger.Info("Patcher", $"Restoring original client binary from {backupPath}");

            File.Copy(backupPath, clientPath, overwrite: true);

            if (File.Exists(flagFile)) File.Delete(flagFile);

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
    /// Restore the server JAR from its .original backup, removing the patch
    /// </summary>
    public static PatchResult RestoreServerJarFromBackup(string gameDir, Action<string, int?>? progressCallback = null)
    {
        string serverJarPath = Path.Combine(gameDir, "Server", "HytaleServer.jar");
        string backupPath = serverJarPath + ".original";
        string patchFlag = serverJarPath + PatchedFlag;

        if (!File.Exists(backupPath))
        {
            Logger.Info("Patcher", "No server JAR backup found. The JAR is likely already original");
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
    /// Used when switching to official servers
    /// </summary>
    public static PatchResult RestoreAllFromBackup(string gameDir, Action<string, int?>? progressCallback = null)
    {
        Logger.Info("Patcher", "=== Restoring originals (official mode) ===");

        var clientResult = RestoreClientFromBackup(gameDir, progressCallback);
        if (!clientResult.Success) return clientResult;

        if (clientResult.PatchCount == 0 && RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string? clientPath = FindClientPath(gameDir);
            if (clientPath != null && File.Exists(GetBackupFilePath(clientPath)))
            {
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
