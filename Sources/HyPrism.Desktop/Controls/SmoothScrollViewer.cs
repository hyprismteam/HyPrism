// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.VisualTree;

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
    private const double WheelEasingPerTick = 0.16;
    private const double TickMilliseconds = 16.0;
    private const double AutoScrollDeadZone = 14;
    private const double AutoScrollAccelerationPerTick = 0.14;
    private const double AutoScrollMaximumVelocity = 1625;
    private const double AutoScrollBaseVelocity = 137.5;
    private const double AutoScrollStopVelocity = 1.25;
    private const double AutoScrollEasingExponent = 1.12;
    private const double AutoScrollEasingScale = 18;
    private readonly Cursor _autoScrollIdleCursor = new(StandardCursorType.SizeAll);
    private readonly Cursor _autoScrollUpCursor = new(StandardCursorType.TopSide);
    private readonly Cursor _autoScrollDownCursor = new(StandardCursorType.BottomSide);
    private double _targetY;
    private bool _isAnimating;
    private bool _isAutoScrolling;
    private bool _isFrameLoopActive;
    private TimeSpan? _lastFrameTimestamp;
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
        EnsureFrameLoop();
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
                EnsureFrameLoop();
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
                    Math.Min(
                        AutoScrollMaximumVelocity,
                        Math.Pow(distance / AutoScrollEasingScale, AutoScrollEasingExponent) * AutoScrollBaseVelocity),
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
        _isFrameLoopActive = false;
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

    private void EnsureFrameLoop()
    {
        if (_isFrameLoopActive)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        _isFrameLoopActive = true;
        _lastFrameTimestamp = null;
        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan timestamp)
    {
        if (!_isFrameLoopActive)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || !this.IsAttachedToVisualTree())
        {
            _isFrameLoopActive = false;
            return;
        }

        var deltaMilliseconds = _lastFrameTimestamp is { } previous
            ? (timestamp - previous).TotalMilliseconds
            : 0.0;
        _lastFrameTimestamp = timestamp;

        var maxY = Math.Max(0, Extent.Height - Viewport.Height);
        if (_isAutoScrolling)
        {
            _autoScrollVelocity += (_autoScrollTargetVelocity - _autoScrollVelocity) *
                                   EasePerFrame(AutoScrollAccelerationPerTick, deltaMilliseconds);
            if (Math.Abs(_autoScrollVelocity) < AutoScrollStopVelocity && _autoScrollTargetVelocity == 0)
                _autoScrollVelocity = 0;

            var next = Math.Clamp(Offset.Y + _autoScrollVelocity * deltaMilliseconds / 1000.0, 0, maxY);
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
                Offset = new Vector(Offset.X, Offset.Y + delta * EasePerFrame(WheelEasingPerTick, deltaMilliseconds));
            }
        }

        if (!_isAnimating && !_isAutoScrolling)
        {
            _isFrameLoopActive = false;
            return;
        }

        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private static double EasePerFrame(double perTickFactor, double deltaMilliseconds)
        => 1 - Math.Pow(1 - perTickFactor, Math.Max(0, deltaMilliseconds) / TickMilliseconds);

    private void ResetScrollPosition()
    {
        _isAnimating = false;
        StopAutoScroll();
        _targetY = 0;
        Offset = new Vector(Offset.X, 0);
        SetCurrentValue(IsPastTopProperty, false);
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
