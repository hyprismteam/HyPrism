// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;

namespace HyPrism.Desktop.Controls;

/// <summary>Arranges storage categories as proportional segments without a render loop</summary>
public sealed class StorageUsageBarPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0d;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var totalBytes = Children
            .OfType<Control>()
            .Sum(child => (child.DataContext as StorageUsageSegment)?.Bytes ?? 0);
        var x = 0d;
        foreach (var child in Children.OfType<Control>())
        {
            var bytes = (child.DataContext as StorageUsageSegment)?.Bytes ?? 0;
            var width = totalBytes > 0 ? finalSize.Width * bytes / totalBytes : 0;
            child.Arrange(new Rect(x, 0, Math.Max(0, width - 2), finalSize.Height));
            x += width;
        }

        return finalSize;
    }
}
