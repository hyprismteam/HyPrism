// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Controls;
using HyPrism.Desktop.ViewModels;

namespace HyPrism.Desktop.Views;

public sealed partial class DashboardView : UserControl
{
    private const double CompactDashboardThreshold = 900;

    public DashboardView()
    {
        InitializeComponent();
        SizeChanged += (_, args) => UpdateLayoutMode(args.NewSize.Width);
        DataContextChanged += (_, _) => UpdateLayoutMode(Bounds.Width);
    }

    private void UpdateLayoutMode(double width)
    {
        if (DataContext is MainWindowViewModel viewModel && width > 0)
            viewModel.IsCompactDashboardLayout = width < CompactDashboardThreshold;
    }
}
