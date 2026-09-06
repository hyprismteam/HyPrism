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

    /// <summary>
    /// Starts building the deferred content before the section is activated.
    /// Invisible controls are never measured, so Avalonia only applies the
    /// content template once the control is visible; the warm-up therefore
    /// shows the control for one layout pass while the loading screen covers it
    /// </summary>
    public void BeginPreWarm()
    {
        if (_isContentRealized)
        {
            SetCurrentValue(IsVisibleProperty, true);
            return;
        }

        _isContentRealized = true;
        SetCurrentValue(ContentProperty, DeferredContent);
        SetCurrentValue(IsVisibleProperty, true);
    }

    /// <summary>
    /// Hides the control after a completed pre-warm pass, keeping already
    /// active sections visible
    /// </summary>
    public void EndPreWarm()
    {
        if (!IsActive)
            SetCurrentValue(IsVisibleProperty, false);
    }
}
