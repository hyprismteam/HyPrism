// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HyPrism.Desktop.Controls;

public sealed class FadingComboBox : ComboBox
{
    public override bool UpdateSelectionFromEvent(Control container, RoutedEventArgs eventArgs)
    {
        if (eventArgs.Handled)
            return false;

        var containerIndex = IndexFromContainer(container);
        if (containerIndex == -1)
            return false;

        var shouldSelect = eventArgs switch
        {
            PointerEventArgs pointerEvent => ShouldTriggerSelection(container, pointerEvent),
            KeyEventArgs keyEvent => ShouldTriggerSelection(container, keyEvent),
            FocusChangedEventArgs => true,
            _ => false
        };

        if (!shouldSelect)
            return false;

        UpdateSelection(
            containerIndex,
            select: true,
            rangeModifier: ItemSelectionEventTriggers.HasRangeSelectionModifier(container, eventArgs),
            toggleModifier: ItemSelectionEventTriggers.HasToggleSelectionModifier(container, eventArgs),
            rightButton: eventArgs is PointerEventArgs { Properties.IsRightButtonPressed: true },
            fromFocus: eventArgs is FocusChangedEventArgs);

        if (eventArgs is PointerEventArgs)
            container.PerformFeedback(FeedbackAction.Click);

        eventArgs.Handled = true;
        SetCurrentValue(IsDropDownOpenProperty, false);
        return true;
    }

    internal bool IsDropDownInteractionSource(Visual source)
        => ResolvePopup()?.IsInteractionSource(source) ??
           ReferenceEquals(source, this) ||
           this.IsVisualAncestorOf(source);

    private FadingPopup? ResolvePopup()
        => this.GetVisualDescendants().OfType<FadingPopup>().FirstOrDefault();

}
