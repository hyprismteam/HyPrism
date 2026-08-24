// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace HyPrism.Desktop.Controls;

/// <summary>
/// Provides reusable continuous rotation for loading visuals.
/// </summary>
public sealed class RotatingVisual : AvaloniaObject
{
    public static readonly AttachedProperty<bool> IsActiveProperty =
        AvaloniaProperty.RegisterAttached<RotatingVisual, Visual, bool>("IsActive");

    private static readonly ConditionalWeakTable<Visual, RotationState> States = new();

    static RotatingVisual()
    {
        IsActiveProperty.Changed.AddClassHandler<Visual>(OnIsActiveChanged);
    }

    private RotatingVisual()
    {
    }

    public static bool GetIsActive(Visual visual)
        => visual.GetValue(IsActiveProperty);

    public static void SetIsActive(Visual visual, bool value)
        => visual.SetValue(IsActiveProperty, value);

    private static void OnIsActiveChanged(Visual visual, AvaloniaPropertyChangedEventArgs args)
    {
        var state = States.GetOrCreateValue(visual);
        state.SetActive(visual, args.GetNewValue<bool>());
    }

    private sealed class RotationState
    {
        private bool _isActive;
        private bool _eventsAttached;
        private int _animationVersion;
        private TimeSpan? _animationStartedAt;
        private Visual? _visual;

        public void SetActive(Visual visual, bool isActive)
        {
            _isActive = isActive;
            EnsureEventsAttached(visual);
            if (isActive && visual.IsAttachedToVisualTree())
                Start(visual);
            else
                Stop(visual);
        }

        private void EnsureEventsAttached(Visual visual)
        {
            if (_eventsAttached)
                return;

            visual.AttachedToVisualTree += OnAttachedToVisualTree;
            visual.DetachedFromVisualTree += OnDetachedFromVisualTree;
            _eventsAttached = true;
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
        {
            if (_isActive && sender is Visual visual)
                Start(visual);
        }

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
        {
            if (sender is Visual visual)
                Stop(visual);
        }

        private void Start(Visual visual)
        {
            Stop(visual);
            visual.RenderTransformOrigin = RelativePoint.Center;
            visual.RenderTransform ??= new RotateTransform();
            if (visual.RenderTransform is not RotateTransform rotation)
                return;

            _visual = visual;
            _animationStartedAt = null;
            var animationVersion = ++_animationVersion;
            TopLevel.GetTopLevel(visual)?.RequestAnimationFrame(
                timestamp => OnAnimationFrame(timestamp, animationVersion));
        }

        private void Stop(Visual visual)
        {
            _animationVersion++;
            _animationStartedAt = null;
            _visual = null;
            if (visual.RenderTransform is RotateTransform rotation)
                rotation.Angle = 0;
        }

        private void OnAnimationFrame(TimeSpan timestamp, int animationVersion)
        {
            var visual = _visual;
            if (animationVersion != _animationVersion)
                return;

            if (!_isActive ||
                visual is null ||
                !visual.IsAttachedToVisualTree())
            {
                if (visual is not null)
                    Stop(visual);
                return;
            }

            if (visual.IsEffectivelyVisible && visual.RenderTransform is RotateTransform rotation)
            {
                _animationStartedAt ??= timestamp;
                var elapsed = timestamp - _animationStartedAt.Value;
                rotation.Angle = elapsed.TotalMilliseconds %
                                 MotionDurations.SpinnerRotation.TotalMilliseconds /
                                 MotionDurations.SpinnerRotation.TotalMilliseconds * 360;
            }
            else
            {
                _animationStartedAt = null;
            }

            TopLevel.GetTopLevel(visual)?.RequestAnimationFrame(
                nextTimestamp => OnAnimationFrame(nextTimestamp, animationVersion));
        }
    }
}
