// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Core.Application.Ports;

/// <summary>
/// Describes a graphics adapter detected by the active application host
/// </summary>
public sealed class GpuAdapterInfo
{
    /// <summary>Gets or sets the adapter display name</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the normalized vendor name</summary>
    public string Vendor { get; set; } = string.Empty;

    /// <summary>Gets or sets the platform adapter identifier when available</summary>
    public string PciId { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the adapter is dedicated or integrated</summary>
    public string Type { get; set; } = "dedicated";
}
