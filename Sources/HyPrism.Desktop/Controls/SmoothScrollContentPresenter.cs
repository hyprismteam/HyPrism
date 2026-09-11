// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace HyPrism.Desktop.Controls;

/// <summary>
/// Routes wheel input from the content presenter to its owning smooth scroll viewer.
/// </summary>
public sealed class SmoothScrollContentPresenter : ScrollContentPresenter
{
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (this.FindAncestorOfType<ScrollViewer>() is SmoothScrollViewer viewer)
        {
            viewer.HandlePointerWheelChanged(e);
            return;
        }

        base.OnPointerWheelChanged(e);
    }
}
