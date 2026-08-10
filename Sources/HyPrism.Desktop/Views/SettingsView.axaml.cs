// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using HyPrism.Desktop.ViewModels;

namespace HyPrism.Desktop.Views;

public sealed partial class SettingsView : UserControl
{
    private const double WideLayoutThreshold = 940;

    private bool? _usesCompactLayout;
    private bool _compactContentOpen;

    private TranslateTransform MainTranslation
        => (TranslateTransform)SettingsMain.RenderTransform!;

    public SettingsView()
        => InitializeComponent();

    private void OnSettingsViewSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < WideLayoutThreshold;
        if (_usesCompactLayout != compact)
        {
            var keepContentOpen = compact && _usesCompactLayout is false;
            _usesCompactLayout = compact;
            _compactContentOpen = keepContentOpen;
            ApplyLayoutMode(compact, e.NewSize.Width);
        }
        else if (compact && !_compactContentOpen)
        {
            SetMainOffsetWithoutTransition(e.NewSize.Width);
        }

        SettingsHeader.Margin = compact
            ? new Thickness(24, 6, 24, 14)
            : new Thickness(32, 23, 32, 16);
        SettingsContentHost.Margin = compact
            ? new Thickness(24, 2, 24, 36)
            : new Thickness(32, 2, 32, 40);
    }

    private void ApplyLayoutMode(bool compact, double width)
    {
        SettingsCategoryRail.Classes.Set("compact", compact);
        if (DataContext is SettingsViewModel viewModel)
            viewModel.IsCompactLayout = compact;

        CompactSettingsToolbar.IsVisible = compact;
        if (compact)
        {
            SettingsLayout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            SettingsLayout.ColumnDefinitions[1].Width = new GridLength(0);
            Grid.SetColumn(SettingsMain, 0);
            SettingsCategoryRail.IsHitTestVisible = !_compactContentOpen;
            SettingsMain.IsHitTestVisible = _compactContentOpen;
            SetMainOffsetWithoutTransition(_compactContentOpen ? 0 : width);
            return;
        }

        SettingsLayout.ColumnDefinitions[0].Width = new GridLength(276);
        SettingsLayout.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(SettingsMain, 1);
        SettingsCategoryRail.IsHitTestVisible = true;
        SettingsMain.IsHitTestVisible = true;
        SetMainOffsetWithoutTransition(0);
    }

    private void OnSettingsCategoryClicked(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(SettingsContent.ScrollToHome, DispatcherPriority.Background);
        if (_usesCompactLayout is not true)
            return;

        _compactContentOpen = true;
        SettingsCategoryRail.IsHitTestVisible = false;
        SettingsMain.IsHitTestVisible = true;
        MainTranslation.X = 0;
    }

    private void OnCompactSettingsBackClicked(object? sender, RoutedEventArgs e)
    {
        if (_usesCompactLayout is not true)
            return;

        _compactContentOpen = false;
        SettingsMain.IsHitTestVisible = false;
        SettingsCategoryRail.IsHitTestVisible = true;
        MainTranslation.X = Bounds.Width;
    }

    private void SetMainOffsetWithoutTransition(double offset)
    {
        var transitions = MainTranslation.Transitions;
        MainTranslation.Transitions = null;
        MainTranslation.X = offset;
        MainTranslation.Transitions = transitions;
    }
}
