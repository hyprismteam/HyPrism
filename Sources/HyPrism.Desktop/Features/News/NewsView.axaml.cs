// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HyPrism.Desktop.Controls;

namespace HyPrism.Desktop.Features.News;

public sealed partial class NewsView : UserControl
{
    private const double WideNewsLayoutThreshold = 1180;

    private readonly AdaptiveMasterDetailHost _layoutHost;
    private INotifyPropertyChanged? _observedViewModel;
    private int _wideArticleTransitionVersion;
    private bool? _usesWideNewsLayout;

    public NewsView()
    {
        InitializeComponent();
        _layoutHost = new AdaptiveMasterDetailHost(
            CompactNewsShell,
            CompactNewsFeedBackground,
            CompactArticleHost,
            compactBreakpoint: WideNewsLayoutThreshold);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _observedViewModel = DataContext as INotifyPropertyChanged;
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged += OnViewModelPropertyChanged;

        _usesWideNewsLayout = null;
        UpdateNewsLayout(Bounds.Width);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is not NewsViewModel viewModel)
            return;

        if (e.PropertyName == nameof(NewsViewModel.SelectedNewsItem) &&
            viewModel.SelectedNewsItem is not null)
        {
            _layoutHost.RememberDetail();
            if (_layoutHost.IsCompact)
                _layoutHost.OpenDetail();
        }

        if (e.PropertyName == nameof(NewsViewModel.IsCompactNewsArticleClosing) &&
            viewModel.IsCompactNewsArticleClosing &&
            _layoutHost.IsCompact)
        {
            _layoutHost.TryCloseDetail();
        }

        if (e.PropertyName != nameof(NewsViewModel.SelectedNewsArticle) ||
            viewModel.SelectedNewsArticle is null)
        {
            return;
        }

        var wideArticleHost = FindVisualByName<ContentControl>("WideArticleHost");
        var compactArticleHost = FindVisualByName<ContentControl>("CompactArticleHost");
        if (wideArticleHost is null || compactArticleHost is null)
            return;

        var transitionVersion = ++_wideArticleTransitionVersion;
        var transitions = wideArticleHost.Transitions;
        if (viewModel.IsWideNewsLayout)
        {
            wideArticleHost.Transitions = null;
            wideArticleHost.Opacity = 0;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (transitionVersion != _wideArticleTransitionVersion)
                return;

            foreach (var scrollViewer in compactArticleHost
                         .GetVisualDescendants()
                         .OfType<ScrollViewer>())
            {
                scrollViewer.ScrollToHome();
            }

            foreach (var scrollViewer in wideArticleHost
                         .GetVisualDescendants()
                         .OfType<ScrollViewer>())
            {
                scrollViewer.ScrollToHome();
            }

            if (viewModel.IsWideNewsLayout)
            {
                wideArticleHost.Transitions = transitions;
                wideArticleHost.Opacity = 1;
            }
        }, DispatcherPriority.Background);
    }

    private void OnNewsResponsiveSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateNewsLayout(e.NewSize.Width);
    }

    private void UpdateNewsLayout(double width)
    {
        if (width <= 0 || DataContext is not NewsViewModel viewModel)
            return;

        _layoutHost.Update(width, hasMaster: true);
        var useWideLayout = !_layoutHost.IsCompact;
        viewModel.IsWideNewsLayout = useWideLayout;
        if (viewModel.SelectedNewsItem is not null &&
            _layoutHost.IsCompact &&
            !_layoutHost.IsDetailOpen)
        {
            _layoutHost.OpenDetail();
        }

        if (_usesWideNewsLayout == useWideLayout)
            return;

        _usesWideNewsLayout = useWideLayout;
        var compactNewsShell = FindVisualByName<Grid>("CompactNewsShell");
        var wideNewsShell = FindVisualByName<Grid>("WideNewsShell");
        if (compactNewsShell is not null)
            compactNewsShell.IsVisible = !useWideLayout;
        if (wideNewsShell is not null)
            wideNewsShell.IsVisible = useWideLayout;
    }

    private async void OnCompactNewsBackClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NewsViewModel viewModel)
            return;

        _layoutHost.TryCloseDetail();
        await viewModel.CloseNewsArticleCommand.ExecuteAsync(null);
    }

    private T? FindVisualByName<T>(string name)
        where T : Control
        => this.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(control => string.Equals(control.Name, name, StringComparison.Ordinal));
}
