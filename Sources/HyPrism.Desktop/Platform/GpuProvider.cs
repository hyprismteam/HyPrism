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
                adapter.PciId = match.Groups[1].Value.Trim();
                return adapter;
            })
            .ToList();
    }

    private static List<GpuAdapterInfo> DetectMacOS()
    {
        var output = RunProcess("system_profiler", "SPDisplaysDataType -detailLevel mini");
        return Regex.Matches(output, @"Chipset Model:\s*(.+)", RegexOptions.IgnoreCase)
            .Select(match => match.Groups[1].Value.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => CreateAdapter(name, ExtractVendor(name)))
            .ToList();
    }

    private static GpuAdapterInfo CreateAdapter(string name, string vendor)
        => new()
        {
            Name = name,
            Vendor = string.IsNullOrWhiteSpace(vendor) ? ExtractVendor(name) : vendor,
            Type = Classify(name)
        };

    private static string Classify(string name)
    {
        var value = name.ToLowerInvariant();
        if (value.Contains("intel") ||
            value.Contains("apple m") ||
            value.Contains("apple gpu") ||
            value.Contains("microsoft basic") ||
            value.Contains("basic render") ||
            value.Contains("vega 3") ||
            value.Contains("vega 6") ||
            value.Contains("vega 7") ||
            value.Contains("vega 8"))
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
