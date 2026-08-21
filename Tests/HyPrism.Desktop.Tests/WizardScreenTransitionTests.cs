// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using HyPrism.Desktop.Controls;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class WizardScreenTransitionTests
{
    [AvaloniaFact]
    public async Task CancelledStepTransitionRestoresTheCurrentStep()
    {
        var overview = CreateControl();
        var wizard = CreateControl();
        var outgoingStep = CreateControl();
        var incomingStep = CreateControl();
        var transition = new WizardScreenTransition(overview, wizard);
        var stepSwitched = false;

        var transitionTask = transition.SwitchStepAsync(
            outgoingStep,
            incomingStep,
            forward: true,
            () => stepSwitched = true,
            () => true);
        await Task.Delay(20);
        transition.Cancel();
        await transitionTask;

        Assert.False(stepSwitched);
        Assert.Equal(1, outgoingStep.Opacity);
        Assert.True(outgoingStep.IsHitTestVisible);
        Assert.Equal(0, Assert.IsType<TranslateTransform>(outgoingStep.RenderTransform).X);
    }

    [AvaloniaFact]
    public void ShowStepImmediatelyRestoresAControlLeftHiddenByAnEarlierTransition()
    {
        var overview = CreateControl();
        var wizard = CreateControl();
        var activeStep = CreateControl();
        var inactiveStep = CreateControl();
        var transitions = new Avalonia.Animation.Transitions();
        activeStep.Transitions = transitions;
        var activeTransitionsChanged = 0;
        activeStep.PropertyChanged += (_, args) =>
        {
            if (args.Property == Avalonia.Animation.Animatable.TransitionsProperty)
                activeTransitionsChanged++;
        };
        var transition = new WizardScreenTransition(overview, wizard);
        activeStep.Opacity = 0;
        activeStep.IsHitTestVisible = false;
        Assert.IsType<TranslateTransform>(activeStep.RenderTransform).X = -28;

        transition.ShowStepImmediately(activeStep, inactiveStep);

        Assert.Equal(1, activeStep.Opacity);
        Assert.True(activeStep.IsHitTestVisible);
        Assert.Equal(0, Assert.IsType<TranslateTransform>(activeStep.RenderTransform).X);
        Assert.Same(transitions, activeStep.Transitions);
        Assert.Equal(0, activeTransitionsChanged);
        Assert.Equal(0, inactiveStep.Opacity);
        Assert.False(inactiveStep.IsHitTestVisible);
    }

    [AvaloniaFact]
    public async Task PreviousStepRemainsVisibleUntilItsExitAnimationCompletes()
    {
        var overview = CreateControl();
        var wizard = CreateControl();
        var outgoingStep = CreateControl();
        var incomingStep = CreateControl();
        var transition = new WizardScreenTransition(overview, wizard);
        var stepSwitched = false;

        var transitionTask = transition.SwitchStepAsync(
            outgoingStep,
            incomingStep,
            forward: true,
            () => stepSwitched = true,
            () => true);
        await Task.Delay(80);

        Assert.False(stepSwitched);
        Assert.InRange(outgoingStep.Opacity, 0.01, 0.99);
        Assert.InRange(
            Assert.IsType<TranslateTransform>(outgoingStep.RenderTransform).X,
            -27.99,
            -0.01);

        transition.Cancel();
        await transitionTask;
    }

    [AvaloniaFact]
    public async Task StartingStepTransitionKeepsOutgoingTransitionsAttached()
    {
        var overview = CreateControl();
        var wizard = CreateControl();
        var outgoingStep = CreateControl();
        var incomingStep = CreateControl();
        var transitions = new Avalonia.Animation.Transitions();
        outgoingStep.Transitions = transitions;
        var outgoingTransitionsChanged = 0;
        outgoingStep.PropertyChanged += (_, args) =>
        {
            if (args.Property == Avalonia.Animation.Animatable.TransitionsProperty)
                outgoingTransitionsChanged++;
        };
        var transition = new WizardScreenTransition(overview, wizard);

        var transitionTask = transition.SwitchStepAsync(
            outgoingStep,
            incomingStep,
            forward: true,
            static () => { },
            () => true);

        Assert.Same(transitions, outgoingStep.Transitions);
        Assert.Equal(0, outgoingTransitionsChanged);
        transition.Cancel();
        await transitionTask;
    }

    private static Border CreateControl()
        => new()
        {
            RenderTransform = new TranslateTransform()
        };
}
