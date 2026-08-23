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
    private readonly WizardRevealIcon? _revealIcon;
    private readonly IReadOnlyDictionary<Control, string> _stepAnimationPaths;
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
        _stepAnimationPaths = new Dictionary<Control, string>();
        _steps = steps;
    }

    public WizardHost(
        Control overview,
        Control wizard,
        Control? navigationPane,
        WizardRevealIcon revealIcon,
        params WizardStepDefinition[] steps)
        : this(
            overview,
            wizard,
            navigationPane,
            revealIcon.Anchor,
            revealIcon.MotionTarget,
            revealIcon.Animation,
            steps.Select(step => step.Content).ToArray())
    {
        _revealIcon = revealIcon;
        _stepAnimationPaths = steps.ToDictionary(
            step => step.Content,
            step => step.AnimationPath);
    }

    public Task OpenAsync(Func<bool> shouldRemainOpen, Action? onOpened = null)
    {
        NormalizeSteps();
        SelectActiveStepAnimation();
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

    public async Task SwitchStepAsync(
        Control outgoingStep,
        Control incomingStep,
        bool forward,
        Action switchStep,
        Func<bool> shouldRemainOpen)
    {
        var completed = await _transition.SwitchStepAsync(
            outgoingStep,
            incomingStep,
            forward,
            switchStep,
            shouldRemainOpen);
        if (completed && shouldRemainOpen())
        {
            if (!forward && ReferenceEquals(incomingStep, _steps.FirstOrDefault()))
                ShowStepAnimationFinalFrame(incomingStep);
            else
                PlayStepAnimation(incomingStep);
        }
    }

    public async Task ShowWizardForCompactEntryAsync()
    {
        NormalizeSteps();
        SelectActiveStepAnimation();
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
        SelectActiveStepAnimation();
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
        var activeStep = _steps.FirstOrDefault(step => step.IsVisible);
        if (activeStep is not null && PlayStepAnimation(activeStep))
            return;

        if (_revealAnimation is null)
            return;

        _revealAnimation.SeekToProgress(0);
        _revealAnimation.Start();
    }

    private bool PlayStepAnimation(Control step)
    {
        if (_revealIcon is null || !_stepAnimationPaths.TryGetValue(step, out var animationPath))
            return false;

        _revealIcon.Play(animationPath);
        return true;
    }

    private void ShowStepAnimationFinalFrame(Control step)
    {
        if (_revealIcon is not null &&
            _stepAnimationPaths.TryGetValue(step, out var animationPath))
        {
            _revealIcon.ShowFinalFrame(animationPath);
        }
    }

    private void SelectActiveStepAnimation()
    {
        if (_revealIcon is null)
            return;

        var activeStep = _steps.FirstOrDefault(step => step.IsVisible);
        if (activeStep is not null &&
            _stepAnimationPaths.TryGetValue(activeStep, out var animationPath))
        {
            _revealIcon.Select(animationPath);
        }
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

public sealed record WizardStepDefinition(Control Content, string AnimationPath);
