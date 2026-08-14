// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HyPrism.Desktop.Features.Instances;
using HyPrism.Desktop.Features.Settings;

namespace HyPrism.Desktop.Shell;

public sealed partial class MainWindow : Window
{
    private const double WideNewsLayoutThreshold = 1180;

    private INotifyPropertyChanged? _observedViewModel;
    private bool? _usesWideNewsLayout;
    private int _wideArticleTransitionVersion;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _observedViewModel = DataContext as INotifyPropertyChanged;
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged += OnViewModelPropertyChanged;

        if (DataContext is MainWindowViewModel viewModel && _usesWideNewsLayout is { } useWideLayout)
            viewModel.IsWideNewsLayout = useWideLayout;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedNewsArticle) &&
            DataContext is MainWindowViewModel { SelectedNewsArticle: not null } viewModel)
        {
            var transitionVersion = ++_wideArticleTransitionVersion;
            var transitions = WideArticleHost.Transitions;
            if (viewModel.IsWideNewsLayout)
            {
                // Hide the newly-bound tree without animating the old content out. The
                // first visible fade therefore starts only after the hero and its mask
                // have both completed a render pass.
                WideArticleHost.Transitions = null;
                WideArticleHost.Opacity = 0;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (transitionVersion != _wideArticleTransitionVersion)
                    return;

                foreach (var scrollViewer in CompactArticleHost
                             .GetVisualDescendants()
                             .OfType<ScrollViewer>())
                {
                    scrollViewer.ScrollToHome();
                }

                foreach (var scrollViewer in WideArticleHost
                             .GetVisualDescendants()
                             .OfType<ScrollViewer>())
                {
                    scrollViewer.ScrollToHome();
                }

                if (viewModel.IsWideNewsLayout)
                {
                    WideArticleHost.Transitions = transitions;
                    WideArticleHost.Opacity = 1;
                }
            }, DispatcherPriority.Background);
        }
    }

    private void OnNewsResponsiveSizeChanged(object? sender, SizeChangedEventArgs e)
        => UpdateNewsResponsiveLayout(e.NewSize.Width >= WideNewsLayoutThreshold);

    private void UpdateNewsResponsiveLayout(bool useWideLayout)
    {
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.IsWideNewsLayout = useWideLayout;

        if (_usesWideNewsLayout == useWideLayout)
            return;

        _usesWideNewsLayout = useWideLayout;
        CompactNewsShell.IsVisible = !useWideLayout;
        WideNewsShell.IsVisible = useWideLayout;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape &&
            DataContext is MainWindowViewModel { IsInstances: true } &&
            this.GetVisualDescendants()
                .OfType<InstancesView>()
                .FirstOrDefault()
                ?.TryNavigateBack() == true)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape &&
            DataContext is MainWindowViewModel { IsSettings: true } &&
            this.GetVisualDescendants()
                .OfType<SettingsView>()
                .FirstOrDefault()
                ?.TryCloseCompactContent() == true)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape &&
            DataContext is MainWindowViewModel { IsNews: true, HasSelectedNewsItem: true } viewModel)
        {
            viewModel.CloseNewsArticleCommand.Execute(null);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.ClickCount == 2)
            ToggleMaximized();
        else
            BeginMoveDrag(e);
    }

    private void OnMinimizeClicked(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaximizeClicked(object? sender, RoutedEventArgs e)
        => ToggleMaximized();

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
        => Close();

    private void ToggleMaximized()
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void BeginResize(WindowEdge edge, PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.Normal &&
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginResizeDrag(edge, e);
        }
    }

    private void OnResizeNorth(object? sender, PointerPressedEventArgs e)
        => BeginResize(WindowEdge.North, e);

    private void OnResizeSouth(object? sender, PointerPressedEventArgs e)
        => BeginResize(WindowEdge.South, e);

    private void OnResizeWest(object? sender, PointerPressedEventArgs e)
        => BeginResize(WindowEdge.West, e);

    private void OnResizeEast(object? sender, PointerPressedEventArgs e)
        => BeginResize(WindowEdge.East, e);

    private void OnResizeNorthWest(object? sender, PointerPressedEventArgs e)
        => BeginResize(WindowEdge.NorthWest, e);

    private void OnResizeNorthEast(object? sender, PointerPressedEventArgs e)
        => BeginResize(WindowEdge.NorthEast, e);

    private void OnResizeSouthWest(object? sender, PointerPressedEventArgs e)
        => BeginResize(WindowEdge.SouthWest, e);

    private void OnResizeSouthEast(object? sender, PointerPressedEventArgs e)
        => BeginResize(WindowEdge.SouthEast, e);
}
