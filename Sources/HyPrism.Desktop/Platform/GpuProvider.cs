// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Text.RegularExpressions;
using HyPrism.Core.Infrastructure;
using HyPrism.Core.Application.Ports;

namespace HyPrism.Desktop.Platform;

/// <summary>
/// Detects graphics adapters through the platform tools available to Desktop
/// </summary>
public sealed class GpuProvider : IGpuProvider
{
    private List<GpuAdapterInfo>? _cachedAdapters;

    /// <inheritdoc/>
    public List<GpuAdapterInfo> GetAdapters()
    {
        if (_cachedAdapters is not null)
            return _cachedAdapters;

        try
        {
            _cachedAdapters = OperatingSystem.IsWindows()
                ? DetectWindows()
                : OperatingSystem.IsLinux()
                    ? DetectLinux()
                    : OperatingSystem.IsMacOS()
                        ? DetectMacOS()
                        : [];
        }
        catch (Exception ex)
        {
            Logger.Warning("GPU", $"GPU detection failed: {ex.Message}");
            _cachedAdapters = [];
        }

        foreach (var adapter in _cachedAdapters)
            Logger.Info("GPU", $"Detected: {adapter.Name} (type={adapter.Type})");

        return _cachedAdapters;
    }

    /// <inheritdoc/>
    public bool HasSingleGpu() => GetAdapters().Count <= 1;

    private static List<GpuAdapterInfo> DetectWindows()
    {
        var output = RunProcess(
            "powershell",
            "-NoProfile -NonInteractive -Command \"Get-CimInstance Win32_VideoController | ForEach-Object { $_.Name + '|||' + $_.AdapterCompatibility }\"");

        if (string.IsNullOrWhiteSpace(output))
            output = RunProcess("wmic", "path win32_videocontroller get name /format:list");

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseWindowsAdapter)
            .Where(adapter => adapter is not null)
            .Select(adapter => adapter!)
            .GroupBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static GpuAdapterInfo? ParseWindowsAdapter(string line)
    {
        var parts = line.Split("|||", StringSplitOptions.TrimEntries);
        var name = parts[0].StartsWith("Name=", StringComparison.OrdinalIgnoreCase)
            ? parts[0]["Name=".Length..].Trim()
            : parts[0].Trim();

        if (string.IsNullOrWhiteSpace(name))
            return null;

        return CreateAdapter(name, parts.Length > 1 ? parts[1] : string.Empty);
    }

    private static List<GpuAdapterInfo> DetectLinux()
    {
        // sysfs works everywhere (including sandboxed launches) but exposes PCI ids
        // instead of marketing names; lspci supplies readable names when available
        var adapters = DetectLinuxSysfs();
        if (adapters.Count == 0)
            return DetectLinuxLspci();

        var adapterById = adapters
            .Where(adapter => !string.IsNullOrEmpty(adapter.PciId))
            .ToDictionary(adapter => adapter.PciId, StringComparer.OrdinalIgnoreCase);
        foreach (var named in DetectLinuxLspci())
        {
            if (!string.IsNullOrEmpty(named.PciId) &&
                adapterById.TryGetValue(named.PciId, out var existing))
            {
                existing.Name = named.Name;
                existing.Vendor = named.Vendor;
                existing.Type = named.Type;
            }
        }

        return adapters;
    }

    private static List<GpuAdapterInfo> DetectLinuxLspci()
    {
        var output = RunProcess("lspci", string.Empty);
        return Regex.Matches(
                output,
                @"^([0-9a-fA-F:.]+)\s+(?:VGA compatible controller|3D controller|Display controller):\s*(.+)$",
                RegexOptions.IgnoreCase | RegexOptions.Multiline)
            .Select(match =>
            {
                var name = Regex.Replace(
                    match.Groups[2].Value.Trim(),
                    @"\s*\(rev [0-9a-f]+\)$",
                    string.Empty,
                    RegexOptions.IgnoreCase);
                var adapter = CreateAdapter(name, ExtractVendor(name));
                if (adapter is null)
                    return null;
                adapter.PciId = match.Groups[1].Value.Trim();
                return adapter;
            })
            .Where(adapter => adapter is not null)
            .Select(adapter => adapter!)
            .ToList();
    }

