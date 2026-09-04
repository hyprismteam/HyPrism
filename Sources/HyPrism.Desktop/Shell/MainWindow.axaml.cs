// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HyPrism.Desktop.Features.Instances;
using HyPrism.Desktop.Features.Settings;

namespace HyPrism.Desktop.Shell;

public sealed partial class MainWindow : Window
{
    private const double WideNewsLayoutThreshold = 1180;

    private static readonly Dictionary<WindowEdge, Cursor> ResizeCursors = new()
    {
        [WindowEdge.North] = new Cursor(StandardCursorType.SizeNorthSouth),
        [WindowEdge.South] = new Cursor(StandardCursorType.SizeNorthSouth),
        [WindowEdge.East] = new Cursor(StandardCursorType.SizeWestEast),
        [WindowEdge.West] = new Cursor(StandardCursorType.SizeWestEast),
        [WindowEdge.NorthEast] = new Cursor(StandardCursorType.TopRightCorner),
        [WindowEdge.SouthWest] = new Cursor(StandardCursorType.BottomLeftCorner),
        [WindowEdge.NorthWest] = new Cursor(StandardCursorType.TopLeftCorner),
        [WindowEdge.SouthEast] = new Cursor(StandardCursorType.BottomRightCorner)
    };

    private INotifyPropertyChanged? _observedViewModel;
    private bool? _usesWideNewsLayout;
    private int _wideArticleTransitionVersion;
    private int _startupTransitionVersion;
    private bool _startupAnimationFrameActive;
    private TimeSpan? _startupAnimationStartedAt;
    private WindowEdge? _activeResizeEdge;
    private PixelPoint _resizeStartScreenPoint;
    private PixelPoint _pendingResizeScreenPoint;
    private PixelPoint _resizeStartWindowPosition;
    private Size _resizeStartClientSize;
    private Cursor? _resizeCursorSnapshot;
    private bool _isResizeFrameScheduled;
    private readonly Action<TimeSpan> _onResizeFrame;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private ScaleTransform LauncherShellScale =>
        ((TransformGroup)LauncherShell.RenderTransform!).Children.OfType<ScaleTransform>().Single();
    private TranslateTransform LauncherShellTranslation =>
        ((TransformGroup)LauncherShell.RenderTransform!).Children.OfType<TranslateTransform>().Single();
    private ScaleTransform StartupContentScale =>
        (ScaleTransform)StartupLoadingContent.RenderTransform!;
    private ScaleTransform StartupMarkScale =>
        (ScaleTransform)StartupMark.RenderTransform!;

