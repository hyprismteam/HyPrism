// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace HyPrism.Desktop.Views;

public sealed partial class SettingsView : UserControl
{
    private const double TwoColumnThreshold = 900;

    public SettingsView()
        => InitializeComponent();

    private void OnSettingsContentSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var columns = e.NewSize.Width >= TwoColumnThreshold ? 2 : 1;
        foreach (var panel in SettingsContent.GetVisualDescendants().OfType<UniformGrid>())
        {
            panel.Columns = columns;
            for (var index = 0; index < panel.Children.Count; index++)
            {
                if (panel.Children[index] is not StackPanel column)
                    continue;

                column.Margin = columns == 1
                    ? new Thickness(0, 0, 0, 12)
                    : index % 2 == 0
                        ? new Thickness(0, 0, 7, 0)
                        : new Thickness(7, 0, 0, 0);
            }
        }
    }
}
