// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using HyPrism.Desktop.Shell;

namespace HyPrism.Desktop.Features.Instances;

public sealed partial class InstancesView : UserControl
{
    private const double WideLayoutThreshold = 940;
    private static readonly TimeSpan CreatorAnimationPhaseDuration = TimeSpan.FromMilliseconds(190);
    private static readonly TimeSpan CompactSectionSlideDuration = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan WideSectionSlideDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan VersionLoadingFadeDuration = TimeSpan.FromMilliseconds(170);
    private readonly Stopwatch _versionSpinnerClock = new();
    private readonly DispatcherTimer _versionSpinnerTimer;
    private INotifyPropertyChanged? _viewModel;
    private bool? _usesCompactLayout;
    private bool _compactContentOpen;
    private CancellationTokenSource? _creatorAnimationCancellation;
    private CancellationTokenSource? _sectionAnimationCancellation;
    private CancellationTokenSource? _versionLoadingCancellation;

    public InstancesView()
    {
        InitializeComponent();
        _versionSpinnerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _versionSpinnerTimer.Tick += OnVersionSpinnerTick;
        SizeChanged += (_, args) => UpdateLayout(args.NewSize.Width);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as INotifyPropertyChanged;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        UpdateLayout(Bounds.Width);
        UpdateBranchIndicator(animate: false);
        ApplyVersionLoadingStateImmediately();
        ApplySectionStateImmediately();

        if (DataContext is MainWindowViewModel { IsInstanceCreatorOpen: true })
            _ = PlayCreatorOpenAnimationAsync();
        else
            HideCreatorImmediately();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainWindowViewModel.HasInstances))
            UpdateLayout(Bounds.Width);

        if (args.PropertyName is nameof(MainWindowViewModel.NewInstanceBranch))
            UpdateBranchIndicator(animate: true);

        if (args.PropertyName is nameof(MainWindowViewModel.InstanceSection))
        {
            if (DataContext is MainWindowViewModel { IsInstanceOverviewSection: true })
                _ = PlaySectionCloseAnimationAsync();
            else
                _ = PlaySectionOpenAnimationAsync();
        }

        if (args.PropertyName is nameof(MainWindowViewModel.IsInstanceVersionsLoading))
        {
            if (DataContext is MainWindowViewModel { IsInstanceVersionsLoading: true })
                ShowVersionLoading();
            else
                _ = HideVersionLoadingAsync();
        }

        if (args.PropertyName is nameof(MainWindowViewModel.IsInstanceCreatorOpen))
        {
            if (DataContext is MainWindowViewModel { IsInstanceCreatorOpen: true })
                _ = PlayCreatorOpenAnimationAsync();
            else
                _ = PlayCreatorCloseAnimationAsync();
        }
    }

    private void UpdateLayout(double width)
    {
        if (width <= 0 || DataContext is not MainWindowViewModel viewModel)
            return;

        var compact = width < WideLayoutThreshold;
        var hasInstances = viewModel.HasInstances;
        Classes.Set("compact", compact);

        var layoutModeChanged = _usesCompactLayout != compact;
        if (layoutModeChanged)
        {
            var keepContentOpen = compact && _usesCompactLayout is false;
            _usesCompactLayout = compact;
            _compactContentOpen = keepContentOpen;
        }

        if (!hasInstances)
        {
            EnsureSingleLayoutRow();
            InstancesLayout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            InstancesLayout.ColumnDefinitions[1].Width = new GridLength(0);
            InstancesLayout.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(InstancesContent, 0);
            Grid.SetColumnSpan(InstancesContent, 2);
            Grid.SetRow(InstancesContent, 0);
            Grid.SetRowSpan(InstancesContent, 1);
            Grid.SetColumnSpan(InstancesContent, 2);
            InstancesListPane.IsHitTestVisible = false;
            InstancesContent.IsHitTestVisible = true;
            CompactInstanceToolbar.IsVisible = false;
            SetContentOffsetWithoutTransition(0);
            if (layoutModeChanged)
                ApplySectionStateImmediately();
            return;
        }

        EnsureSingleLayoutRow();
        InstancesLayout.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
        Grid.SetRow(InstancesListPane, 0);
        Grid.SetRow(InstancesContent, 0);
        Grid.SetRowSpan(InstancesContent, 1);

        InstanceHubContent.Margin = compact
            ? new Thickness(24, 16, 24, 36)
            : new Thickness(32, 28, 32, 40);
        InstanceHubContent.MaxWidth = compact ? double.PositiveInfinity : 720;
        CompactInstanceToolbar.IsVisible = compact;

        if (!compact)
        {
            InstancesLayout.ColumnDefinitions[0].Width = new GridLength(306);
            InstancesLayout.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(InstancesListPane, 0);
            Grid.SetColumn(InstancesContent, 1);
            Grid.SetColumnSpan(InstancesContent, 1);
            InstancesListPane.IsHitTestVisible = true;
            InstancesContent.IsHitTestVisible = true;
            SetContentOffsetWithoutTransition(0);
            if (layoutModeChanged)
                ApplySectionStateImmediately();
            return;
        }

        InstancesLayout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        InstancesLayout.ColumnDefinitions[1].Width = new GridLength(0);
        Grid.SetColumn(InstancesListPane, 0);
        Grid.SetColumn(InstancesContent, 0);
        Grid.SetColumnSpan(InstancesContent, 2);
        Grid.SetRowSpan(InstancesContent, 1);
        InstancesListPane.IsHitTestVisible = !_compactContentOpen;
        InstancesContent.IsHitTestVisible = _compactContentOpen;
        SetContentOffsetWithoutTransition(_compactContentOpen ? 0 : width);
        if (layoutModeChanged)
            ApplySectionStateImmediately();
    }

    private void EnsureSingleLayoutRow()
    {
        while (InstancesLayout.RowDefinitions.Count > 1)
            InstancesLayout.RowDefinitions.RemoveAt(InstancesLayout.RowDefinitions.Count - 1);
    }

    private void OnInstanceClicked(object? sender, RoutedEventArgs args)
    {
        if (_usesCompactLayout is not true)
            return;

        OpenCompactContent();
    }

    private void OnOpenCreatorClicked(object? sender, RoutedEventArgs args)
    {
        if (_usesCompactLayout is true)
            OpenCompactContent();
    }

    private void OpenCompactContent()
    {
        _compactContentOpen = true;
        InstancesListPane.IsHitTestVisible = false;
        InstancesContent.IsHitTestVisible = true;
        ((TranslateTransform)InstancesContent.RenderTransform!).X = 0;
    }

    private void OnCompactInstanceBackClicked(object? sender, RoutedEventArgs args)
        => TryCloseCompactContent();

    public bool TryCloseCompactContent()
    {
        if (_usesCompactLayout is not true || !_compactContentOpen)
            return false;

        _compactContentOpen = false;
        InstancesContent.IsHitTestVisible = false;
        InstancesListPane.IsHitTestVisible = true;
        ((TranslateTransform)InstancesContent.RenderTransform!).X = Bounds.Width;
        return true;
    }

    public bool TryNavigateBack()
    {
        if (DataContext is MainWindowViewModel { IsInstanceOverviewSection: false } viewModel)
        {
            viewModel.CloseInstanceSectionCommand.Execute(null);
            return true;
        }

        return TryCloseCompactContent();
    }

    private void SetContentOffsetWithoutTransition(double offset)
    {
        var translation = (TranslateTransform)InstancesContent.RenderTransform!;
        var transitions = translation.Transitions;
        translation.Transitions = null;
        translation.X = offset;
        translation.Transitions = transitions;
    }

    private async Task PlayCreatorOpenAnimationAsync()
    {
        CancelCreatorAnimation();

        var overviewTranslation = (TranslateTransform)InstancesOverview.RenderTransform!;
        var wizardTranslation = (TranslateTransform)InstanceCreatorScreen.RenderTransform!;
        InstancesOverview.IsHitTestVisible = false;
        InstanceCreatorScreen.IsHitTestVisible = false;

        _creatorAnimationCancellation = new CancellationTokenSource();
        var cancellationToken = _creatorAnimationCancellation.Token;

        try
        {
            InstancesOverview.Opacity = 0;
            overviewTranslation.X = -28;
            await Task.Delay(CreatorAnimationPhaseDuration, cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                DataContext is not MainWindowViewModel { IsInstanceCreatorOpen: true })
            {
                return;
            }

            InstancesOverview.IsVisible = false;
            PrepareWizardForEntry(wizardTranslation);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            if (cancellationToken.IsCancellationRequested)
                return;

            InstanceCreatorScreen.IsHitTestVisible = true;
            InstanceCreatorScreen.Opacity = 1;
            wizardTranslation.X = 0;
            UpdateBranchIndicator(animate: false);
        }
        catch (OperationCanceledException)
        {
            // A reverse navigation replaces the pending wizard transition
        }
    }

    private async Task PlayCreatorCloseAnimationAsync()
    {
        CancelCreatorAnimation();
        if (!InstanceCreatorScreen.IsVisible)
            return;

        _creatorAnimationCancellation = new CancellationTokenSource();
        var cancellationToken = _creatorAnimationCancellation.Token;
        var wizardTranslation = (TranslateTransform)InstanceCreatorScreen.RenderTransform!;
        InstanceCreatorScreen.IsHitTestVisible = false;
        InstanceCreatorScreen.Opacity = 0;
        wizardTranslation.X = 28;

        try
        {
            await Task.Delay(CreatorAnimationPhaseDuration, cancellationToken);
            if (!cancellationToken.IsCancellationRequested &&
                DataContext is MainWindowViewModel { IsInstanceCreatorOpen: false })
            {
                InstanceCreatorScreen.IsVisible = false;
                var overviewTranslation = (TranslateTransform)InstancesOverview.RenderTransform!;
                PrepareOverviewForEntry(overviewTranslation);
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
                if (cancellationToken.IsCancellationRequested)
                    return;

                InstancesOverview.IsHitTestVisible = true;
                InstancesOverview.Opacity = 1;
                overviewTranslation.X = 0;
            }
        }
        catch (OperationCanceledException)
        {
            // Reopening the creator replaces the pending close animation
        }
    }

    private void HideCreatorImmediately()
    {
        CancelCreatorAnimation();
        var overviewTranslation = (TranslateTransform)InstancesOverview.RenderTransform!;
        var wizardTranslation = (TranslateTransform)InstanceCreatorScreen.RenderTransform!;
        var overviewTransitions = InstancesOverview.Transitions;
        var wizardTransitions = InstanceCreatorScreen.Transitions;
        var overviewTranslationTransitions = overviewTranslation.Transitions;
        var wizardTranslationTransitions = wizardTranslation.Transitions;
        InstancesOverview.Transitions = null;
        InstanceCreatorScreen.Transitions = null;
        overviewTranslation.Transitions = null;
        wizardTranslation.Transitions = null;
        InstancesOverview.Opacity = 1;
        overviewTranslation.X = 0;
        InstancesOverview.IsVisible = true;
        InstancesOverview.IsHitTestVisible = true;
        InstanceCreatorScreen.Opacity = 0;
        wizardTranslation.X = 36;
        InstanceCreatorScreen.IsVisible = false;
        InstanceCreatorScreen.IsHitTestVisible = false;
        InstancesOverview.Transitions = overviewTransitions;
        InstanceCreatorScreen.Transitions = wizardTransitions;
        overviewTranslation.Transitions = overviewTranslationTransitions;
        wizardTranslation.Transitions = wizardTranslationTransitions;
    }

    private void PrepareWizardForEntry(TranslateTransform wizardTranslation)
    {
        var transitions = InstanceCreatorScreen.Transitions;
        var translationTransitions = wizardTranslation.Transitions;
        InstanceCreatorScreen.Transitions = null;
        wizardTranslation.Transitions = null;
        InstanceCreatorScreen.Opacity = 0;
        wizardTranslation.X = 28;
        InstanceCreatorScreen.IsVisible = true;
        InstanceCreatorScreen.Transitions = transitions;
        wizardTranslation.Transitions = translationTransitions;
    }

    private void PrepareOverviewForEntry(TranslateTransform overviewTranslation)
    {
        var transitions = InstancesOverview.Transitions;
        var translationTransitions = overviewTranslation.Transitions;
        InstancesOverview.Transitions = null;
        overviewTranslation.Transitions = null;
        InstancesOverview.Opacity = 0;
        overviewTranslation.X = -28;
        InstancesOverview.IsVisible = true;
        InstancesOverview.Transitions = transitions;
        overviewTranslation.Transitions = translationTransitions;
    }

    private async Task PlaySectionOpenAnimationAsync()
    {
        CancelSectionAnimation();
        if (_usesCompactLayout is true)
        {
            await PlayCompactSectionOpenAnimationAsync();
            return;
        }

        if (!InstanceHubScreen.IsVisible)
        {
            ApplySectionStateImmediately();
            return;
        }

        _sectionAnimationCancellation = new CancellationTokenSource();
        var cancellationToken = _sectionAnimationCancellation.Token;
        var hubTranslation = (TranslateTransform)InstanceHubScreen.RenderTransform!;
        var sectionTranslation = (TranslateTransform)InstanceSectionScreen.RenderTransform!;
        SetSectionTranslationDuration(WideSectionSlideDuration);
        InstanceHubScreen.IsHitTestVisible = false;
        InstanceSectionScreen.IsHitTestVisible = false;
        InstanceHubScreen.Opacity = 0;
        hubTranslation.X = -28;

        try
        {
            await Task.Delay(CreatorAnimationPhaseDuration, cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                DataContext is not MainWindowViewModel { IsInstanceOverviewSection: false })
            {
                return;
            }

            InstanceHubScreen.IsVisible = false;
            PrepareSectionForEntry(sectionTranslation);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            if (cancellationToken.IsCancellationRequested)
                return;

            InstanceSectionScreen.IsHitTestVisible = true;
            InstanceSectionScreen.Opacity = 1;
            sectionTranslation.X = 0;
        }
        catch (OperationCanceledException)
        {
            // A reverse section navigation replaces the pending transition
        }
    }

    private async Task PlaySectionCloseAnimationAsync()
    {
        CancelSectionAnimation();
        if (_usesCompactLayout is true)
        {
            await PlayCompactSectionCloseAnimationAsync();
            return;
        }

        if (!InstanceSectionScreen.IsVisible)
        {
            ApplySectionStateImmediately();
            return;
        }

        _sectionAnimationCancellation = new CancellationTokenSource();
        var cancellationToken = _sectionAnimationCancellation.Token;
        var sectionTranslation = (TranslateTransform)InstanceSectionScreen.RenderTransform!;
        SetSectionTranslationDuration(WideSectionSlideDuration);
        InstanceSectionScreen.IsHitTestVisible = false;
        InstanceSectionScreen.Opacity = 0;
        sectionTranslation.X = 28;

        try
        {
            await Task.Delay(CreatorAnimationPhaseDuration, cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                DataContext is not MainWindowViewModel { IsInstanceOverviewSection: true })
            {
                return;
            }

            InstanceSectionScreen.IsVisible = false;
            var hubTranslation = (TranslateTransform)InstanceHubScreen.RenderTransform!;
            PrepareHubForEntry(hubTranslation);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            if (cancellationToken.IsCancellationRequested)
                return;

            InstanceHubScreen.IsHitTestVisible = true;
            InstanceHubScreen.Opacity = 1;
            hubTranslation.X = 0;
        }
        catch (OperationCanceledException)
        {
            // Opening another section replaces the pending transition
        }
    }

    private void ApplySectionStateImmediately()
    {
        CancelSectionAnimation();
        var showHub = DataContext is not MainWindowViewModel { IsInstanceOverviewSection: false };
        var hubTranslation = (TranslateTransform)InstanceHubScreen.RenderTransform!;
        var sectionTranslation = (TranslateTransform)InstanceSectionScreen.RenderTransform!;
        var hubTransitions = InstanceHubScreen.Transitions;
        var sectionTransitions = InstanceSectionScreen.Transitions;
        var hubTranslationTransitions = hubTranslation.Transitions;
        var sectionTranslationTransitions = sectionTranslation.Transitions;
        InstanceHubScreen.Transitions = null;
        InstanceSectionScreen.Transitions = null;
        hubTranslation.Transitions = null;
        sectionTranslation.Transitions = null;
        var compact = _usesCompactLayout is true;
        InstanceHubScreen.IsVisible = compact || showHub;
        InstanceHubScreen.IsHitTestVisible = showHub;
        InstanceHubScreen.Opacity = compact || showHub ? 1 : 0;
        hubTranslation.X = compact || showHub ? 0 : -28;
        InstanceSectionScreen.IsVisible = !showHub;
        InstanceSectionScreen.IsHitTestVisible = !showHub;
        InstanceSectionScreen.Opacity = showHub ? 0 : 1;
        sectionTranslation.X = showHub
            ? compact ? GetSectionSlideDistance() : 28
            : 0;
        InstanceHubScreen.Transitions = hubTransitions;
        InstanceSectionScreen.Transitions = sectionTransitions;
        hubTranslation.Transitions = hubTranslationTransitions;
        sectionTranslation.Transitions = sectionTranslationTransitions;
        SetSectionTranslationDuration(compact ? CompactSectionSlideDuration : WideSectionSlideDuration);
    }

    private async Task PlayCompactSectionOpenAnimationAsync()
    {
        _sectionAnimationCancellation = new CancellationTokenSource();
        var cancellationToken = _sectionAnimationCancellation.Token;
        var sectionTranslation = (TranslateTransform)InstanceSectionScreen.RenderTransform!;
        SetSectionTranslationDuration(CompactSectionSlideDuration);

        InstanceHubScreen.IsVisible = true;
        InstanceHubScreen.IsHitTestVisible = false;
        InstanceHubScreen.Opacity = 1;
        ((TranslateTransform)InstanceHubScreen.RenderTransform!).X = 0;
        PrepareCompactSectionForEntry(sectionTranslation);

        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
        if (cancellationToken.IsCancellationRequested ||
            DataContext is not MainWindowViewModel { IsInstanceOverviewSection: false })
        {
            return;
        }

        InstanceSectionScreen.IsHitTestVisible = true;
        sectionTranslation.X = 0;
    }

    private async Task PlayCompactSectionCloseAnimationAsync()
    {
        if (!InstanceSectionScreen.IsVisible)
        {
            ApplySectionStateImmediately();
            return;
        }

        _sectionAnimationCancellation = new CancellationTokenSource();
        var cancellationToken = _sectionAnimationCancellation.Token;
        var sectionTranslation = (TranslateTransform)InstanceSectionScreen.RenderTransform!;
        SetSectionTranslationDuration(CompactSectionSlideDuration);
        InstanceSectionScreen.IsHitTestVisible = false;
        InstanceHubScreen.IsVisible = true;
        InstanceHubScreen.IsHitTestVisible = false;
        InstanceHubScreen.Opacity = 1;
        ((TranslateTransform)InstanceHubScreen.RenderTransform!).X = 0;
        sectionTranslation.X = GetSectionSlideDistance();

        try
        {
            await Task.Delay(CompactSectionSlideDuration + TimeSpan.FromMilliseconds(20), cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                DataContext is not MainWindowViewModel { IsInstanceOverviewSection: true })
            {
                return;
            }

            InstanceSectionScreen.IsVisible = false;
            InstanceSectionScreen.Opacity = 0;
            InstanceHubScreen.IsHitTestVisible = true;
        }
        catch (OperationCanceledException)
        {
            // Opening a section again replaces the pending compact slide
        }
    }

    private void PrepareCompactSectionForEntry(TranslateTransform translation)
    {
        var transitions = InstanceSectionScreen.Transitions;
        var translationTransitions = translation.Transitions;
        InstanceSectionScreen.Transitions = null;
        translation.Transitions = null;
        InstanceSectionScreen.Opacity = 1;
        translation.X = GetSectionSlideDistance();
        InstanceSectionScreen.IsVisible = true;
        InstanceSectionScreen.Transitions = transitions;
        translation.Transitions = translationTransitions;
    }

    private double GetSectionSlideDistance()
        => Math.Max(1, InstancesContent.Bounds.Width > 0 ? InstancesContent.Bounds.Width : Bounds.Width);

    private void SetSectionTranslationDuration(TimeSpan duration)
    {
        var translation = (TranslateTransform)InstanceSectionScreen.RenderTransform!;
        var transition = translation.Transitions?.OfType<DoubleTransition>().FirstOrDefault();
        if (transition is not null)
            transition.Duration = duration;
    }

    private void PrepareSectionForEntry(TranslateTransform translation)
    {
        var transitions = InstanceSectionScreen.Transitions;
        var translationTransitions = translation.Transitions;
        InstanceSectionScreen.Transitions = null;
        translation.Transitions = null;
        InstanceSectionScreen.Opacity = 0;
        translation.X = 28;
        InstanceSectionScreen.IsVisible = true;
        InstanceSectionScreen.Transitions = transitions;
        translation.Transitions = translationTransitions;
    }

    private void PrepareHubForEntry(TranslateTransform translation)
    {
        var transitions = InstanceHubScreen.Transitions;
        var translationTransitions = translation.Transitions;
        InstanceHubScreen.Transitions = null;
        translation.Transitions = null;
        InstanceHubScreen.Opacity = 0;
        translation.X = -28;
        InstanceHubScreen.IsVisible = true;
        InstanceHubScreen.Transitions = transitions;
        translation.Transitions = translationTransitions;
    }

    private void CancelSectionAnimation()
    {
        _sectionAnimationCancellation?.Cancel();
        _sectionAnimationCancellation?.Dispose();
        _sectionAnimationCancellation = null;
    }

    private void OnBranchSwitchSizeChanged(object? sender, SizeChangedEventArgs args)
        => UpdateBranchIndicator(animate: false);

    private void UpdateBranchIndicator(bool animate)
    {
        if (DataContext is not MainWindowViewModel viewModel || BranchSwitchTrack.Bounds.Width <= 0)
            return;

        var translation = (TranslateTransform)BranchSelectionIndicator.RenderTransform!;
        var transitions = translation.Transitions;
        if (!animate)
            translation.Transitions = null;

        translation.X = viewModel.IsCreatePreReleaseBranch
            ? BranchSwitchTrack.Bounds.Width / 2
            : 0;

        if (!animate)
            translation.Transitions = transitions;
    }

    private void CancelCreatorAnimation()
    {
        _creatorAnimationCancellation?.Cancel();
        _creatorAnimationCancellation?.Dispose();
        _creatorAnimationCancellation = null;
    }

    private void ApplyVersionLoadingStateImmediately()
    {
        CancelVersionLoadingAnimation();
        var isLoading = DataContext is MainWindowViewModel { IsInstanceVersionsLoading: true };
        var comboTransitions = InstanceVersionComboBox.Transitions;
        var spinnerTransitions = VersionLoadingSpinner.Transitions;
        InstanceVersionComboBox.Transitions = null;
        VersionLoadingSpinner.Transitions = null;
        InstanceVersionComboBox.Opacity = isLoading ? 0 : 1;
        InstanceVersionComboBox.IsHitTestVisible = !isLoading;
        VersionLoadingSpinner.IsVisible = isLoading;
        VersionLoadingSpinner.Opacity = isLoading ? 1 : 0;
        SetVersionSpinnerRunning(isLoading);
        InstanceVersionComboBox.Transitions = comboTransitions;
        VersionLoadingSpinner.Transitions = spinnerTransitions;
    }

    private void ShowVersionLoading()
    {
        CancelVersionLoadingAnimation();
        InstanceVersionComboBox.IsHitTestVisible = false;
        InstanceVersionComboBox.Opacity = 0;
        VersionLoadingSpinner.IsVisible = true;
        VersionLoadingSpinner.Opacity = 1;
        SetVersionSpinnerRunning(true);
    }

    private async Task HideVersionLoadingAsync()
    {
        CancelVersionLoadingAnimation();
        _versionLoadingCancellation = new CancellationTokenSource();
        var cancellationToken = _versionLoadingCancellation.Token;
        VersionLoadingSpinner.Opacity = 0;

        try
        {
            await Task.Delay(VersionLoadingFadeDuration, cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                DataContext is MainWindowViewModel { IsInstanceVersionsLoading: true })
            {
                return;
            }

            VersionLoadingSpinner.IsVisible = false;
            SetVersionSpinnerRunning(false);
            InstanceVersionComboBox.IsHitTestVisible = true;
            InstanceVersionComboBox.Opacity = 1;
        }
        catch (OperationCanceledException)
        {
            // A new loading cycle replaces the pending transition
        }
    }

    private void CancelVersionLoadingAnimation()
    {
        _versionLoadingCancellation?.Cancel();
        _versionLoadingCancellation?.Dispose();
        _versionLoadingCancellation = null;
    }

    private void SetVersionSpinnerRunning(bool isRunning)
    {
        if (isRunning)
        {
            _versionSpinnerClock.Restart();
            _versionSpinnerTimer.Start();
            return;
        }

        _versionSpinnerTimer.Stop();
        _versionSpinnerClock.Reset();
        ((RotateTransform)VersionLoadingSpinner.RenderTransform!).Angle = 0;
    }

    private void OnVersionSpinnerTick(object? sender, EventArgs args)
    {
        const double rotationDurationMilliseconds = 800;
        ((RotateTransform)VersionLoadingSpinner.RenderTransform!).Angle =
            _versionSpinnerClock.Elapsed.TotalMilliseconds % rotationDurationMilliseconds /
            rotationDurationMilliseconds * 360;
    }
}
