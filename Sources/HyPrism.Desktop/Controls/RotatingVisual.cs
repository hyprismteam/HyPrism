// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace HyPrism.Desktop.Controls;

/// <summary>
/// Provides a reusable render-clock-driven rotation for loading visuals.
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
        private CancellationTokenSource? _cancellation;
        private bool _isActive;
        private bool _eventsAttached;

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

            _cancellation = new CancellationTokenSource();
            _ = RunAsync(rotation, _cancellation.Token);
        }

        private void Stop(Visual visual)
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;
            if (visual.RenderTransform is RotateTransform rotation)
                rotation.Angle = 0;
        }

        private static async Task RunAsync(RotateTransform rotation, CancellationToken cancellationToken)
        {
            var animation = new Animation
            {
                Duration = MotionDurations.SpinnerRotation,
                IterationCount = IterationCount.Infinite,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters = { new Setter(RotateTransform.AngleProperty, 0d) }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters = { new Setter(RotateTransform.AngleProperty, 360d) }
                    }
                }
            };

            try
            {
                await animation.RunAsync(rotation, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Stopping or restarting the spinner replaces the running animation
            }
        }
    }
}
