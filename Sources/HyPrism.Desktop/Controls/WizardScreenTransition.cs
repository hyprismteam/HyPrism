// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
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
        var outgoingTranslation = GetTranslation(outgoingStep);
        var incomingTranslation = GetTranslation(incomingStep);
        PrepareHiddenState(incomingStep, forward ? 28 : -28);
        outgoingStep.IsHitTestVisible = false;
        incomingStep.IsHitTestVisible = false;

        try
        {
            outgoingStep.Opacity = 0;
            outgoingTranslation.X = forward ? -28 : 28;
            await Task.Delay(PhaseDuration, cancellationToken);
            if (cancellationToken.IsCancellationRequested || !shouldRemainOpen())
                return;

            switchStep();
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            if (cancellationToken.IsCancellationRequested || !shouldRemainOpen())
                return;

            incomingStep.IsHitTestVisible = true;
            incomingStep.Opacity = 1;
            incomingTranslation.X = 0;
        }
        catch (OperationCanceledException)
        {
            // Another wizard navigation replaces the pending step transition
        }
    }

    public void ShowOverviewImmediately()
        => ApplyImmediateState(showWizard: false);

    public void ShowWizardImmediately()
        => ApplyImmediateState(showWizard: true);

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
        var transitions = control.Transitions;
        var translationTransitions = translation.Transitions;
        control.Transitions = null;
        translation.Transitions = null;
        control.Opacity = 0;
        translation.X = offset;
        control.Transitions = transitions;
        translation.Transitions = translationTransitions;
    }

    private static TranslateTransform GetTranslation(Control control)
        => (TranslateTransform)control.RenderTransform!;
}
