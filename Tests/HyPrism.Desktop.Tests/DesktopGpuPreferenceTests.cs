// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Platform;
using HyPrism.Desktop.Platform;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class DesktopGpuPreferenceTests
{
    [Fact]
    public void CreateWin32Options_UsesAngleWithSoftwareFallbackAndAdapterSelection()
    {
        var options = DesktopGpuPreference.CreateWin32Options();

        Assert.Equal(
            [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software],
            options.RenderingMode);
        Assert.NotNull(options.GraphicsAdapterSelectionCallback);
    }

    [Fact]
    public void FindAdapterIndex_MatchesDxgiLuidInsteadOfAdapterName()
    {
        var preferredLuid = BitConverter.GetBytes(42L);
        PlatformGraphicsDeviceAdapterDescription[] adapters =
        [
            new() { Description = "Integrated", DeviceLuid = BitConverter.GetBytes(11L) },
            new() { Description = "Dedicated", DeviceLuid = preferredLuid.ToArray() }
        ];

        var result = DesktopGpuPreference.FindAdapterIndex(adapters, preferredLuid);

        Assert.Equal(1, result);
    }

    [Fact]
    public void FindAdapterIndex_ReturnsMissingWhenAvaloniaDoesNotExposePreferredAdapter()
    {
        PlatformGraphicsDeviceAdapterDescription[] adapters =
        [
            new() { Description = "Available", DeviceLuid = BitConverter.GetBytes(11L) }
        ];

        var result = DesktopGpuPreference.FindAdapterIndex(adapters, BitConverter.GetBytes(42L));

        Assert.Equal(-1, result);
    }

    [Fact]
    public void BuildLinuxEnvironmentOverrides_RequestsMesaAndNvidiaOffloadForHybridGraphics()
    {
        var current = EmptyLinuxEnvironment();

        var result = DesktopGpuPreference.BuildLinuxEnvironmentOverrides(
            current,
            ["0x8086", "0x10de"]);

        Assert.Equal("1", result["DRI_PRIME"]);
        Assert.Equal("1", result["__NV_PRIME_RENDER_OFFLOAD"]);
        Assert.Equal("nvidia", result["__GLX_VENDOR_LIBRARY_NAME"]);
    }

    [Fact]
    public void BuildLinuxEnvironmentOverrides_RequestsMesaOffloadForNonNvidiaHybridGraphics()
    {
        var current = EmptyLinuxEnvironment();

        var result = DesktopGpuPreference.BuildLinuxEnvironmentOverrides(
            current,
            ["0x8086", "0x1002"]);

        Assert.Single(result);
        Assert.Equal("1", result["DRI_PRIME"]);
    }

    [Fact]
    public void BuildLinuxEnvironmentOverrides_DoesNotChangeSingleGpuSystems()
    {
        var result = DesktopGpuPreference.BuildLinuxEnvironmentOverrides(
            EmptyLinuxEnvironment(),
            ["0x8086"]);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildLinuxEnvironmentOverrides_PreservesExplicitUserValues()
    {
        var current = new Dictionary<string, string?>
        {
            ["DRI_PRIME"] = "pci-0000_03_00_0",
            ["__NV_PRIME_RENDER_OFFLOAD"] = "0",
            ["__GLX_VENDOR_LIBRARY_NAME"] = "custom"
        };

        var result = DesktopGpuPreference.BuildLinuxEnvironmentOverrides(
            current,
            ["0x8086", "0x10de"]);

        Assert.Empty(result);
    }

    private static Dictionary<string, string?> EmptyLinuxEnvironment()
        => new()
        {
            ["DRI_PRIME"] = null,
            ["__NV_PRIME_RENDER_OFFLOAD"] = null,
            ["__GLX_VENDOR_LIBRARY_NAME"] = null
        };
}