    private static List<GpuAdapterInfo> DetectLinuxSysfs()
    {
        const string drmRoot = "/sys/class/drm";
        var adapters = new List<GpuAdapterInfo>();

        try
        {
            foreach (var cardPath in Directory.EnumerateDirectories(drmRoot))
            {
                var cardName = Path.GetFileName(cardPath);
                if (!Regex.IsMatch(cardName, @"^card\d+$", RegexOptions.IgnoreCase))
                    continue;

                var devicePath = Path.Combine(cardPath, "device");
                var vendorId = ReadSysfsValue(Path.Combine(devicePath, "vendor"));
                var deviceId = ReadSysfsValue(Path.Combine(devicePath, "device"));
                if (!vendorId.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                    vendorId == "0x0000")
                    continue;

                var pciId = ReadSysfsPciId(devicePath);
                var vendorName = GetPciVendorName(vendorId);
                if (vendorName.Length == 0)
                    continue;

                var name = $"{vendorName} GPU ({vendorId}/{deviceId.ToLowerInvariant()})";
                var adapter = CreateAdapter(name, vendorName);
                if (adapter is null)
                    continue;

                adapter.PciId = pciId;
                adapter.Type = ClassifyLinuxSysfs(vendorName, adapter.Type);
                adapters.Add(adapter);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warning("GPU", $"Linux sysfs adapter detection is unavailable: {ex.Message}");
        }

        return adapters;
    }

    private static string ReadSysfsValue(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static string ReadSysfsPciId(string devicePath)
    {
        try
        {
            // /sys/class/drm/cardN is a symlink into /sys/devices/.../0000:NN:NN.N/
            var target = Directory.ResolveLinkTarget(devicePath, returnFinalTarget: true)?.FullName;
            if (string.IsNullOrEmpty(target))
                return string.Empty;

            var pciMatch = Regex.Match(
                target,
                @"(\d{4}:[0-9a-fA-F]{2}:\d{2}\.\d)",
                RegexOptions.IgnoreCase);
            return pciMatch.Success ? pciMatch.Groups[1].Value : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static string GetPciVendorName(string vendorId)
        => vendorId.ToLowerInvariant() switch
        {
            "0x10de" => "NVIDIA",
            "0x1002" or "0x1022" => "AMD",
            "0x8086" => "Intel",
            "0x106b" => "Apple",
            _ => string.Empty
        };

    private static string ClassifyLinuxSysfs(string vendorName, string classified)
    {
        // sysfs exposes drivers and ids but not lineups, so vendor drives the decision;
        // AMD cards report the same driver for integrated and discrete chips
        return vendorName switch
        {
            "Intel" => "integrated",
            "NVIDIA" => "dedicated",
            _ => classified
        };
    }

    private static List<GpuAdapterInfo> DetectMacOS()
    {
        var output = RunProcess("system_profiler", "SPDisplaysDataType -detailLevel mini");
        return Regex.Matches(output, @"Chipset Model:\s*(.+)", RegexOptions.IgnoreCase)
            .Select(match => match.Groups[1].Value.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => CreateAdapter(name, ExtractVendor(name)))
            .Where(adapter => adapter is not null)
            .Select(adapter => adapter!)
            .ToList();
    }

    private static GpuAdapterInfo? CreateAdapter(string name, string vendor)
    {
        if (IsVirtualAdapter(name))
            return null;

        return new GpuAdapterInfo
        {
            Name = name,
            Vendor = string.IsNullOrWhiteSpace(vendor) ? ExtractVendor(name) : vendor,
            Type = Classify(name)
        };
    }

    internal static bool IsVirtualAdapter(string name)
    {
        var value = name.ToLowerInvariant();
        return value.Contains("microsoft basic") ||
               value.Contains("hyper-v") ||
               value.Contains("vmware") ||
               value.Contains("virtualbox") ||
               value.Contains("parallels") ||
               value.Contains("qxl") ||
               value.Contains("indirect display") ||
               value.Contains("llvmpipe") ||
               value.Contains("softpipe");
    }

    internal static string Classify(string name)
    {
        var value = name.ToLowerInvariant();

        // Discrete lineups that would otherwise trip the vendor or integrated heuristics
        if (value.Contains("arc") || value.Contains(" rx"))
            return "dedicated";

        if (value.Contains("intel") ||
            value.Contains("apple m") ||
            value.Contains("apple gpu") ||
            value.Contains("vega 3") ||
            value.Contains("vega 6") ||
            value.Contains("vega 7") ||
            value.Contains("vega 8"))
        {
            return "integrated";
        }

        if (value.Contains("radeon") &&
            (value.Contains("graphics") ||
             Regex.IsMatch(value, @"radeon( \(tm\))?\s*\d{3}m\b", RegexOptions.IgnoreCase)))
        {
            return "integrated";
        }

        return "dedicated";
    }

    private static string ExtractVendor(string name)
    {
        var value = name.ToLowerInvariant();
        if (value.Contains("nvidia")) return "NVIDIA";
        if (value.Contains("amd") || value.Contains("ati") || value.Contains("radeon")) return "AMD";
        if (value.Contains("intel")) return "Intel";
        if (value.Contains("apple")) return "Apple";
        return string.Empty;
    }

    private static string RunProcess(string executable, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5000))
                process.Kill(true);
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }
}
