// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Application.Ports;
using HyPrism.Core.Game.Launch;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class GpuLaunchPreferenceTests
{
    private static readonly GpuAdapterInfo Rtx = new()
    {
        Name = "NVIDIA GeForce RTX 4070",
        Vendor = "NVIDIA",
        Type = "dedicated",
        PciId = "0000:01:00.0"
    };

    private static readonly GpuAdapterInfo Uhd = new()
    {
        Name = "Intel(R) UHD Graphics 770",
        Vendor = "Intel",
        Type = "integrated",
        PciId = "0000:00:02.0"
    };

    private static readonly GpuAdapterInfo NameOnly = new()
    {
        Name = "AMD Radeon RX 7800 XT",
        Vendor = "AMD",
        Type = "dedicated"
    };

    [Fact]
    public void AutoAndLegacyTypesNeverResolveToAnAdapter()
    {
        var adapters = new[] { Rtx, Uhd, NameOnly };
        Assert.Null(GpuLaunchPreference.FindAdapter("auto", adapters));
        Assert.Null(GpuLaunchPreference.FindAdapter("AUTO", adapters));
        Assert.Null(GpuLaunchPreference.FindAdapter(null, adapters));
        Assert.Null(GpuLaunchPreference.FindAdapter("", adapters));
        Assert.Null(GpuLaunchPreference.FindAdapter("dedicated", adapters));
        Assert.Null(GpuLaunchPreference.FindAdapter("integrated", adapters));
    }

    [Fact]
    public void PciKeysResolveCaseInsensitively()
    {
        var adapters = new[] { Rtx, Uhd, NameOnly };
        Assert.Same(Uhd, GpuLaunchPreference.FindAdapter("pci:0000:00:02.0", adapters));
        Assert.Same(Uhd, GpuLaunchPreference.FindAdapter("PCI:0000:00:02.0", adapters));
        Assert.Null(GpuLaunchPreference.FindAdapter("pci:0000:09:0f.0", adapters));
    }

    [Fact]
    public void CardNamesResolveExactly()
    {
        var adapters = new[] { Rtx, Uhd, NameOnly };
        Assert.Same(NameOnly, GpuLaunchPreference.FindAdapter("AMD Radeon RX 7800 XT", adapters));
        Assert.Null(GpuLaunchPreference.FindAdapter("amd radeon rx 7800 xt", adapters));
        Assert.Null(GpuLaunchPreference.FindAdapter("GeForce", adapters));
    }

    [Fact]
    public void AdapterValuePrefersPciKey()
    {
        Assert.Equal("pci:0000:01:00.0", GpuLaunchPreference.AdapterValue(Rtx));
        Assert.Equal("AMD Radeon RX 7800 XT", GpuLaunchPreference.AdapterValue(NameOnly));
    }
}
