// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HyPrism.Desktop.Controls;

public sealed class FadingPopup : Popup
{
    private static readonly TimeSpan CloseRetentionDuration = MotionDurations.PopupCloseRetention;

    public static readonly StyledProperty<bool> IsRequestedOpenProperty =
        AvaloniaProperty.Register<FadingPopup, bool>(nameof(IsRequestedOpen));

    private CancellationTokenSource? _animationCancellation;
    private TopLevel? _subscribedTopLevel;
    private Window? _subscribedWindow;

    public FadingPopup()
    {
        IsLightDismissEnabled = false;
        WindowManagerAddShadowHint = false;
    }

    public bool IsRequestedOpen
    {
        get => GetValue(IsRequestedOpenProperty);
        set => SetValue(IsRequestedOpenProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != IsRequestedOpenProperty)
            return;

        if (change.GetNewValue<bool>())
            ShowPopup();
        else
            BeginHidePopup();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelPendingAnimation();
        UnsubscribeFromTopLevel();
        SetCurrentValue(IsOpenProperty, false);
        if (Child is not null)
            Child.Opacity = 1;
        base.OnDetachedFromVisualTree(e);
    }

    internal bool IsInteractionSource(Visual source)
    {
        if (PlacementTarget is Visual target &&
            (ReferenceEquals(source, target) || target.IsVisualAncestorOf(source)))
        {
            return true;
        }

        return Child is not null &&
               (ReferenceEquals(source, Child) || Child.IsVisualAncestorOf(source));
    }

    private void ShowPopup()
    {
        CancelPendingAnimation();
        if (Child is not null)
            Child.Opacity = 0;

        SetCurrentValue(IsOpenProperty, true);
        SubscribeToTopLevel();
        _animationCancellation = new CancellationTokenSource();
        _ = PlayOpenAnimationAsync(_animationCancellation.Token);
    }

    private void BeginHidePopup()
    {
        UnsubscribeFromTopLevel();
        if (!IsOpen)
            return;

        CancelPendingAnimation();
        if (Child is not null)
            Child.Opacity = 0;

        _animationCancellation = new CancellationTokenSource();
        _ = PlayCloseAnimationAndHideAsync(_animationCancellation.Token);
    }

    private async Task PlayOpenAnimationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            if (!cancellationToken.IsCancellationRequested && IsRequestedOpen && Child is not null)
                Child.Opacity = 1;
        }
        catch (OperationCanceledException)
        {
            // A close request replaces the opening animation
        }
    }

    private async Task PlayCloseAnimationAndHideAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(CloseRetentionDuration, cancellationToken);
            if (cancellationToken.IsCancellationRequested || IsRequestedOpen)
                return;

            SetCurrentValue(IsOpenProperty, false);
            if (Child is not null)
                Child.Opacity = 1;
        }
        catch (OperationCanceledException)
        {
            // Reopening the popup cancels the pending visual close
        }
    }

    private void SubscribeToTopLevel()
    {
        var topLevel = TopLevel.GetTopLevel(PlacementTarget ?? this);
        if (ReferenceEquals(topLevel, _subscribedTopLevel))
            return;

        UnsubscribeFromTopLevel();
        _subscribedTopLevel = topLevel;
        _subscribedTopLevel?.AddHandler(
            PointerPressedEvent,
            OnTopLevelPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _subscribedTopLevel?.AddHandler(
            KeyDownEvent,
            OnTopLevelKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        if (topLevel is Window window)
        {
            _subscribedWindow = window;
            _subscribedWindow.Deactivated += OnWindowDeactivated;
        }
    }

    private void UnsubscribeFromTopLevel()
    {
        _subscribedTopLevel?.RemoveHandler(PointerPressedEvent, OnTopLevelPointerPressed);
        _subscribedTopLevel?.RemoveHandler(KeyDownEvent, OnTopLevelKeyDown);
        if (_subscribedWindow is not null)
            _subscribedWindow.Deactivated -= OnWindowDeactivated;

        _subscribedTopLevel = null;
        _subscribedWindow = null;
    }

    private void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!IsRequestedOpen || args.Source is not Visual source || IsInteractionSource(source))
            return;

        SetCurrentValue(IsRequestedOpenProperty, false);
    }

    private void OnTopLevelKeyDown(object? sender, KeyEventArgs args)
    {
        if (!IsRequestedOpen || args.Key != Key.Escape)
            return;

        SetCurrentValue(IsRequestedOpenProperty, false);
        args.Handled = true;
    }

    private void OnWindowDeactivated(object? sender, EventArgs args)
        => SetCurrentValue(IsRequestedOpenProperty, false);

    private void CancelPendingAnimation()
    {
        _animationCancellation?.Cancel();
        _animationCancellation?.Dispose();
        _animationCancellation = null;
    }
}
