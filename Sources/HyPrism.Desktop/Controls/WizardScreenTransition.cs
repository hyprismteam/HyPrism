// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace HyPrism.Desktop.Controls;

/// <summary>
/// Coordinates the phased transition between a content overview and its wizard screen.
/// </summary>
public sealed class WizardScreenTransition
{
    public static readonly TimeSpan PhaseDuration = TimeSpan.FromMilliseconds(190);

    private readonly Control _overview;
    private readonly Control _wizard;
    private CancellationTokenSource? _animationCancellation;

    public WizardScreenTransition(Control overview, Control wizard)
    {
        _overview = overview;
        _wizard = wizard;
    }

    public async Task OpenAsync(Func<bool> shouldRemainOpen, Action? onOpened = null)
    {
        var cancellationToken = BeginAnimation();
        var overviewTranslation = GetTranslation(_overview);
        _overview.IsHitTestVisible = false;
        _wizard.IsHitTestVisible = false;

        try
        {
            _overview.Opacity = 0;
            overviewTranslation.X = -28;
            await Task.Delay(PhaseDuration, cancellationToken);
            if (cancellationToken.IsCancellationRequested || !shouldRemainOpen())
                return;

            _overview.IsVisible = false;
            PrepareForEntry(_wizard, 28);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            if (cancellationToken.IsCancellationRequested || !shouldRemainOpen())
                return;

            _wizard.IsHitTestVisible = true;
            _wizard.Opacity = 1;
            GetTranslation(_wizard).X = 0;
            onOpened?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // A reverse navigation replaces the pending transition
        }
    }

    public async Task CloseAsync(Func<bool> shouldRemainClosed, Action? onClosed = null)
    {
        var cancellationToken = BeginAnimation();
        if (!_wizard.IsVisible)
        {
            ShowOverviewImmediately();
            onClosed?.Invoke();
            return;
        }

        _wizard.IsHitTestVisible = false;
        _wizard.Opacity = 0;
        GetTranslation(_wizard).X = 28;

        try
        {
            await Task.Delay(PhaseDuration, cancellationToken);
            if (cancellationToken.IsCancellationRequested || !shouldRemainClosed())
                return;

            _wizard.IsVisible = false;
            PrepareForEntry(_overview, -28);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            if (cancellationToken.IsCancellationRequested || !shouldRemainClosed())
                return;

            _overview.IsHitTestVisible = true;
            _overview.Opacity = 1;
            GetTranslation(_overview).X = 0;
            onClosed?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Reopening the wizard replaces the pending close transition
        }
    }

    public async Task SwitchStepAsync(
        Control outgoingStep,
        Control incomingStep,
        bool forward,
        Action switchStep,
        Func<bool> shouldRemainOpen)
    {
        var cancellationToken = BeginAnimation();
        var stepSwitched = false;
        PrepareHiddenState(incomingStep, forward ? 28 : -28);
        outgoingStep.IsHitTestVisible = false;
        incomingStep.IsHitTestVisible = false;

        try
        {
            var outgoingOffset = forward ? -28 : 28;
            await RunStepAnimationAsync(
                outgoingStep,
                fromOpacity: 1,
                toOpacity: 0,
                fromOffset: 0,
                toOffset: outgoingOffset,
                cancellationToken);
            if (cancellationToken.IsCancellationRequested || !shouldRemainOpen())
            {
                if (shouldRemainOpen())
                    RestoreVisibleState(outgoingStep);
                return;
            }

            switchStep();
            stepSwitched = true;
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            if (cancellationToken.IsCancellationRequested || !shouldRemainOpen())
            {
                if (shouldRemainOpen())
                    RestoreVisibleState(incomingStep);
                return;
            }

            await RunStepAnimationAsync(
                incomingStep,
                fromOpacity: 0,
                toOpacity: 1,
                fromOffset: forward ? 28 : -28,
                toOffset: 0,
                cancellationToken);
            if (cancellationToken.IsCancellationRequested || !shouldRemainOpen())
                return;

            incomingStep.IsHitTestVisible = true;
        }
        catch (OperationCanceledException)
        {
            // Keep the model's active step visible when an interrupted animation
            // does not also close the wizard
            if (shouldRemainOpen())
                RestoreVisibleState(stepSwitched ? incomingStep : outgoingStep);
        }
    }

