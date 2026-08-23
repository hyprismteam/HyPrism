// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Labs.Lottie;

namespace HyPrism.Desktop.Controls;

public sealed partial class WizardRevealIcon : Border
{
    public static readonly StyledProperty<string?> AnimationPathProperty =
        AvaloniaProperty.Register<WizardRevealIcon, string?>(nameof(AnimationPath));
    public static readonly StyledProperty<double> PlaybackRateProperty =
        AvaloniaProperty.Register<WizardRevealIcon, double>(nameof(PlaybackRate), 2);

    public WizardRevealIcon()
    {
        InitializeComponent();
    }

    public string? AnimationPath
    {
        get => GetValue(AnimationPathProperty);
        set => SetValue(AnimationPathProperty, value);
    }

    public double PlaybackRate
    {
        get => GetValue(PlaybackRateProperty);
        set => SetValue(PlaybackRateProperty, value);
    }

    public Control Anchor => this;
    public Border MotionTarget => AnimationMotionTarget;
    public Lottie Animation => AnimationPlayer;
    internal bool LastSelectionWasAnimated { get; private set; }

    public void Play(string animationPath)
    {
        LastSelectionWasAnimated = true;
        AnimationPlayer.Stop();
        Select(animationPath);
        AnimationPlayer.SeekToProgress(0);
        AnimationPlayer.Start();
    }

    public void ShowFinalFrame(string animationPath)
    {
        LastSelectionWasAnimated = false;
        var autoPlay = AnimationPlayer.AutoPlay;
        AnimationPlayer.AutoPlay = false;
        try
        {
            AnimationPlayer.Stop();
            Select(animationPath);
            AnimationPlayer.Start();
            AnimationPlayer.SeekToProgress(0.999f);
            AnimationPlayer.Pause();
        }
        finally
        {
            AnimationPlayer.AutoPlay = autoPlay;
        }
    }

    internal void Select(string animationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(animationPath);
        SetCurrentValue(AnimationPathProperty, animationPath);
    }
}
