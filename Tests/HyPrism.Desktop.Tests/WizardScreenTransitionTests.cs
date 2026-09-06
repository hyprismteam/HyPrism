// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HyPrism.Desktop.Controls;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class WizardScreenTransitionTests
{
    [AvaloniaTheory]
    [InlineData("loader-reveal.json", 13)]
    [InlineData("avatar-jumping.json", 0)]
    [InlineData("avatar-looking.json", 0)]
    [InlineData("server-pinch.json", 0)]
    public void WizardAnimationsPreserveDarkThemeContrast(
        string assetName,
        int expectedBlackDetails)
    {
        var uri = new Uri($"avares://HyPrism.Desktop/Assets/Lotties/{assetName}");
        using var stream = AssetLoader.Open(uri);
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        Assert.Equal(
            expectedBlackDetails,
            json.Split("\"k\":[0,0,0,1]", StringSplitOptions.None).Length - 1);
        Assert.Contains("\"k\":[0.960784,0.960784,0.964706,1]", json);
    }

    [AvaloniaFact]
    public async Task WizardHostChangesIconStateOnlyAfterStepTransitionCompletes()
    {
        var overview = CreateControl();
        var wizard = CreateControl();
        var choice = CreateControl();
        var form = CreateControl();
        choice.IsVisible = true;
        form.IsVisible = false;
        var revealIcon = new WizardRevealIcon
        {
            AnimationPath = "/Assets/Lotties/avatar-reveal.json"
        };
        var host = new WizardHost(
            overview,
            wizard,
            navigationPane: null,
            revealIcon,
            new WizardStepDefinition(choice, "/Assets/Lotties/avatar-reveal.json"),
            new WizardStepDefinition(form, "/Assets/Lotties/avatar-jumping.json"));
        host.ShowWizardImmediately();

        var switchTask = host.SwitchStepAsync(
            choice,
            form,
            forward: true,
            () =>
            {
                choice.IsVisible = false;
                form.IsVisible = true;
            },
            () => true);
        await WaitForRenderStateAsync(
            () => !choice.IsVisible && form.IsVisible,
            "wizard step model state to switch");

        Assert.Equal("/Assets/Lotties/avatar-reveal.json", revealIcon.AnimationPath);

        await switchTask;

        Assert.Equal("/Assets/Lotties/avatar-jumping.json", revealIcon.AnimationPath);
        Assert.True(revealIcon.LastSelectionWasAnimated);

        await host.SwitchStepAsync(
            form,
            choice,
            forward: false,
            () =>
            {
                form.IsVisible = false;
                choice.IsVisible = true;
            },
            () => true);

        Assert.Equal("/Assets/Lotties/avatar-reveal.json", revealIcon.AnimationPath);
        Assert.False(revealIcon.LastSelectionWasAnimated);
        Assert.True(revealIcon.Animation.AutoPlay);
        Assert.False(revealIcon.ClipToBounds);
        Assert.False(revealIcon.MotionTarget.ClipToBounds);
        Assert.False(revealIcon.Animation.ClipToBounds);
    }

    [AvaloniaFact]
    public async Task RotatingVisualRunsOnlyWhileAttachedAndActive()
    {
        var spinner = new Border();
        RotatingVisual.SetIsActive(spinner, true);

        Assert.Null(spinner.RenderTransform);

        var window = new Window { Content = spinner };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var rotation = Assert.IsType<RotateTransform>(spinner.RenderTransform);
        await WaitForRenderStateAsync(
            () => rotation.Angle is > 0 and < 360,
            "active rotating visual to advance");
        Assert.InRange(rotation.Angle, 1, 359);

        RotatingVisual.SetIsActive(spinner, false);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, rotation.Angle);

        RotatingVisual.SetIsActive(spinner, true);
        await WaitForRenderStateAsync(
            () => rotation.Angle is > 0 and < 360,
            "reactivated rotating visual to advance");
        Assert.InRange(rotation.Angle, 1, 359);

        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, rotation.Angle);
    }

    [AvaloniaFact]
    public async Task WizardHostKeepsActiveContentVisibleUntilExitPhaseCompletes()
    {
        var overview = CreateControl();
        var wizard = CreateControl();
        var activeStep = CreateControl();
        var inactiveStep = CreateControl();
        activeStep.IsVisible = true;
        inactiveStep.IsVisible = false;
        var host = new WizardHost(
            overview,
            wizard,
            steps: [activeStep, inactiveStep]);
        host.ShowWizardImmediately();
        var callbackInvoked = false;

        var closeTask = host.CloseAsync(() => true, () => callbackInvoked = true);

        Assert.True(activeStep.IsVisible);
        Assert.False(callbackInvoked);
        Assert.False(closeTask.IsCompleted);

        await closeTask;

        Assert.True(callbackInvoked);
        Assert.True(overview.IsVisible);
        Assert.False(wizard.IsVisible);
    }

    [AvaloniaFact]
    public void WizardHostNormalizesStepStateWithoutRunningAnEntryTransition()
    {
        var overview = CreateControl();
        var wizard = CreateControl();
        var activeStep = CreateControl();
        var inactiveStep = CreateControl();
        activeStep.IsVisible = true;
        activeStep.Opacity = 0;
        activeStep.IsHitTestVisible = false;
        Assert.IsType<TranslateTransform>(activeStep.RenderTransform).X = 28;
        inactiveStep.IsVisible = false;
        var host = new WizardHost(
            overview,
            wizard,
            steps: [activeStep, inactiveStep]);

        host.ShowWizardImmediately();

        Assert.True(activeStep.IsVisible);
        Assert.Equal(1, activeStep.Opacity);
        Assert.True(activeStep.IsHitTestVisible);
        Assert.Equal(0, Assert.IsType<TranslateTransform>(activeStep.RenderTransform).X);
        Assert.False(inactiveStep.IsVisible);
        Assert.Equal(0, inactiveStep.Opacity);
    }

    [AvaloniaFact]
    public void AdaptiveMasterDetailHostPreservesDetailIntentAcrossLayoutChanges()
    {
        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*")
        };
        var master = CreateControl();
        var detail = CreateControl();
        var toolbar = CreateControl();
        var contentHost = CreateControl();
        layout.Children.Add(master);
        layout.Children.Add(detail);
        var host = new AdaptiveMasterDetailHost(
            layout,
            master,
            detail,
            toolbar,
            contentHost);

        host.Update(800, hasMaster: true);
        Assert.True(host.IsCompact);
        Assert.False(host.IsDetailOpen);
        Assert.False(detail.IsHitTestVisible);
        Assert.Equal(800, Assert.IsType<TranslateTransform>(detail.RenderTransform).X);

        host.OpenDetail();
        Assert.True(host.IsDetailOpen);
        Assert.True(detail.IsHitTestVisible);
        Assert.Equal(0, Assert.IsType<TranslateTransform>(detail.RenderTransform).X);

        host.Update(1200, hasMaster: true);
        Assert.False(host.IsCompact);
        Assert.True(detail.IsHitTestVisible);

        host.Update(800, hasMaster: true);
        Assert.True(host.IsCompact);
        Assert.True(host.IsDetailOpen);

        Assert.True(host.TryCloseDetail());
        Assert.False(host.IsDetailOpen);
        Assert.False(detail.IsHitTestVisible);
    }

    [AvaloniaFact]
    public async Task AdaptiveMasterDetailHostAnimatesUserNavigationButNotLayoutSync()
    {
        var layout = new Grid
        {
            Width = 800,
            Height = 600,
            ColumnDefinitions = new ColumnDefinitions("Auto,*")
        };
        var master = CreateControl();
        var detail = CreateControl();
        var translation = Assert.IsType<TranslateTransform>(detail.RenderTransform);
        translation.Transitions = new Avalonia.Animation.Transitions
        {
            new Avalonia.Animation.DoubleTransition
            {
                Property = TranslateTransform.XProperty,
                Duration = TimeSpan.FromMilliseconds(300)
            }
        };
        layout.Children.Add(master);
        layout.Children.Add(detail);
        var host = new AdaptiveMasterDetailHost(layout, master, detail);
        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = layout
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        host.Update(800, hasMaster: true);
        Assert.Equal(800, translation.X);
        Assert.False(translation.IsAnimating(TranslateTransform.XProperty));

        host.OpenDetail();
        Dispatcher.UIThread.RunJobs();
        Assert.True(translation.IsAnimating(TranslateTransform.XProperty));

        await WaitForRenderStateAsync(
            () => Math.Abs(translation.X) <= 0.01,
            "detail opening animation to complete");
        Assert.InRange(Math.Abs(translation.X), 0, 0.01);
        Assert.False(translation.IsAnimating(TranslateTransform.XProperty));

        Assert.True(host.TryCloseDetail());
        Dispatcher.UIThread.RunJobs();
        Assert.True(translation.IsAnimating(TranslateTransform.XProperty));
        await WaitForRenderStateAsync(
            () => Math.Abs(translation.X - 800) <= 0.01,
            "detail closing animation to complete");
        Assert.InRange(Math.Abs(translation.X - 800), 0, 0.01);
        Assert.False(translation.IsAnimating(TranslateTransform.XProperty));

        window.Close();
    }

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
        Assert.False(outgoingStep.IsHitTestVisible);
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
        await WaitForRenderStateAsync(
            () => outgoingStep.Opacity is > 0 and < 1,
            "outgoing wizard step animation to advance");

        Assert.False(stepSwitched);
        Assert.True(outgoingStep.IsVisible);
        Assert.True(outgoingStep.Opacity < 1);
        Assert.False(outgoingStep.IsHitTestVisible);
        Assert.False(transitionTask.IsCompleted);

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

    [AvaloniaFact]
    public void ImmediateNavigationPaneStatePreservesItsTransitions()
    {
        var navigationPane = CreateControl();
        var paneTransitions = new Avalonia.Animation.Transitions();
        var translationTransitions = new Avalonia.Animation.Transitions();
        navigationPane.Width = 276;
        navigationPane.Transitions = paneTransitions;
        Assert.IsType<TranslateTransform>(navigationPane.RenderTransform).Transitions =
            translationTransitions;
        var transition = new WizardScreenTransition(
            CreateControl(),
            CreateControl(),
            navigationPane);

        transition.HideNavigationPane(animate: false);

        Assert.Equal(0, navigationPane.Width);
        Assert.Equal(0, navigationPane.Opacity);
        Assert.False(navigationPane.IsHitTestVisible);
        Assert.Equal(-24, Assert.IsType<TranslateTransform>(navigationPane.RenderTransform).X);
        Assert.Same(paneTransitions, navigationPane.Transitions);
        Assert.Same(
            translationTransitions,
            Assert.IsType<TranslateTransform>(navigationPane.RenderTransform).Transitions);

        transition.ShowNavigationPane(animate: false);

        Assert.Equal(276, navigationPane.Width);
        Assert.Equal(1, navigationPane.Opacity);
        Assert.True(navigationPane.IsHitTestVisible);
        Assert.Equal(0, Assert.IsType<TranslateTransform>(navigationPane.RenderTransform).X);

        transition.ResetNavigationPane();

        Assert.True(double.IsNaN(navigationPane.Width));
        Assert.Equal(1, navigationPane.Opacity);
        Assert.Equal(0, Assert.IsType<TranslateTransform>(navigationPane.RenderTransform).X);
    }

    [AvaloniaFact]
    public async Task LayoutAnchorMovesSmoothlyWhenCenteredWizardContentChangesHeight()
    {
        var overview = CreateControl();
        var anchor = new Border
        {
            Width = 64,
            Height = 64,
            RenderTransform = new TranslateTransform()
        };
        var variableContent = new Border
        {
            Width = 240,
            Height = 80
        };
        var wizardContent = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 20,
            Children =
            {
                anchor,
                variableContent
            }
        };
        var wizard = new Border
        {
            Width = 400,
            Height = 500,
            Child = wizardContent,
            RenderTransform = new TranslateTransform()
        };
        Assert.Same(wizardContent, anchor.Parent);
        var transition = new WizardScreenTransition(
            overview,
            wizard,
            layoutAnchor: anchor);
        var window = new Window
        {
            Width = 400,
            Height = 500,
            Content = wizard
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        transition.ShowWizardImmediately();
        var initialY = anchor.TranslatePoint(default, wizard)!.Value.Y;

        variableContent.Height = 280;
        Dispatcher.UIThread.RunJobs();

        var translation = Assert.IsType<TranslateTransform>(anchor.RenderTransform);
        var movingY = anchor.TranslatePoint(default, wizard)!.Value.Y;
        Assert.Contains(
            translation.Transitions!,
            item => item is DoubleTransition { Property: not null } doubleTransition &&
                    doubleTransition.Property == TranslateTransform.YProperty);
        Assert.True(
            translation.Y is >= 0 and <= 200,
            $"Initial Y: {initialY}, moving Y: {movingY}, translation Y: {translation.Y}, " +
            $"anchor bounds: {anchor.Bounds}, content bounds: {variableContent.Bounds}, " +
            $"stack bounds: {wizardContent.Bounds}");
        Assert.InRange(movingY, initialY - 200, initialY);

        await WaitForAvaloniaPropertyAsync(
            translation,
            TranslateTransform.YProperty,
            () => Math.Abs(translation.Y) <= 0.01,
            "wizard layout anchor animation to finish");
        Dispatcher.UIThread.RunJobs();

        Assert.InRange(Math.Abs(translation.Y), 0, 0.01);
        var settledY = anchor.TranslatePoint(default, wizard)!.Value.Y;
        Assert.InRange(initialY - settledY, 99, 101);

        transition.Cancel();
        window.Close();
    }

    [AvaloniaFact]
    public async Task CancelledTransitionDoesNotAnimateAnchorForLaterLayoutChanges()
    {
        var overview = CreateControl();
        var anchor = new Border
        {
            Width = 64,
            Height = 64,
            RenderTransform = new TranslateTransform()
        };
        var variableContent = new Border
        {
            Width = 240,
            Height = 80
        };
        var wizardContent = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 20,
            Children =
            {
                anchor,
                variableContent
            }
        };
        var wizard = new Border
        {
            Width = 400,
            Height = 500,
            Child = wizardContent,
            RenderTransform = new TranslateTransform()
        };
        var transition = new WizardScreenTransition(
            overview,
            wizard,
            layoutAnchor: anchor);
        var window = new Window
        {
            Width = 400,
            Height = 500,
            Content = wizard
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        transition.ShowWizardImmediately();
        transition.Cancel();

        variableContent.Height = 280;
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();

        var translation = Assert.IsType<TranslateTransform>(anchor.RenderTransform);
        Assert.InRange(Math.Abs(translation.Y), 0, 0.01);
        Assert.Null(translation.Transitions);

        window.Close();
    }

    private static Border CreateControl()
        => new()
        {
            RenderTransform = new TranslateTransform()
        };

    private static async Task WaitForRenderStateAsync(Func<bool> condition, string description)
    {
        var startedAt = Stopwatch.GetTimestamp();
        while (!condition() && Stopwatch.GetElapsedTime(startedAt) < TimeSpan.FromSeconds(5))
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(16, TestContext.Current.CancellationToken);
        }

        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();
        Assert.True(condition(), $"Timed out waiting for {description}");
    }

    private static async Task WaitForAvaloniaPropertyAsync(
        AvaloniaObject source,
        AvaloniaProperty property,
        Func<bool> condition,
        string description)
    {
        if (condition())
            return;

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == property && condition())
                completion.TrySetResult(true);
        }

        source.PropertyChanged += OnPropertyChanged;
        try
        {
            if (!condition())
                await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            Assert.True(condition(), $"Timed out waiting for {description}");
        }
        finally
        {
            source.PropertyChanged -= OnPropertyChanged;
        }
    }
}
