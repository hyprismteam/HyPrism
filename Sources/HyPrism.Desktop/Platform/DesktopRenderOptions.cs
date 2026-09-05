// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using HyPrism.Core.Infrastructure;

namespace HyPrism.Desktop.Platform;

/// <summary>
/// Selects the Win32 composition mode used by Avalonia for frame presentation.
/// The default is the low-latency flip-model DXGI swap chain paced by display vblank.
/// The environment variable HYPRISM_WIN32_COMPOSITION reverts it for troubleshooting:
/// winui (Avalonia default), dcomp, or dxgi.
/// </summary>
internal static class DesktopRenderOptions
{
    internal const string CompositionEnvironmentVariable = "HYPRISM_WIN32_COMPOSITION";

    internal static IReadOnlyList<Win32CompositionMode> CreateCompositionModes(
        string? environmentValue = null)
    {
        var requested = ParseCompositionMode(
            environmentValue ?? Environment.GetEnvironmentVariable(CompositionEnvironmentVariable));

        var modes = requested switch
        {
            Win32CompositionMode.DirectComposition => DirectCompositionModes,
            Win32CompositionMode.WinUIComposition => WinUiCompositionModes,
            _ => LowLatencyDxgiModes
        };

        return modes;
    }

    private static IReadOnlyList<Win32CompositionMode> LowLatencyDxgiModes { get; } =
    [
        Win32CompositionMode.LowLatencyDxgiSwapChain,
        Win32CompositionMode.WinUIComposition,
        Win32CompositionMode.RedirectionSurface
    ];

    private static IReadOnlyList<Win32CompositionMode> WinUiCompositionModes { get; } =
    [
        Win32CompositionMode.WinUIComposition,
        Win32CompositionMode.DirectComposition,
        Win32CompositionMode.RedirectionSurface
    ];

    private static IReadOnlyList<Win32CompositionMode> DirectCompositionModes { get; } =
    [
        Win32CompositionMode.DirectComposition,
        Win32CompositionMode.WinUIComposition,
        Win32CompositionMode.RedirectionSurface
    ];

    private static Win32CompositionMode? ParseCompositionMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().ToLowerInvariant() switch
        {
            "winui" => Win32CompositionMode.WinUIComposition,
            "dcomp" => Win32CompositionMode.DirectComposition,
            "dxgi" => Win32CompositionMode.LowLatencyDxgiSwapChain,
            _ => Invalid(value)
        };
    }

    private static Win32CompositionMode? Invalid(string value)
    {
        Logger.Warning(
            "Render",
            $"Unknown {CompositionEnvironmentVariable} value '{value}', falling back to the default low-latency DXGI composition mode");
        return null;
    }

    private static string FormatModes(IReadOnlyList<Win32CompositionMode> modes)
        => string.Join(" | ", modes.Select(mode => mode.ToString()));
}
