// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using HyPrism.Desktop.Platform;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class DesktopRenderOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("dxgi")]
    [InlineData(" DXGI ")]
    public void CreateCompositionModes_DefaultsToLowLatencyDxgiSwapChain(string? environmentValue)
    {
        var modes = DesktopRenderOptions.CreateCompositionModes(environmentValue);

        Assert.Equal(
            Win32CompositionMode.LowLatencyDxgiSwapChain,
            Assert.Single(modes.Take(1)));
        Assert.Contains(Win32CompositionMode.WinUIComposition, modes);
        Assert.Contains(Win32CompositionMode.RedirectionSurface, modes);
    }

    [Theory]
    [InlineData("winui")]
    [InlineData("WINUI ")]
    public void CreateCompositionModes_CanRevertToWinUiComposition(string environmentValue)
    {
        var modes = DesktopRenderOptions.CreateCompositionModes(environmentValue);

        Assert.Equal(
            Win32CompositionMode.WinUIComposition,
            Assert.Single(modes.Take(1)));
        Assert.Contains(Win32CompositionMode.RedirectionSurface, modes);
    }

    [Theory]
    [InlineData("dcomp")]
    [InlineData("DCOMP")]
    public void CreateCompositionModes_PutsDirectCompositionFirst(string environmentValue)
    {
        var modes = DesktopRenderOptions.CreateCompositionModes(environmentValue);

        Assert.Equal(
            Win32CompositionMode.DirectComposition,
            Assert.Single(modes.Take(1)));
    }

    [Fact]
    public void CreateWin32Options_UsesTheCompositionModeDefault()
    {
        var options = DesktopGpuPreference.CreateWin32Options();

        Assert.Equal(
            Win32CompositionMode.LowLatencyDxgiSwapChain,
            Assert.Single(options.CompositionMode.Take(1)));
    }
}
