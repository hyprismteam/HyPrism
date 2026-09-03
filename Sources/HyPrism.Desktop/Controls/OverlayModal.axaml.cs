// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Windows.Input;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HyPrism.Desktop.Controls;

public sealed partial class OverlayModal : UserControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<OverlayModal, bool>(nameof(IsOpen));

    public static readonly StyledProperty<ICommand?> DismissCommandProperty =
        AvaloniaProperty.Register<OverlayModal, ICommand?>(nameof(DismissCommand));

    public static readonly StyledProperty<object?> ModalContentProperty =
        AvaloniaProperty.Register<OverlayModal, object?>(nameof(ModalContent));

    public static readonly StyledProperty<double> SheetMaxWidthProperty =
        AvaloniaProperty.Register<OverlayModal, double>(nameof(SheetMaxWidth), 720);

    public static readonly StyledProperty<double> SheetMaxHeightProperty =
        AvaloniaProperty.Register<OverlayModal, double>(nameof(SheetMaxHeight), 680);

    public static readonly StyledProperty<Thickness> SheetMarginProperty =
        AvaloniaProperty.Register<OverlayModal, Thickness>(nameof(SheetMargin), new Thickness(20, 20, 20, 0));

    public static readonly StyledProperty<double> ShoulderMaxWidthProperty =
        AvaloniaProperty.Register<OverlayModal, double>(nameof(ShoulderMaxWidth), 780);

    public static readonly StyledProperty<Thickness> ShoulderMarginProperty =
        AvaloniaProperty.Register<OverlayModal, Thickness>(nameof(ShoulderMargin), new Thickness(20, 0, 20, 0));

    public static readonly StyledProperty<double> HiddenOffsetProperty =
        AvaloniaProperty.Register<OverlayModal, double>(nameof(HiddenOffset), 720);

    private CancellationTokenSource? _animationCancellation;
    private bool _initialized;

    public OverlayModal()
    {
        InitializeComponent();
        _initialized = true;
        ApplyStateImmediately();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    public event EventHandler? Closed;

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public ICommand? DismissCommand
    {
        get => GetValue(DismissCommandProperty);
        set => SetValue(DismissCommandProperty, value);
    }

    public object? ModalContent
    {
        get => GetValue(ModalContentProperty);
        set => SetValue(ModalContentProperty, value);
    }

    public double SheetMaxWidth
    {
        get => GetValue(SheetMaxWidthProperty);
        set => SetValue(SheetMaxWidthProperty, value);
    }

    public double SheetMaxHeight
    {
        get => GetValue(SheetMaxHeightProperty);
        set => SetValue(SheetMaxHeightProperty, value);
    }

    public Thickness SheetMargin
    {
        get => GetValue(SheetMarginProperty);
        set => SetValue(SheetMarginProperty, value);
    }

    public double ShoulderMaxWidth
    {
        get => GetValue(ShoulderMaxWidthProperty);
        set => SetValue(ShoulderMaxWidthProperty, value);
    }

    public Thickness ShoulderMargin
    {
        get => GetValue(ShoulderMarginProperty);
        set => SetValue(ShoulderMarginProperty, value);
    }

    public double HiddenOffset
    {
        get => GetValue(HiddenOffsetProperty);
        set => SetValue(HiddenOffsetProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (!_initialized)
            return;

        if (change.Property != IsOpenProperty)
            return;

        if (change.GetNewValue<bool>())
            _ = ShowAsync();
        else
            _ = HideAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelAnimation();
        base.OnDetachedFromVisualTree(e);
    }

    private async Task ShowAsync()
    {
        var cancellationToken = ReplaceAnimationCancellation();
        OverlayModalBackdrop.Opacity = 0;
        ((TranslateTransform)OverlayModalSheet.RenderTransform!).Y = HiddenOffset;
        ((ScaleTransform)OverlayModalShoulders.RenderTransform!).ScaleY = 0;
        IsVisible = true;
        IsHitTestVisible = true;

        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
        if (cancellationToken.IsCancellationRequested || !IsOpen)
            return;

        SyncShoulderScaleWithSheetTravel(opening: true);
        OverlayModalBackdrop.Opacity = 1;
        ((TranslateTransform)OverlayModalSheet.RenderTransform!).Y = 0;
        ((ScaleTransform)OverlayModalShoulders.RenderTransform!).ScaleY = 1;
        Focus();
    }

    private async Task HideAsync()
    {
        if (!IsVisible)
            return;

        var cancellationToken = ReplaceAnimationCancellation();
        IsHitTestVisible = false;
        OverlayModalBackdrop.Opacity = 0;
        SyncShoulderScaleWithSheetTravel(opening: false);
        ((TranslateTransform)OverlayModalSheet.RenderTransform!).Y = HiddenOffset;
        ((ScaleTransform)OverlayModalShoulders.RenderTransform!).ScaleY = 0;

        try
        {
            await Task.Delay(MotionDurations.ModalCloseRetention, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (IsOpen)
            return;

        IsVisible = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyStateImmediately()
    {
        CancelAnimation();
        IsVisible = IsOpen;
        IsHitTestVisible = IsOpen;
        OverlayModalBackdrop.Opacity = IsOpen ? 1 : 0;
        ((TranslateTransform)OverlayModalSheet.RenderTransform!).Y = IsOpen ? 0 : HiddenOffset;
        ((ScaleTransform)OverlayModalShoulders.RenderTransform!).ScaleY = IsOpen ? 1 : 0;
    }

    /// <summary>
    /// Aligns the shoulder scale animation with the moment the sheet visually crosses
    /// the window edge, so the shoulders finish exactly when the sheet arrives or leaves.
    /// </summary>
    private void SyncShoulderScaleWithSheetTravel(bool opening)
    {
        if (OverlayModalShoulders.RenderTransform is not ScaleTransform transform ||
            transform.Transitions?.OfType<DoubleTransition>().FirstOrDefault() is not { } transition)
            return;

        var sheetHeight = OverlayModalSheet.Bounds.Height;
        var visibleFraction = sheetHeight <= 0
            ? 1
            : Math.Clamp(sheetHeight / HiddenOffset, 0, 1);

        if (opening)
        {
            var enterProgress = InverseCubicEaseInOut(1 - visibleFraction);
            transition.Delay = MotionDurations.ModalCloseRetention * enterProgress;
            transition.Duration = MotionDurations.ModalCloseRetention * (1 - enterProgress);
        }
        else
        {
            transition.Delay = TimeSpan.Zero;
            transition.Duration = MotionDurations.ModalCloseRetention * InverseCubicEaseInOut(visibleFraction);
        }
    }

    private static double InverseCubicEaseInOut(double value)
    {
        if (value <= 0)
            return 0;

        if (value >= 1)
            return 1;

        return value < 0.5
            ? Math.Cbrt(value / 4)
            : 1 - (Math.Cbrt(2 * (1 - value)) / 2);
    }

    private CancellationToken ReplaceAnimationCancellation()
    {
        CancelAnimation();
        _animationCancellation = new CancellationTokenSource();
        return _animationCancellation.Token;
    }

    private void CancelAnimation()
    {
        _animationCancellation?.Cancel();
        _animationCancellation?.Dispose();
        _animationCancellation = null;
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!TryDismiss())
            return;

        args.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || !TryDismiss())
            return;

        args.Handled = true;
    }

    private bool TryDismiss()
    {
        if (!IsOpen || DismissCommand?.CanExecute(null) != true)
            return false;

        DismissCommand.Execute(null);
        return true;
    }
}
