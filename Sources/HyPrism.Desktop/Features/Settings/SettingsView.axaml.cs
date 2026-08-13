// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
namespace HyPrism.Desktop.Features.Settings;

public sealed partial class SettingsView : UserControl
{
    private const double WideLayoutThreshold = 940;
    private const double WideContentMaxWidth = 720;

    private bool? _usesCompactLayout;
    private bool _compactContentOpen;
    private double _backgroundPickerWidth;

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

        SettingsContentHost.Margin = compact
            ? new Thickness(24, 16, 24, 36)
            : new Thickness(32, 28, 32, 40);
        SettingsContentHost.MaxWidth = compact
            ? double.PositiveInfinity
            : WideContentMaxWidth;
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
        => TryCloseCompactContent();

    private void OnBackgroundPickerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || Math.Abs(_backgroundPickerWidth - e.NewSize.Width) < 0.5)
            return;

        _backgroundPickerWidth = e.NewSize.Width;
        Dispatcher.UIThread.Post(
            () => UpdateBackgroundPickerLayout(e.NewSize.Width),
            DispatcherPriority.Loaded);
    }

    private void OnAboutContributorsSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        const double containerPadding = 28;
        const double contributorSlotWidth = 64;

        if (DataContext is not SettingsViewModel viewModel || e.NewSize.Width <= containerPadding)
            return;

        var slots = Math.Max(
            1,
            (int)Math.Floor((e.NewSize.Width - containerPadding) / contributorSlotWidth));
        viewModel.UpdateAboutContributorCapacity(slots);
    }

    private void UpdateBackgroundPickerLayout(double availableWidth)
    {
        const double targetSlotWidth = 185;
        const double minimumSlotWidth = 150;
        const double tileMargin = 12;

        var maximumColumns = Math.Max(1, (int)Math.Floor(availableWidth / minimumSlotWidth));
        var columns = Math.Clamp(
            (int)Math.Round(availableWidth / targetSlotWidth, MidpointRounding.AwayFromZero),
            1,
            maximumColumns);
        var slotWidth = Math.Floor((availableWidth - 1) / columns);
        var tileWidth = Math.Max(120, slotWidth - tileMargin);
        var tileHeight = Math.Round(tileWidth * 9 / 16);

        var panel = BackgroundPicker.GetVisualDescendants().OfType<WrapPanel>().FirstOrDefault();
        if (panel is not null)
        {
            panel.ItemWidth = slotWidth;
            panel.ItemHeight = tileHeight + tileMargin;
        }

        foreach (var button in BackgroundPicker.GetVisualDescendants()
                     .OfType<Button>()
                     .Where(button => button.Classes.Contains("backgroundChoice")))
        {
            button.Width = tileWidth;
            button.Height = tileHeight;
        }
    }

    public bool TryCloseCompactContent()
    {
        if (_usesCompactLayout is not true || !_compactContentOpen)
            return false;

        _compactContentOpen = false;
        SettingsMain.IsHitTestVisible = false;
        SettingsCategoryRail.IsHitTestVisible = true;
        MainTranslation.X = Bounds.Width;
        return true;
    }

    private void SetMainOffsetWithoutTransition(double offset)
    {
        var transitions = MainTranslation.Transitions;
        MainTranslation.Transitions = null;
        MainTranslation.X = offset;
        MainTranslation.Transitions = transitions;
    }
}
