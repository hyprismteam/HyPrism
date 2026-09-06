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
    public const string Auto = "auto";
    public const string Dedicated = "dedicated";
    public const string Integrated = "integrated";

    public static bool IsAuto(string? preference)
        => string.IsNullOrWhiteSpace(preference) ||
           string.Equals(preference.Trim(), Auto, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the adapter selected by a per-adapter preference value, or null when the
    /// preference is a legacy type value or does not match any detected adapter
    /// </summary>
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
    public static string AdapterValue(GpuAdapterInfo adapter)
        => string.IsNullOrWhiteSpace(adapter.PciId)
            ? adapter.Name
            : $"pci:{adapter.PciId}";
}
