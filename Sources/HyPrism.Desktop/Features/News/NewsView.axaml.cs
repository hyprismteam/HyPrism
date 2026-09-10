// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HyPrism.Desktop.Features.News;

public sealed partial class NewsView : UserControl
{
    private const double WideNewsLayoutThreshold = 1180;

    private INotifyPropertyChanged? _observedViewModel;
    private int _wideArticleTransitionVersion;
    private bool? _usesWideNewsLayout;

    public NewsView()
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

        if (DataContext is NewsViewModel viewModel && _usesWideNewsLayout is { } useWideLayout)
            viewModel.IsWideNewsLayout = useWideLayout;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NewsViewModel.SelectedNewsArticle) ||
            DataContext is not NewsViewModel { SelectedNewsArticle: not null } viewModel)
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
        if (DataContext is not NewsViewModel viewModel)
            return;

        var useWideLayout = e.NewSize.Width >= WideNewsLayoutThreshold;
        viewModel.IsWideNewsLayout = useWideLayout;
        if (_usesWideNewsLayout == useWideLayout)
            return;

        _usesWideNewsLayout = useWideLayout;
        var compactNewsShell = FindVisualByName<Carousel>("CompactNewsShell");
        var wideNewsShell = FindVisualByName<Grid>("WideNewsShell");
        if (compactNewsShell is not null)
            compactNewsShell.IsVisible = !useWideLayout;
        if (wideNewsShell is not null)
            wideNewsShell.IsVisible = useWideLayout;
    }

    private T? FindVisualByName<T>(string name)
        where T : Control
        => this.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(control => string.Equals(control.Name, name, StringComparison.Ordinal));
}
