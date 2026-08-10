// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HyPrism.Desktop.Controls;

[PseudoClasses(ClosingPseudoClass)]
public sealed class FadingComboBox : ComboBox
{
    private const string ClosingPseudoClass = ":dropdownclosing";
    private static readonly TimeSpan CloseRetentionDuration = TimeSpan.FromMilliseconds(210);

    public static readonly DirectProperty<FadingComboBox, bool> IsPopupVisibleProperty =
        AvaloniaProperty.RegisterDirect<FadingComboBox, bool>(
            nameof(IsPopupVisible),
            control => control.IsPopupVisible);

    private CancellationTokenSource? _animationCancellation;
    private Border? _popupBorder;
    private TopLevel? _subscribedTopLevel;
    private Window? _subscribedWindow;
    private bool _isPopupVisible;

    public bool IsPopupVisible
    {
        get => _isPopupVisible;
        private set => SetAndRaise(IsPopupVisibleProperty, ref _isPopupVisible, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _popupBorder = e.NameScope.Find<Popup>("PART_Popup")?.Child as Border;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != IsDropDownOpenProperty)
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
        PseudoClasses.Set(ClosingPseudoClass, false);
        IsPopupVisible = false;
        _popupBorder = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void ShowPopup()
    {
        CancelPendingAnimation();
        PseudoClasses.Set(ClosingPseudoClass, false);
        var popupBorder = ResolvePopupBorder();
        if (popupBorder is not null)
            popupBorder.Opacity = 0;

        IsPopupVisible = true;
        SubscribeToTopLevel();
        _animationCancellation = new CancellationTokenSource();
        _ = PlayOpenAnimationAsync(_animationCancellation.Token);
    }

    private void BeginHidePopup()
    {
        UnsubscribeFromTopLevel();

        if (!IsPopupVisible)
            return;

        CancelPendingAnimation();
        PseudoClasses.Set(ClosingPseudoClass, true);
        _animationCancellation = new CancellationTokenSource();
        _ = PlayCloseAnimationAndHideAsync(_animationCancellation.Token);
    }

    private async Task PlayOpenAnimationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            var popupBorder = ResolvePopupBorder();
            if (popupBorder is null || cancellationToken.IsCancellationRequested)
                return;

            if (!cancellationToken.IsCancellationRequested && IsDropDownOpen)
                popupBorder.Opacity = 1;
        }
        catch (OperationCanceledException)
        {
            // A close request replaces the opening animation.
        }
    }

    private async Task PlayCloseAnimationAndHideAsync(CancellationToken cancellationToken)
    {
        try
        {
            var popupBorder = ResolvePopupBorder();
            if (popupBorder is not null)
                popupBorder.Opacity = 0;

            await Task.Delay(CloseRetentionDuration, cancellationToken);

            if (cancellationToken.IsCancellationRequested || IsDropDownOpen)
                return;

            IsPopupVisible = false;
            PseudoClasses.Set(ClosingPseudoClass, false);
            if (popupBorder is not null)
                popupBorder.Opacity = 1;
        }
        catch (OperationCanceledException)
        {
            // Reopening the list cancels the pending visual close.
        }
    }

    private void SubscribeToTopLevel()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (ReferenceEquals(topLevel, _subscribedTopLevel))
            return;

        UnsubscribeFromTopLevel();
        _subscribedTopLevel = topLevel;
        _subscribedTopLevel?.AddHandler(
            PointerPressedEvent,
            OnTopLevelPointerPressed,
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
        if (_subscribedWindow is not null)
            _subscribedWindow.Deactivated -= OnWindowDeactivated;

        _subscribedTopLevel = null;
        _subscribedWindow = null;
    }

    private void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsDropDownOpen || e.Source is not Visual source)
            return;

        if (ReferenceEquals(source, this) || this.IsVisualAncestorOf(source))
            return;

        SetCurrentValue(IsDropDownOpenProperty, false);
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
        => SetCurrentValue(IsDropDownOpenProperty, false);

    private void CancelPendingAnimation()
    {
        _animationCancellation?.Cancel();
        _animationCancellation?.Dispose();
        _animationCancellation = null;
    }

    private Border? ResolvePopupBorder()
    {
        _popupBorder ??= this.GetVisualDescendants()
            .OfType<Popup>()
            .Select(popup => popup.Child)
            .OfType<Border>()
            .FirstOrDefault();
        return _popupBorder;
    }

}
