// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Core.Models;

/// <summary>
/// Version status response for latest instance.
/// </summary>
public class VersionStatus
{
    /// <summary>
    /// Status: "not_installed", "update_available", "current", "none", "error"
    /// </summary>
    public string Status { get; set; } = "none";

    /// <summary>
    /// Currently installed version number (0 for latest).
    /// </summary>
    public int InstalledVersion { get; set; }

    /// <summary>
    /// Latest available version number.
    /// </summary>
    public int LatestVersion { get; set; }
}
