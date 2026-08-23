// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HyPrism.Desktop.Controls;
using HyPrism.Desktop.Features.Dashboard;
using HyPrism.Desktop.Shell;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace HyPrism.Desktop.Features.Instances;

public sealed partial class InstancesView : UserControl
{
    private const double WideLayoutThreshold = 940;
    private static readonly TimeSpan CompactContentTransitionDuration = TimeSpan.FromMilliseconds(320);
    private static readonly TimeSpan CompactSectionSlideDuration = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan WideSectionSlideDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan VersionLoadingFadeDuration = TimeSpan.FromMilliseconds(170);
    private readonly Stopwatch _versionSpinnerClock = new();
    private readonly DispatcherTimer _versionSpinnerTimer;
    private readonly Stopwatch _actionSpinnerClock = new();
    private readonly DispatcherTimer _actionSpinnerTimer;
    private readonly WizardScreenTransition _creatorTransition;
    private INotifyPropertyChanged? _viewModel;
    private bool? _usesCompactLayout;
    private bool _compactContentOpen;
    private bool _returnToCompactContent;
    private bool _creatorOpenedFromCompactList;
    private int _creatorNavigationRevision;
    private CancellationTokenSource? _sectionAnimationCancellation;
    private CancellationTokenSource? _versionLoadingCancellation;
    private Control? _instanceDragHandle;
    private Button? _instanceDragRow;
    private string? _draggedInstanceId;
    private Point _instanceDragStart;
    private Point _instanceDragStartInLayout;
    private Point _instanceDragPreviewOrigin;
    private int _instanceDragTargetIndex = -1;
    private bool _isInstanceDragActive;

    public InstancesView()
    {
        InitializeComponent();
        _creatorTransition = new WizardScreenTransition(
            InstancesOverview,
            InstanceCreatorScreen,
            InstancesListPane,
            InstanceWizardAnimationAnchor,
            InstanceWizardAnimationMotion);
        _versionSpinnerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _versionSpinnerTimer.Tick += OnVersionSpinnerTick;
        _actionSpinnerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _actionSpinnerTimer.Tick += OnActionSpinnerTick;
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
        UpdateActionSpinnerState();
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

        if (args.PropertyName is nameof(MainWindowViewModel.IsManagedInstanceActionActive) or
            nameof(MainWindowViewModel.IsManagedInstanceActionRunning))
        {
            UpdateActionSpinnerState();
        }
    }

    private void UpdateLayout(double width)
    {
        if (width <= 0 || DataContext is not MainWindowViewModel viewModel)
            return;

        var compact = width < WideLayoutThreshold;
        var hasInstances = viewModel.HasInstances;
        Classes.Set("compact", compact);
        Classes.Set("wide", !compact);

        var layoutModeChanged = _usesCompactLayout != compact;
        if (layoutModeChanged)
        {
            if (compact)
            {
                _compactContentOpen = _returnToCompactContent;
            }
            else if (_usesCompactLayout is true)
            {
                _returnToCompactContent = _compactContentOpen;
            }

            _usesCompactLayout = compact;
        }

        if (!hasInstances)
        {
            EnsureSingleLayoutRow();
            _creatorTransition.ResetNavigationPane();
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
            InstancesLayout.ColumnDefinitions[0].Width = GridLength.Auto;
            InstancesLayout.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(InstancesListPane, 0);
            Grid.SetColumn(InstancesContent, 1);
            Grid.SetColumnSpan(InstancesContent, 1);
            if (viewModel.IsInstanceCreatorOpen)
                _creatorTransition.HideNavigationPane(animate: false);
            else
                _creatorTransition.ShowNavigationPane(animate: false);
            InstancesContent.IsHitTestVisible = true;
            SetContentOffsetWithoutTransition(0);
            if (layoutModeChanged)
                ApplySectionStateImmediately();
            return;
        }

        _creatorTransition.ResetNavigationPane();
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
        _returnToCompactContent = true;
        if (_usesCompactLayout is not true)
            return;

        OpenCompactContent();
    }

    private void OnManagedInstanceActionPointerExited(object? sender, PointerEventArgs args)
    {
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.ArmManagedInstanceCancellation();
    }

    private void OnInstanceDragHandlePressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is not Border { DataContext: InstanceItemViewModel instance } handle ||
            !args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _instanceDragHandle = handle;
        _instanceDragRow = handle.FindAncestorOfType<Button>();
        _draggedInstanceId = instance.Id;
        _instanceDragStart = args.GetPosition(InstancesItems);
        _instanceDragStartInLayout = args.GetPosition(InstancesLayout);
        _instanceDragPreviewOrigin = _instanceDragRow?.TranslatePoint(default, InstancesLayout) ?? default;
        _instanceDragTargetIndex = -1;
        _isInstanceDragActive = false;
        InstanceDragPreviewName.Text = instance.Name;
        InstanceDragPreviewBranch.Text = instance.Branch;
        InstanceDragPreview.Width = _instanceDragRow?.Bounds.Width ?? InstancesListPane.Bounds.Width;
        InstanceDragPreview.Height = _instanceDragRow?.Bounds.Height ?? 66;
        args.Pointer.Capture(handle);
        args.Handled = true;
    }

    private void OnInstanceDragHandleMoved(object? sender, PointerEventArgs args)
    {
        if (_instanceDragHandle is null || _draggedInstanceId is null)
            return;

        var position = args.GetPosition(InstancesItems);
        if (!_isInstanceDragActive)
        {
            var delta = position - _instanceDragStart;
            if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 5)
                return;

            _isInstanceDragActive = true;
            _instanceDragRow?.Classes.Add("dragging");
            InstanceDragPreview.IsVisible = true;
        }

        UpdateInstanceDragPreview(args.GetPosition(InstancesLayout));
        _instanceDragTargetIndex = GetInstanceDropTargetIndex(position.Y);
        args.Handled = true;
    }

    private void OnInstanceDragHandleReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (_isInstanceDragActive &&
            _instanceDragTargetIndex >= 0 &&
            _draggedInstanceId is not null &&
            DataContext is MainWindowViewModel viewModel)
        {
            viewModel.MoveInstance(_draggedInstanceId, _instanceDragTargetIndex);
        }

        args.Pointer.Capture(null);
        ResetInstanceDragState();
        args.Handled = true;
    }

    private int GetInstanceDropTargetIndex(double pointerY)
    {
        var rows = InstancesItems.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.Classes.Contains("instancesListItem"))
            .Select(button => new
            {
                Button = button,
                Origin = button.TranslatePoint(default, InstancesItems)
            })
            .Where(item => item.Origin.HasValue)
            .OrderBy(item => item.Origin!.Value.Y)
            .ToList();

        for (var index = 0; index < rows.Count; index++)
        {
            var midpoint = rows[index].Origin!.Value.Y + rows[index].Button.Bounds.Height / 2;
            if (pointerY < midpoint)
                return index;
        }

        return Math.Max(0, rows.Count - 1);
    }

    private void UpdateInstanceDragPreview(Point pointerPosition)
    {
        var transform = (TranslateTransform)InstanceDragPreview.RenderTransform!;
        transform.X =
            _instanceDragPreviewOrigin.X + pointerPosition.X - _instanceDragStartInLayout.X + 10;
        transform.Y =
            _instanceDragPreviewOrigin.Y + pointerPosition.Y - _instanceDragStartInLayout.Y + 8;
    }

    private void ResetInstanceDragState()
    {
        _instanceDragRow?.Classes.Remove("dragging");
        InstanceDragPreview.IsVisible = false;
        InstanceDragPreviewName.Text = string.Empty;
        InstanceDragPreviewBranch.Text = string.Empty;
        _instanceDragHandle = null;
        _instanceDragRow = null;
        _draggedInstanceId = null;
        _instanceDragTargetIndex = -1;
        _isInstanceDragActive = false;
    }

    private void OnOpenCreatorClicked(object? sender, RoutedEventArgs args)
    {
        _creatorOpenedFromCompactList = _usesCompactLayout is true &&
                                        !_compactContentOpen &&
                                        DataContext is MainWindowViewModel { HasInstances: true };
        if (_usesCompactLayout is true && !_creatorOpenedFromCompactList)
            OpenCompactContent();

        if (DataContext is MainWindowViewModel viewModel)
            viewModel.OpenInstanceCreatorCommand.Execute(null);
    }

    private void OnCloseDeleteInstanceFlyoutClicked(object? sender, RoutedEventArgs args)
    {
        DeleteInstanceButton.Flyout?.Hide();
        CompactDeleteInstanceButton.Flyout?.Hide();
        CompactInstanceMenuPopup.IsRequestedOpen = false;
    }

    private void OnCloseCompactInstanceMenuClicked(object? sender, RoutedEventArgs args)
        => CompactInstanceMenuPopup.IsRequestedOpen = false;

    private void OnToggleCompactInstanceMenuClicked(object? sender, RoutedEventArgs args)
        => CompactInstanceMenuPopup.IsRequestedOpen = !CompactInstanceMenuPopup.IsRequestedOpen;

    private void OpenCompactContent()
    {
        _returnToCompactContent = true;
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

        if (DataContext is MainWindowViewModel { IsInstanceCreatorOpen: true } viewModel)
        {
            viewModel.CloseInstanceCreatorCommand.Execute(null);
            return true;
        }

        _returnToCompactContent = false;
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
        var revision = ++_creatorNavigationRevision;
        if (_creatorOpenedFromCompactList && _usesCompactLayout is true)
        {
            _creatorTransition.ShowWizardImmediately();
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            if (revision != _creatorNavigationRevision ||
                DataContext is not MainWindowViewModel { IsInstanceCreatorOpen: true })
            {
                return;
            }

            RestartInstanceWizardAnimation();
            OpenCompactContent();
            UpdateBranchIndicator(animate: false);
            return;
        }

        if (_usesCompactLayout is false &&
            DataContext is MainWindowViewModel { HasInstances: true })
        {
            _creatorTransition.HideNavigationPane(animate: true);
        }

        await _creatorTransition.OpenAsync(
            () => DataContext is MainWindowViewModel { IsInstanceCreatorOpen: true },
            () =>
            {
                RestartInstanceWizardAnimation();
                UpdateBranchIndicator(animate: false);
            });
    }

    private void RestartInstanceWizardAnimation()
    {
        InstanceWizardAnimation.SeekToProgress(0);
        InstanceWizardAnimation.Start();
    }

    private async Task PlayCreatorCloseAnimationAsync()
    {
        var revision = ++_creatorNavigationRevision;
        if (_creatorOpenedFromCompactList && _usesCompactLayout is true)
        {
            _creatorTransition.Cancel();
            _returnToCompactContent = false;
            _compactContentOpen = false;
            InstancesContent.IsHitTestVisible = false;
            InstancesListPane.IsHitTestVisible = true;
            ((TranslateTransform)InstancesContent.RenderTransform!).X = Bounds.Width;
            await Task.Delay(CompactContentTransitionDuration);
            if (revision == _creatorNavigationRevision &&
                DataContext is MainWindowViewModel { IsInstanceCreatorOpen: false })
            {
                _creatorTransition.ShowOverviewImmediately();
                _creatorOpenedFromCompactList = false;
            }

            return;
        }

        await _creatorTransition.CloseAsync(
            () => DataContext is MainWindowViewModel { IsInstanceCreatorOpen: false },
            () =>
            {
                if (_usesCompactLayout is false &&
                    DataContext is MainWindowViewModel { HasInstances: true })
                {
                    _creatorTransition.ShowNavigationPane(animate: true);
                }

                _creatorOpenedFromCompactList = false;
            });
    }

    private void HideCreatorImmediately()
    {
        ++_creatorNavigationRevision;
        _creatorOpenedFromCompactList = false;
        _creatorTransition.ShowOverviewImmediately();
        if (_usesCompactLayout is false &&
            DataContext is MainWindowViewModel { HasInstances: true })
        {
            _creatorTransition.ShowNavigationPane(animate: false);
        }
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
            await Task.Delay(WizardScreenTransition.PhaseDuration, cancellationToken);
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
            await Task.Delay(WizardScreenTransition.PhaseDuration, cancellationToken);
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

    private void UpdateActionSpinnerState()
    {
        var shouldSpin = DataContext is MainWindowViewModel
        {
            IsManagedInstanceActionActive: true,
            IsManagedInstanceActionRunning: false
        };

        if (shouldSpin)
        {
            if (!_actionSpinnerTimer.IsEnabled)
                _actionSpinnerClock.Restart();
            _actionSpinnerTimer.Start();
            return;
        }

        _actionSpinnerTimer.Stop();
        _actionSpinnerClock.Reset();
        RotateActionSpinners(0);
    }

    private void OnActionSpinnerTick(object? sender, EventArgs args)
    {
        const double rotationDurationMilliseconds = 800;
        var angle = _actionSpinnerClock.Elapsed.TotalMilliseconds % rotationDurationMilliseconds /
                    rotationDurationMilliseconds * 360;
        RotateActionSpinners(angle);
    }

    private void RotateActionSpinners(double angle)
    {
        foreach (var spinner in this.GetVisualDescendants()
                     .OfType<ShapePath>()
                     .Where(path => path.Classes.Contains("managedActionSpinner")))
        {
            spinner.RenderTransform ??= new RotateTransform();
            ((RotateTransform)spinner.RenderTransform).Angle = angle;
        }
    }
}