    public void ShowOverviewImmediately()
        => ApplyImmediateState(showWizard: false);

    public void ShowWizardImmediately()
        => ApplyImmediateState(showWizard: true);

    public void ShowStepImmediately(Control activeStep, params Control[] inactiveSteps)
    {
        RestoreVisibleState(activeStep);
        foreach (var inactiveStep in inactiveSteps)
        {
            PrepareHiddenState(inactiveStep, 28);
            inactiveStep.IsHitTestVisible = false;
        }
    }

    public void Cancel()
    {
        _animationCancellation?.Cancel();
        _animationCancellation?.Dispose();
        _animationCancellation = null;
    }

    private CancellationToken BeginAnimation()
    {
        Cancel();
        _animationCancellation = new CancellationTokenSource();
        return _animationCancellation.Token;
    }

    private void ApplyImmediateState(bool showWizard)
    {
        Cancel();
        var overviewTranslation = GetTranslation(_overview);
        var wizardTranslation = GetTranslation(_wizard);
        var overviewTransitions = _overview.Transitions;
        var wizardTransitions = _wizard.Transitions;
        var overviewTranslationTransitions = overviewTranslation.Transitions;
        var wizardTranslationTransitions = wizardTranslation.Transitions;
        _overview.Transitions = null;
        _wizard.Transitions = null;
        overviewTranslation.Transitions = null;
        wizardTranslation.Transitions = null;

        _overview.Opacity = showWizard ? 0 : 1;
        overviewTranslation.X = showWizard ? -28 : 0;
        _overview.IsVisible = !showWizard;
        _overview.IsHitTestVisible = !showWizard;
        _wizard.Opacity = showWizard ? 1 : 0;
        wizardTranslation.X = showWizard ? 0 : 36;
        _wizard.IsVisible = showWizard;
        _wizard.IsHitTestVisible = showWizard;

        _overview.Transitions = overviewTransitions;
        _wizard.Transitions = wizardTransitions;
        overviewTranslation.Transitions = overviewTranslationTransitions;
        wizardTranslation.Transitions = wizardTranslationTransitions;
    }

    private static void PrepareForEntry(Control control, double offset)
    {
        PrepareHiddenState(control, offset);
        control.IsVisible = true;
    }

    private static void PrepareHiddenState(Control control, double offset)
    {
        var translation = GetTranslation(control);
        control.Opacity = 0;
        translation.X = offset;
    }

    private static void RestoreVisibleState(Control control)
    {
        var translation = GetTranslation(control);
        control.Opacity = 1;
        control.IsHitTestVisible = true;
        translation.X = 0;
    }

    private static async Task RunStepAnimationAsync(
        Control target,
        double fromOpacity,
        double toOpacity,
        double fromOffset,
        double toOffset,
        CancellationToken cancellationToken)
    {
        var translation = GetTranslation(target);
        target.Opacity = toOpacity;
        translation.X = toOffset;

        await Task.WhenAll(
            CreateAnimation(Visual.OpacityProperty, fromOpacity, toOpacity)
                .RunAsync(target, cancellationToken),
            CreateAnimation(TranslateTransform.XProperty, fromOffset, toOffset)
                .RunAsync(target, cancellationToken));
    }

    private static Animation CreateAnimation(
        AvaloniaProperty property,
        object? from,
        object? to)
        => new()
        {
            Duration = PhaseDuration,
            Easing = new CubicEaseInOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(property, from) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(property, to) }
                }
            }
        };

    private static TranslateTransform GetTranslation(Control control)
        => (TranslateTransform)control.RenderTransform!;
}
