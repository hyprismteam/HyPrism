// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Platform;
using HyPrism.Core.Infrastructure;
using Vortice.DXGI;
using static Vortice.DXGI.DXGI;

namespace HyPrism.Desktop.Platform;

internal static class DesktopGpuPreference
{
    private const string LinuxDriPrime = "DRI_PRIME";
    private const string LinuxNvidiaPrimeOffload = "__NV_PRIME_RENDER_OFFLOAD";
    private const string LinuxGlxVendor = "__GLX_VENDOR_LIBRARY_NAME";
    private const string NvidiaVendorId = "0x10de";

    private static readonly Lazy<WindowsAdapterPreference?> PreferredWindowsAdapter =
        new(FindHighPerformanceWindowsAdapter, LazyThreadSafetyMode.ExecutionAndPublication);

    internal static void ConfigureBeforeAvalonia()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var currentEnvironment = new Dictionary<string, string?>
        {
            [LinuxDriPrime] = Environment.GetEnvironmentVariable(LinuxDriPrime),
            [LinuxNvidiaPrimeOffload] = Environment.GetEnvironmentVariable(LinuxNvidiaPrimeOffload),
            [LinuxGlxVendor] = Environment.GetEnvironmentVariable(LinuxGlxVendor)
        };
        var overrides = BuildLinuxEnvironmentOverrides(currentEnvironment, DetectLinuxGpuVendors());

        foreach (var (name, value) in overrides)
            Environment.SetEnvironmentVariable(name, value);

        if (overrides.Count > 0)
            Logger.Info("GPU", "Requested the non-default high-performance GPU for Avalonia rendering");
    }

    internal static Win32PlatformOptions CreateWin32Options()
        => new()
        {
            RenderingMode = [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software],
            GraphicsAdapterSelectionCallback = SelectWindowsAdapter
        };

    internal static int FindAdapterIndex(
        IReadOnlyList<PlatformGraphicsDeviceAdapterDescription> adapters,
        ReadOnlySpan<byte> preferredLuid)
    {
        for (var index = 0; index < adapters.Count; index++)
        {
            var candidateLuid = adapters[index].DeviceLuid;
            if (candidateLuid is not null && preferredLuid.SequenceEqual(candidateLuid))
                return index;
        }

        return -1;
    }

    internal static IReadOnlyDictionary<string, string> BuildLinuxEnvironmentOverrides(
        IReadOnlyDictionary<string, string?> currentEnvironment,
        IReadOnlyList<string> detectedVendors)
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        if (detectedVendors.Count < 2)
            return overrides;

        AddIfUnset(overrides, currentEnvironment, LinuxDriPrime, "1");

        if (detectedVendors.Any(vendor =>
                string.Equals(vendor, NvidiaVendorId, StringComparison.OrdinalIgnoreCase)))
        {
            AddIfUnset(overrides, currentEnvironment, LinuxNvidiaPrimeOffload, "1");
            AddIfUnset(overrides, currentEnvironment, LinuxGlxVendor, "nvidia");
        }

        return overrides;
    }

    private static int SelectWindowsAdapter(
        IReadOnlyList<PlatformGraphicsDeviceAdapterDescription> adapters)
    {
        if (adapters.Count == 0 || !OperatingSystem.IsWindows())
            return 0;

        var preferred = PreferredWindowsAdapter.Value;
        if (preferred is null)
            return 0;

        var selectedIndex = FindAdapterIndex(adapters, preferred.Luid);
        if (selectedIndex < 0)
        {
            Logger.Warning(
                "GPU",
                $"DXGI preferred adapter '{preferred.Description}' was not exposed by Avalonia, using '{adapters[0].Description}'");
            return 0;
        }

        Logger.Info("GPU", $"Avalonia renderer selected '{adapters[selectedIndex].Description}'");
        return selectedIndex;
    }

    private static WindowsAdapterPreference? FindHighPerformanceWindowsAdapter()
    {
        try
        {
            using var factory = CreateDXGIFactory1<IDXGIFactory6>();
            for (uint index = 0; ; index++)
            {
                var result = factory.EnumAdapterByGpuPreference(
                    index,
                    GpuPreference.HighPerformance,
                    out IDXGIAdapter1? adapter);
                if (result.Failure)
                    return null;

                using (adapter)
                {
                    if (adapter is null)
                        continue;

                    var description = adapter.Description1;
                    if ((description.Flags & AdapterFlags.Software) != AdapterFlags.None)
                        continue;

                    return new WindowsAdapterPreference(
                        BitConverter.GetBytes((long)description.Luid),
                        description.Description);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("GPU", $"DXGI high-performance adapter selection is unavailable: {ex.Message}");
            return null;
        }
    }

    private static IReadOnlyList<string> DetectLinuxGpuVendors()
    {
        const string drmRoot = "/sys/class/drm";
        var vendors = new List<string>();

        try
        {
            foreach (var cardPath in Directory.EnumerateDirectories(drmRoot, "card*"))
            {
                var cardName = Path.GetFileName(cardPath);
                if (cardName.Length <= "card".Length ||
                    !int.TryParse(cardName.AsSpan("card".Length), out _))
                {
                    continue;
                }

                var vendorPath = Path.Combine(cardPath, "device", "vendor");
                if (File.Exists(vendorPath))
                    vendors.Add(File.ReadAllText(vendorPath).Trim());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warning("GPU", $"Linux DRM adapter detection is unavailable: {ex.Message}");
        }

        return vendors;
    }

    private static void AddIfUnset(
        IDictionary<string, string> overrides,
        IReadOnlyDictionary<string, string?> currentEnvironment,
        string name,
        string value)
    {
        if (!currentEnvironment.TryGetValue(name, out var currentValue) || currentValue is null)
            overrides[name] = value;
    }

    private sealed record WindowsAdapterPreference(byte[] Luid, string Description);
}
