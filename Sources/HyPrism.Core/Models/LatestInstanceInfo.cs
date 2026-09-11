// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System;

namespace HyPrism.Core.Models;

/// <summary>
/// Stores information about the latest installed game instance version.
/// </summary>
public sealed class LatestInstanceInfo
{
    /// <summary>Latest installed game version number.</summary>
    public int Version { get; set; }
    /// <summary>Timestamp when this version info was last updated.</summary>
    public DateTime UpdatedAt { get; set; }
}