    public MainWindow()
    {
        InitializeComponent();
        _onResizeFrame = ApplyResizeFrame;
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

        if (DataContext is MainWindowViewModel startupViewModel)
            ApplyStartupLoadingState(startupViewModel.IsStartupLoading);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsStartupLoading) &&
            DataContext is MainWindowViewModel startupViewModel)
        {
            ApplyStartupLoadingState(startupViewModel.IsStartupLoading);
        }

        if (e.PropertyName == nameof(MainWindowViewModel.SelectedNewsArticle) &&
            DataContext is MainWindowViewModel { SelectedNewsArticle: not null } viewModel)
        {
            var wideArticleHost = FindVisualByName<ContentControl>("WideArticleHost");
            var compactArticleHost = FindVisualByName<ContentControl>("CompactArticleHost");
            if (wideArticleHost is null || compactArticleHost is null)
                return;

            var transitionVersion = ++_wideArticleTransitionVersion;
            var transitions = wideArticleHost.Transitions;
            if (viewModel.IsWideNewsLayout)
            {
                // Hide the newly-bound tree without animating the old content out. The
                // first visible fade therefore starts only after the hero and its mask
                // have both completed a render pass.
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
    }

    private void ApplyStartupLoadingState(bool isLoading)
    {
        if (isLoading)
        {
            ShowStartupLoading();
            return;
        }

        if (StartupLoadingScreen.IsVisible)
            _ = HideStartupLoadingAsync();
        else
            ShowLauncherImmediately();
    }

    private void ShowStartupLoading()
    {
        _startupTransitionVersion++;
        StartupLoadingScreen.IsVisible = true;
        StartupLoadingScreen.IsHitTestVisible = true;
        StartupLoadingScreen.Opacity = 1;
        LauncherShell.IsHitTestVisible = false;
        LauncherShell.Opacity = 0;
        LauncherShellScale.ScaleX = 0.975;
        LauncherShellScale.ScaleY = 0.975;
        LauncherShellTranslation.Y = 12;
        StartupLoadingContent.Opacity = 0;
        StartupBrand.Opacity = 0;
        StartupContentScale.ScaleX = 0.9;
        StartupContentScale.ScaleY = 0.9;
        StartupMarkScale.ScaleX = 1;
        StartupMarkScale.ScaleY = 1;
        StartupAnimation.Start();
        StartStartupFrameAnimation();

        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is not MainWindowViewModel { IsStartupLoading: true })
                return;

            StartupLoadingContent.Opacity = 1;
            StartupBrand.Opacity = 0.68;
            StartupContentScale.ScaleX = 1;
            StartupContentScale.ScaleY = 1;
        }, DispatcherPriority.Render);
    }

    private async Task HideStartupLoadingAsync()
    {
        var transitionVersion = ++_startupTransitionVersion;
        StartupLoadingScreen.IsHitTestVisible = false;
        StartupLoadingContent.Opacity = 0;
        StartupBrand.Opacity = 0;
        StartupContentScale.ScaleX = 0.96;
        StartupContentScale.ScaleY = 0.96;
        StartupMarkScale.ScaleX = 1.08;
        StartupMarkScale.ScaleY = 1.08;
        StartupLoadingScreen.Opacity = 0;
        LauncherShell.Opacity = 1;
        LauncherShellScale.ScaleX = 1;
        LauncherShellScale.ScaleY = 1;
        LauncherShellTranslation.Y = 0;

        await Task.Delay(440);
        if (transitionVersion != _startupTransitionVersion ||
            DataContext is MainWindowViewModel { IsStartupLoading: true })
        {
            return;
        }

        StartupLoadingScreen.IsVisible = false;
        LauncherShell.IsHitTestVisible = true;
        StartupAnimation.Stop();
        StopStartupFrameAnimation();
    }

    private void ShowLauncherImmediately()
    {
        _startupTransitionVersion++;
        StartupLoadingScreen.IsVisible = false;
        StartupLoadingScreen.IsHitTestVisible = false;
        StartupLoadingScreen.Opacity = 0;
        StartupBrand.Opacity = 0;
        LauncherShell.IsHitTestVisible = true;
        LauncherShell.Opacity = 1;
        LauncherShellScale.ScaleX = 1;
        LauncherShellScale.ScaleY = 1;
        LauncherShellTranslation.Y = 0;
        StartupAnimation.Stop();
        StopStartupFrameAnimation();
    }

    private void StartStartupFrameAnimation()
    {
        if (_startupAnimationFrameActive)
            return;

        _startupAnimationFrameActive = true;
        _startupAnimationStartedAt = null;
        RequestAnimationFrame(UpdateStartupAnimationFrame);
    }

    private void StopStartupFrameAnimation()
    {
        _startupAnimationFrameActive = false;
        _startupAnimationStartedAt = null;
    }

    private void UpdateStartupAnimationFrame(TimeSpan timestamp)
    {
        if (!_startupAnimationFrameActive || !StartupLoadingScreen.IsVisible)
            return;

        _startupAnimationStartedAt ??= timestamp;
        var elapsed = (timestamp - _startupAnimationStartedAt.Value).TotalSeconds;
        StartupDotOne.Opacity = CalculateStartupDotOpacity(elapsed, 0);
        StartupDotTwo.Opacity = CalculateStartupDotOpacity(elapsed, 0.18);
        StartupDotThree.Opacity = CalculateStartupDotOpacity(elapsed, 0.36);
        RequestAnimationFrame(UpdateStartupAnimationFrame);
    }

    private static double CalculateStartupDotOpacity(double elapsed, double offset)
        => 0.22 + Math.Max(0, Math.Sin((elapsed - offset) * Math.PI * 2)) * 0.78;

    private void OnNewsResponsiveSizeChanged(object? sender, SizeChangedEventArgs e)
        => UpdateNewsResponsiveLayout(e.NewSize.Width >= WideNewsLayoutThreshold);

    private void UpdateNewsResponsiveLayout(bool useWideLayout)
    {
        if (DataContext is MainWindowViewModel viewModel)
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape &&
            _activeResizeEdge is not null)
        {
            Position = _resizeStartWindowPosition;
            ClientSize = _resizeStartClientSize;
            EndResize();
            e.Handled = true;
            return;
        }

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
        if (WindowState != WindowState.Normal ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _activeResizeEdge = edge;
        _resizeStartScreenPoint = this.PointToScreen(e.GetCurrentPoint(this).Position);
        _pendingResizeScreenPoint = _resizeStartScreenPoint;
        _resizeStartWindowPosition = Position;
        _resizeStartClientSize = ClientSize;
        _resizeCursorSnapshot = Cursor;
        Cursor = ResizeCursors[edge];
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void UpdateResize(WindowEdge edge, PointerEventArgs e)
    {
        _pendingResizeScreenPoint = this.PointToScreen(e.GetCurrentPoint(this).Position);
        if (_isResizeFrameScheduled)
            return;

        _isResizeFrameScheduled = true;
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_onResizeFrame);
    }

    private void ApplyResizeFrame(TimeSpan timestamp)
    {
        _isResizeFrameScheduled = false;
        if (_activeResizeEdge is not { } edge)
            return;

        var deltaDips = new Vector(
            (_pendingResizeScreenPoint.X - _resizeStartScreenPoint.X) / RenderScaling,
            (_pendingResizeScreenPoint.Y - _resizeStartScreenPoint.Y) / RenderScaling);
        var (newClientSize, positionOffsetDips) = CalculateResize(
            edge,
            _resizeStartClientSize,
            deltaDips,
            MinWidth,
            MaxWidth,
            MinHeight,
            MaxHeight);

        // One atomic move-and-resize per frame: separate Position and ClientSize
        // updates present intermediate window states to DWM and make the surface flicker
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
            return;

        SetWindowPos(
            handle,
            IntPtr.Zero,
            (int)Math.Round(_resizeStartWindowPosition.X + positionOffsetDips.X * RenderScaling),
            (int)Math.Round(_resizeStartWindowPosition.Y + positionOffsetDips.Y * RenderScaling),
            (int)Math.Round(newClientSize.Width * RenderScaling),
            (int)Math.Round(newClientSize.Height * RenderScaling),
            SwpNoZOrder | SwpNoActivate);
    }

    internal static (Size NewClientSize, Vector PositionOffsetDips) CalculateResize(
        WindowEdge edge,
        Size startClientSize,
        Vector dragDeltaDips,
        double minWidth,
        double maxWidth,
        double minHeight,
        double maxHeight)
    {
        var newWidth = startClientSize.Width;
        var newHeight = startClientSize.Height;
        var offsetX = 0.0;
        var offsetY = 0.0;

        switch (edge)
        {
            case WindowEdge.East:
            case WindowEdge.NorthEast:
            case WindowEdge.SouthEast:
                newWidth = ClampLength(startClientSize.Width + dragDeltaDips.X, minWidth, maxWidth);
                break;
            case WindowEdge.West:
            case WindowEdge.NorthWest:
            case WindowEdge.SouthWest:
                newWidth = ClampLength(startClientSize.Width - dragDeltaDips.X, minWidth, maxWidth);
                offsetX = startClientSize.Width - newWidth;
                break;
        }

        switch (edge)
        {
            case WindowEdge.South:
            case WindowEdge.SouthEast:
            case WindowEdge.SouthWest:
                newHeight = ClampLength(startClientSize.Height + dragDeltaDips.Y, minHeight, maxHeight);
                break;
            case WindowEdge.North:
            case WindowEdge.NorthEast:
            case WindowEdge.NorthWest:
                newHeight = ClampLength(startClientSize.Height - dragDeltaDips.Y, minHeight, maxHeight);
                offsetY = startClientSize.Height - newHeight;
                break;
        }

        return (new Size(newWidth, newHeight), new Vector(offsetX, offsetY));
    }

    private static double ClampLength(double value, double min, double max)
        => Math.Clamp(value, min, double.IsNaN(max) || max < min ? min : max);

    private void EndResize()
    {
        if (_activeResizeEdge is null)
            return;

        _activeResizeEdge = null;
        Cursor = _resizeCursorSnapshot;
        _resizeCursorSnapshot = null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_activeResizeEdge is { } edge)
            UpdateResize(edge, e);

        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        EndResize();
        base.OnPointerReleased(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        EndResize();
        base.OnPointerCaptureLost(e);
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
