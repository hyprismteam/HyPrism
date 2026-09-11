// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Media;

namespace HyPrism.Desktop.Controls;

/// <summary>One proportional category in the launcher storage overview</summary>
public sealed record StorageUsageSegment(
    string Label,
    long Bytes,
    string DisplaySize,
    string Percentage,
    IBrush Brush,
    string? Count = null)
{
    public bool HasCount => Count is not null;
}
