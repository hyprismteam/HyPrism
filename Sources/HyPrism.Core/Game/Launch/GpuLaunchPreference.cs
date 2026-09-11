// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using HyPrism.Core.Application.Ports;

namespace HyPrism.Core.Game.Launch;

/// <summary>
/// Resolves the stored GPU preference into a concrete adapter. Stored values are
/// "auto", the legacy "dedicated"/"integrated" types, or an adapter key
/// ("pci:&lt;id&gt;" when the platform exposes it, otherwise the card name)
/// </summary>
public static class GpuLaunchPreference
{
    /// <summary>Preference value that lets the operating system choose the adapter</summary>
    public const string Auto = "auto";
    /// <summary>Legacy preference value for a dedicated adapter</summary>
    public const string Dedicated = "dedicated";
    /// <summary>Legacy preference value for an integrated adapter</summary>
    public const string Integrated = "integrated";

    /// <summary>Determines whether a preference requests automatic adapter selection</summary>
    /// <param name="preference">Stored GPU preference value</param>
    /// <returns><see langword="true"/> for an empty value or the <c>auto</c> value</returns>
    public static bool IsAuto(string? preference)
        => string.IsNullOrWhiteSpace(preference) ||
           string.Equals(preference.Trim(), Auto, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the adapter selected by a per-adapter preference value, or null when the
    /// preference is a legacy type value or does not match any detected adapter
    /// </summary>
    /// <returns>The matching adapter, or null when unavailable</returns>
    public static GpuAdapterInfo? FindAdapter(
        string? preference,
        IReadOnlyList<GpuAdapterInfo> adapters)
    {
        if (string.IsNullOrWhiteSpace(preference) || IsAuto(preference))
            return null;

        if (string.Equals(preference.Trim(), Dedicated, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(preference.Trim(), Integrated, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (preference.Trim().StartsWith("pci:", StringComparison.OrdinalIgnoreCase))
        {
            return adapters.FirstOrDefault(adapter =>
                !string.IsNullOrEmpty(adapter.PciId) &&
                string.Equals($"pci:{adapter.PciId}", preference.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return adapters.FirstOrDefault(adapter =>
            string.Equals(adapter.Name, preference.Trim(), StringComparison.Ordinal));
    }

    /// <summary>
    /// Builds the stable preference value for an adapter
    /// </summary>
    /// <returns>The normalized adapter value</returns>
    public static string AdapterValue(GpuAdapterInfo adapter)
        => string.IsNullOrWhiteSpace(adapter.PciId)
            ? adapter.Name
            : $"pci:{adapter.PciId}";
}
