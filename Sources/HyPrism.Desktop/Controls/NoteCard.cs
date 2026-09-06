// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;

namespace HyPrism.Desktop.Controls;

/// <summary>
/// Presents an icon, a title, and supporting content inside an accent-colored callout card.
/// Style the card with the "note" or "important" class to pick its accent color and icon.
/// </summary>
public sealed class NoteCard : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<NoteCard, string?>(nameof(Title));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
}
