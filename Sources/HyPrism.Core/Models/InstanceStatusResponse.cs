// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Models;

/// <summary>
/// Response model for instance status queries.
/// Indicates whether an instance is playable and provides context.
/// </summary>
public class InstanceStatusResponse
{
    /// <summary>
    /// Gets or sets whether the instance is ready to launch.
    /// True if the game client is present and valid.
    /// </summary>
    public bool Playable { get; set; }

    /// <summary>
    /// Gets or sets a human-readable reason for the playability status.
    /// Examples: "Ready", "Game not installed", "Instance not found"
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}
