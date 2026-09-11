// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Desktop.Controls;

/// <summary>
/// Defines semantic motion timings shared by desktop controls and feature views.
/// </summary>
public static class MotionDurations
{
    public static readonly TimeSpan ImmediateFeedback = TimeSpan.FromMilliseconds(160);
    public static readonly TimeSpan VersionLoadingFade = TimeSpan.FromMilliseconds(170);
    public static readonly TimeSpan ContentFade = TimeSpan.FromMilliseconds(180);
    public static readonly TimeSpan WizardPhase = TimeSpan.FromMilliseconds(190);
    public static readonly TimeSpan PopupCloseRetention = TimeSpan.FromMilliseconds(210);
    public static readonly TimeSpan ModalCloseRetention = TimeSpan.FromMilliseconds(320);
    public static readonly TimeSpan TextReplacement = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan CompactSectionSlide = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan CompactPageSlide = TimeSpan.FromMilliseconds(320);
    public static readonly TimeSpan SpinnerRotation = TimeSpan.FromMilliseconds(800);
}
