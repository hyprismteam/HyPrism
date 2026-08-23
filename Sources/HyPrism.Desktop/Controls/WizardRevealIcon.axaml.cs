// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Labs.Lottie;

namespace HyPrism.Desktop.Controls;

public sealed partial class WizardRevealIcon : UserControl
{
    public static readonly StyledProperty<string?> AnimationPathProperty =
        AvaloniaProperty.Register<WizardRevealIcon, string?>(nameof(AnimationPath));

    public WizardRevealIcon()
    {
        InitializeComponent();
    }

    public string? AnimationPath
    {
        get => GetValue(AnimationPathProperty);
        set => SetValue(AnimationPathProperty, value);
    }

    public Control Anchor => this;
    public Border MotionTarget => AnimationMotionTarget;
    public Lottie Animation => AnimationPlayer;
}
