// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Controls;
using Avalonia.Labs.Lottie;
using Avalonia.Threading;

namespace HyPrism.Desktop.Controls;

/// <summary>
/// Owns the shared lifecycle around a wizard screen, its steps, and reveal animation.
/// </summary>
public sealed class WizardHost
{
    private readonly WizardScreenTransition _transition;
    private readonly Lottie? _revealAnimation;
    private readonly Control[] _steps;

    public WizardHost(
        Control overview,
        Control wizard,
        Control? navigationPane = null,
        Control? layoutAnchor = null,
        Control? layoutMotionTarget = null,
        Lottie? revealAnimation = null,
        params Control[] steps)
    {
        _transition = new WizardScreenTransition(
            overview,
            wizard,
            navigationPane,
            layoutAnchor,
            layoutMotionTarget);
        _revealAnimation = revealAnimation;
        _steps = steps;
    }

    public Task OpenAsync(Func<bool> shouldRemainOpen, Action? onOpened = null)
    {
        NormalizeSteps();
        return _transition.OpenAsync(
            shouldRemainOpen,
            () =>
            {
                RestartReveal();
                onOpened?.Invoke();
            });
    }

    public Task CloseAsync(
        Func<bool> shouldRemainClosed,
        Action? onClosed = null)
        => _transition.CloseAsync(
            shouldRemainClosed,
            () =>
            {
                onClosed?.Invoke();
                NormalizeSteps();
            });

    public Task SwitchStepAsync(
        Control outgoingStep,
        Control incomingStep,
        bool forward,
        Action switchStep,
        Func<bool> shouldRemainOpen)
        => _transition.SwitchStepAsync(
            outgoingStep,
            incomingStep,
            forward,
            switchStep,
            shouldRemainOpen);

    public async Task ShowWizardForCompactEntryAsync()
    {
        NormalizeSteps();
        _transition.ShowWizardImmediately();
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
        RestartReveal();
    }

    public void ShowOverviewImmediately(Action? onClosed = null)
    {
        _transition.ShowOverviewImmediately();
        onClosed?.Invoke();
        NormalizeSteps();
    }

    public void ShowWizardImmediately()
    {
        NormalizeSteps();
        _transition.ShowWizardImmediately();
    }

    public void ShowNavigationPane(bool animate)
        => _transition.ShowNavigationPane(animate);

    public void HideNavigationPane(bool animate)
        => _transition.HideNavigationPane(animate);

    public void ResetNavigationPane()
        => _transition.ResetNavigationPane();

    public void Cancel()
        => _transition.Cancel();

    public void RestartReveal()
    {
        if (_revealAnimation is null)
            return;

        _revealAnimation.SeekToProgress(0);
        _revealAnimation.Start();
    }

    private void NormalizeSteps()
    {
        if (_steps.Length == 0)
            return;

        var activeStep = _steps.FirstOrDefault(step => step.IsVisible) ?? _steps[0];
        _transition.ShowStepImmediately(
            activeStep,
            _steps.Where(step => !ReferenceEquals(step, activeStep)).ToArray());
    }
}
