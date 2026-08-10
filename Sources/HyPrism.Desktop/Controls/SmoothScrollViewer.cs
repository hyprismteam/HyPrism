// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Threading;

namespace HyPrism.Desktop.Controls;

/// <summary>
/// A vertical scroll viewer with eased wheel scrolling and browser-style middle-click auto-scroll.
/// </summary>
public sealed class SmoothScrollViewer : ScrollViewer
{
    public static readonly StyledProperty<bool> IsPastTopProperty =
        AvaloniaProperty.Register<SmoothScrollViewer, bool>(
            nameof(IsPastTop),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> ScrollContextKeyProperty =
        AvaloniaProperty.Register<SmoothScrollViewer, string?>(nameof(ScrollContextKey));

    private const double WheelStep = 92;
    private const double WheelEasing = 0.16;
    private const double AutoScrollDeadZone = 14;
    private const double AutoScrollAcceleration = 0.14;
    private readonly DispatcherTimer _scrollTimer;
    private readonly Cursor _autoScrollIdleCursor = new(StandardCursorType.SizeAll);
    private readonly Cursor _autoScrollUpCursor = new(StandardCursorType.TopSide);
    private readonly Cursor _autoScrollDownCursor = new(StandardCursorType.BottomSide);
    private double _targetY;
    private bool _isAnimating;
    private bool _isAutoScrolling;
    private Point _autoScrollAnchor;
    private double _autoScrollVelocity;
    private double _autoScrollTargetVelocity;
    private Cursor? _previousCursor;
    private IPointer? _capturedPointer;

    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    public bool IsPastTop
    {
        get => GetValue(IsPastTopProperty);
        set => SetValue(IsPastTopProperty, value);
    }

    public string? ScrollContextKey
    {
        get => GetValue(ScrollContextKeyProperty);
        set => SetValue(ScrollContextKeyProperty, value);
    }

    public SmoothScrollViewer()
    {
        _scrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _scrollTimer.Tick += OnScrollTick;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (Extent.Height <= Viewport.Height)
        {
            base.OnPointerWheelChanged(e);
            return;
        }

        StopAutoScroll();
        _targetY = Math.Clamp(
            (_isAnimating ? _targetY : Offset.Y) - e.Delta.Y * WheelStep,
            0,
            Math.Max(0, Extent.Height - Viewport.Height));
        _isAnimating = true;
        EnsureTimerRunning();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsMiddleButtonPressed)
        {
            if (_isAutoScrolling)
            {
                StopAutoScroll();
            }
            else if (Extent.Height > Viewport.Height)
            {
                _isAnimating = false;
                _isAutoScrolling = true;
                _autoScrollAnchor = point.Position;
                _autoScrollVelocity = 0;
                _autoScrollTargetVelocity = 0;
                _targetY = Offset.Y;
                _previousCursor = Cursor;
                Cursor = _autoScrollIdleCursor;
                _capturedPointer = e.Pointer;
                e.Pointer.Capture(this);
                Focus();
                EnsureTimerRunning();
            }

            e.Handled = true;
            return;
        }

        if (_isAutoScrolling)
            StopAutoScroll();

        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_isAutoScrolling)
        {
            var delta = e.GetPosition(this).Y - _autoScrollAnchor.Y;
            var distance = Math.Max(0, Math.Abs(delta) - AutoScrollDeadZone);
            _autoScrollTargetVelocity = distance == 0
                ? 0
                : Math.CopySign(
                    Math.Min(26, Math.Pow(distance / 18, 1.12) * 2.2),
                    delta);
            UpdateAutoScrollCursor();
            e.Handled = true;
            return;
        }

        base.OnPointerMoved(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_isAutoScrolling && e.Key == Key.Escape)
        {
            StopAutoScroll();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopAutoScroll();
        _isAnimating = false;
        _scrollTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == OffsetProperty)
        {
            SetCurrentValue(IsPastTopProperty, Offset.Y > 6);
        }
        else if (change.Property == ScrollContextKeyProperty)
        {
            ResetScrollPosition();
        }
    }

    private void OnScrollTick(object? sender, EventArgs e)
    {
        var maxY = Math.Max(0, Extent.Height - Viewport.Height);
        if (_isAutoScrolling)
        {
            _autoScrollVelocity +=
                (_autoScrollTargetVelocity - _autoScrollVelocity) * AutoScrollAcceleration;
            if (Math.Abs(_autoScrollVelocity) < 0.02 && _autoScrollTargetVelocity == 0)
                _autoScrollVelocity = 0;

            var next = Math.Clamp(Offset.Y + _autoScrollVelocity, 0, maxY);
            Offset = new Vector(Offset.X, next);
            if ((next <= 0 && _autoScrollVelocity < 0) ||
                (next >= maxY && _autoScrollVelocity > 0))
            {
                _autoScrollVelocity = 0;
                _autoScrollTargetVelocity = 0;
                Cursor = _autoScrollIdleCursor;
            }
        }

        if (_isAnimating)
        {
            _targetY = Math.Clamp(_targetY, 0, maxY);
            var delta = _targetY - Offset.Y;
            if (Math.Abs(delta) < 0.5)
            {
                Offset = new Vector(Offset.X, _targetY);
                _isAnimating = false;
            }
            else
            {
                Offset = new Vector(Offset.X, Offset.Y + delta * WheelEasing);
            }
        }

        if (!_isAnimating && !_isAutoScrolling)
            _scrollTimer.Stop();
    }

    private void EnsureTimerRunning()
    {
        if (!_scrollTimer.IsEnabled)
            _scrollTimer.Start();
    }

    private void ResetScrollPosition()
    {
        _isAnimating = false;
        StopAutoScroll();
        _targetY = 0;
        Offset = new Vector(Offset.X, 0);
        SetCurrentValue(IsPastTopProperty, false);
        if (!_isAutoScrolling)
            _scrollTimer.Stop();
    }

    private void StopAutoScroll()
    {
        if (!_isAutoScrolling)
            return;

        _isAutoScrolling = false;
        _autoScrollVelocity = 0;
        _autoScrollTargetVelocity = 0;
        Cursor = _previousCursor;
        _previousCursor = null;
        _capturedPointer?.Capture(null);
        _capturedPointer = null;
        if (!_isAnimating)
            _scrollTimer.Stop();
    }

    private void UpdateAutoScrollCursor()
    {
        if (_autoScrollTargetVelocity < 0)
        {
            Cursor = _autoScrollUpCursor;
        }
        else if (_autoScrollTargetVelocity > 0)
        {
            Cursor = _autoScrollDownCursor;
        }
        else
        {
            Cursor = _autoScrollIdleCursor;
        }
    }
}
