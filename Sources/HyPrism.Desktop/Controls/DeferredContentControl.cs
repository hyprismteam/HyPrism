// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;

namespace HyPrism.Desktop.Controls;

/// <summary>
/// Realizes its content only after the corresponding application section becomes active.
/// </summary>
public sealed class DeferredContentControl : ContentControl
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<DeferredContentControl, bool>(nameof(IsActive));

    public static readonly StyledProperty<object?> DeferredContentProperty =
        AvaloniaProperty.Register<DeferredContentControl, object?>(nameof(DeferredContent));

    private bool _isContentRealized;

    public DeferredContentControl()
    {
        SetCurrentValue(IsVisibleProperty, false);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public object? DeferredContent
    {
        get => GetValue(DeferredContentProperty);
        set => SetValue(DeferredContentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsActiveProperty)
        {
            UpdateActiveState();
            return;
        }

        if (change.Property == DeferredContentProperty && _isContentRealized)
            SetCurrentValue(ContentProperty, DeferredContent);
    }

    private void UpdateActiveState()
    {
        SetCurrentValue(IsVisibleProperty, IsActive);
        if (!IsActive || _isContentRealized)
            return;

        _isContentRealized = true;
        SetCurrentValue(ContentProperty, DeferredContent);
    }
}
