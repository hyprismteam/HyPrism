// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HyPrism.Desktop.Controls;
using HyPrism.Desktop.Features.Dashboard;
using HyPrism.Desktop.Shell;

namespace HyPrism.Desktop.Features.Instances;

public sealed partial class InstancesView : UserControl
{
    private const double ModCatalogModalHiddenOffset = 720;

    private static readonly TimeSpan CompactContentTransitionDuration = MotionDurations.CompactPageSlide;
    private static readonly TimeSpan CompactSectionSlideDuration = MotionDurations.CompactSectionSlide;
    private static readonly TimeSpan WideSectionSlideDuration = MotionDurations.ContentFade;
    private static readonly TimeSpan VersionLoadingFadeDuration = MotionDurations.VersionLoadingFade;
    private readonly WizardHost _creatorWizard;
    private readonly AdaptiveMasterDetailHost _layoutHost;
    private readonly ReorderableListController _instanceReorder;
    private INotifyPropertyChanged? _viewModel;
    private bool _creatorOpenedFromCompactList;
    private int _creatorNavigationRevision;
    private CancellationTokenSource? _sectionAnimationCancellation;
    private CancellationTokenSource? _versionLoadingCancellation;
    private CancellationTokenSource? _modCatalogModalCancellation;
    private bool _modDropActive;

    public InstancesView()
    {
        InitializeComponent();
        _creatorWizard = new WizardHost(
            InstancesOverview,
            InstanceCreatorScreen,
            InstancesListPane,
            InstanceWizardReveal.Anchor,
            InstanceWizardReveal.MotionTarget,
            InstanceWizardReveal.Animation);
        _layoutHost = new AdaptiveMasterDetailHost(
            InstancesLayout,
            InstancesListPane,
            InstancesContent,
            CompactInstanceToolbar,
            InstanceHubContent,
            compact =>
            {
                Classes.Set("compact", compact);
                Classes.Set("wide", !compact);
            });
        _instanceReorder = new ReorderableListController(
            InstancesItems,
            InstancesLayout,
            InstanceDragPreview,
            InstancesListPane);
        DragDrop.SetAllowDrop(ModsDropZone, true);
        ModsDropZone.AddHandler(DragDrop.DragEnterEvent, OnModFilesDragEntered);
        ModsDropZone.AddHandler(DragDrop.DragLeaveEvent, OnModFilesDragLeft);
        ModsDropZone.AddHandler(DragDrop.DropEvent, OnModFilesDropped);
        AddHandler(KeyDownEvent, OnInstancesKeyDown, RoutingStrategies.Tunnel);
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
        ApplyModCatalogModalStateImmediately();

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

        if (args.PropertyName is nameof(MainWindowViewModel.ConsoleRevision))
            ScrollConsoleToBottom();

        if (args.PropertyName is nameof(MainWindowViewModel.IsInstanceCreatorOpen))
        {
            if (DataContext is MainWindowViewModel { IsInstanceCreatorOpen: true })
                _ = PlayCreatorOpenAnimationAsync();
            else
                _ = PlayCreatorCloseAnimationAsync();
        }

        if (args.PropertyName is nameof(MainWindowViewModel.HasModCatalogPreview))
        {
            if (DataContext is MainWindowViewModel { HasModCatalogPreview: true })
                _ = ShowModCatalogModalAsync();
            else
                _ = HideModCatalogModalAsync();
        }
    }

    private async Task ShowModCatalogModalAsync()
    {
        var cancellationToken = ReplaceModCatalogModalCancellation();
        var translation = (TranslateTransform)ModCatalogModalSheet.RenderTransform!;
        var shoulderScale = (ScaleTransform)ModCatalogModalShoulders.RenderTransform!;
        ModCatalogModalBackdrop.Opacity = 0;
        translation.Y = ModCatalogModalHiddenOffset;
        shoulderScale.ScaleY = 0;
        ModCatalogModal.IsVisible = true;
        ModCatalogModal.IsHitTestVisible = true;
        InstancesLayout.IsHitTestVisible = false;
        ((BlurEffect)InstancesLayout.Effect!).Radius = 6;

        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
        if (cancellationToken.IsCancellationRequested ||
            DataContext is not MainWindowViewModel { HasModCatalogPreview: true })
        {
            return;
        }

        ModCatalogModalBackdrop.Opacity = 1;
        translation.Y = 0;
        shoulderScale.ScaleY = 1;
    }

    private async Task HideModCatalogModalAsync()
    {
        if (!ModCatalogModal.IsVisible)
            return;

        var cancellationToken = ReplaceModCatalogModalCancellation();
        ModCatalogModal.IsHitTestVisible = false;
        ModCatalogModalBackdrop.Opacity = 0;
        ((TranslateTransform)ModCatalogModalSheet.RenderTransform!).Y = ModCatalogModalHiddenOffset;
        ((ScaleTransform)ModCatalogModalShoulders.RenderTransform!).ScaleY = 0;
        ((BlurEffect)InstancesLayout.Effect!).Radius = 0;

        try
        {
            await Task.Delay(MotionDurations.ModalCloseRetention, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (DataContext is MainWindowViewModel { HasModCatalogPreview: true })
            return;

        ModCatalogModal.IsVisible = false;
        InstancesLayout.IsHitTestVisible = true;
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.CompleteModCatalogPreviewClose();
    }

    private void ApplyModCatalogModalStateImmediately()
    {
        _modCatalogModalCancellation?.Cancel();
        _modCatalogModalCancellation?.Dispose();
        _modCatalogModalCancellation = null;
        var visible = DataContext is MainWindowViewModel { HasModCatalogPreview: true };
        ModCatalogModal.IsVisible = visible;
        ModCatalogModal.IsHitTestVisible = visible;
        ModCatalogModalBackdrop.Opacity = visible ? 1 : 0;
        ((TranslateTransform)ModCatalogModalSheet.RenderTransform!).Y =
            visible ? 0 : ModCatalogModalHiddenOffset;
        ((ScaleTransform)ModCatalogModalShoulders.RenderTransform!).ScaleY = visible ? 1 : 0;
        InstancesLayout.IsHitTestVisible = !visible;
        ((BlurEffect)InstancesLayout.Effect!).Radius = visible ? 6 : 0;
    }

    private CancellationToken ReplaceModCatalogModalCancellation()
    {
        _modCatalogModalCancellation?.Cancel();
        _modCatalogModalCancellation?.Dispose();
        _modCatalogModalCancellation = new CancellationTokenSource();
        return _modCatalogModalCancellation.Token;
    }

    private void OnModCatalogModalBackdropPressed(object? sender, PointerPressedEventArgs args)
    {
        if (DataContext is not MainWindowViewModel { HasModCatalogPreview: true } viewModel)
            return;

        args.Handled = true;
        viewModel.CloseModCatalogPreviewCommand.Execute(null);
    }

    private void OnInstancesKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key is not Key.Escape || !TryCloseModCatalogPreview())
            return;

        args.Handled = true;
    }

    public bool TryCloseModCatalogPreview()
    {
        if (DataContext is not MainWindowViewModel { HasModCatalogPreview: true } viewModel)
            return false;

        viewModel.CloseModCatalogPreviewCommand.Execute(null);
        return true;
    }

    private void UpdateLayout(double width)
    {
        if (width <= 0 || DataContext is not MainWindowViewModel viewModel)
            return;

        var hasInstances = viewModel.HasInstances;
        var wasCompact = _layoutHost.IsCompact;
        _layoutHost.Update(width, hasInstances);
        UpdateInstanceSectionContentWidth();
        var layoutModeChanged = wasCompact != _layoutHost.IsCompact;

        if (!hasInstances)
        {
            EnsureSingleLayoutRow();
            _creatorWizard.ResetNavigationPane();
            InstancesLayout.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            Grid.SetRow(InstancesContent, 0);
            Grid.SetRowSpan(InstancesContent, 1);
            if (layoutModeChanged)
                ApplySectionStateImmediately();
            return;
        }

        EnsureSingleLayoutRow();
        InstancesLayout.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
        Grid.SetRow(InstancesListPane, 0);
        Grid.SetRow(InstancesContent, 0);
        Grid.SetRowSpan(InstancesContent, 1);

        if (!_layoutHost.IsCompact)
        {
            if (viewModel.IsInstanceCreatorOpen)
                _creatorWizard.HideNavigationPane(animate: false);
            else
                _creatorWizard.ShowNavigationPane(animate: false);
            if (layoutModeChanged)
                ApplySectionStateImmediately();
            return;
        }

        _creatorWizard.ResetNavigationPane();
        Grid.SetRowSpan(InstancesContent, 1);
        if (layoutModeChanged)
            ApplySectionStateImmediately();
    }

    private void EnsureSingleLayoutRow()
    {
        while (InstancesLayout.RowDefinitions.Count > 1)
            InstancesLayout.RowDefinitions.RemoveAt(InstancesLayout.RowDefinitions.Count - 1);
    }

    private void UpdateInstanceSectionContentWidth()
    {
        var maxWidth = _layoutHost.IsCompact
            ? double.PositiveInfinity
            : AdaptiveMasterDetailHost.DefaultContentMaxWidth;
        InstalledModsSection.MaxWidth = maxWidth;
        ModCatalogSection.MaxWidth = maxWidth;
        InstanceConsoleSection.MaxWidth = maxWidth;
    }

    private void OnInstanceClicked(object? sender, RoutedEventArgs args)
    {
        _layoutHost.RememberDetail();
        if (!_layoutHost.IsCompact)
            return;

        _layoutHost.OpenDetail();
    }

    private void OnManagedInstanceActionPointerExited(object? sender, PointerEventArgs args)
    {
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.ArmManagedInstanceCancellation();
    }

    private void OnInstanceDragHandlePressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is not Border { DataContext: InstanceItemViewModel instance } handle)
            return;

        _instanceReorder.Begin(handle, instance.Id, args, 66, () =>
        {
            InstanceDragPreviewName.Text = instance.Name;
            InstanceDragPreviewBranch.Text = instance.Branch;
        });
    }

    private void OnInstanceDragHandleMoved(object? sender, PointerEventArgs args)
        => _instanceReorder.Move(args);

    private void OnInstanceDragHandleReleased(object? sender, PointerReleasedEventArgs args)
    {
        _instanceReorder.Complete(
            args,
            (instanceId, targetIndex) =>
            {
                if (DataContext is MainWindowViewModel viewModel)
                    viewModel.MoveInstance(instanceId, targetIndex);
            },
            () =>
            {
                InstanceDragPreviewName.Text = string.Empty;
                InstanceDragPreviewBranch.Text = string.Empty;
            });
    }

    private void OnOpenCreatorClicked(object? sender, RoutedEventArgs args)
    {
        _creatorOpenedFromCompactList = _layoutHost.IsOpeningWizardFromMaster();
        if (_layoutHost.IsCompact && !_creatorOpenedFromCompactList)
            _layoutHost.OpenDetail();

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

    private void OnCompactInstanceBackClicked(object? sender, RoutedEventArgs args)
        => TryCloseCompactContent();

    public bool TryCloseCompactContent()
    {
        if (!_layoutHost.IsCompact || !_layoutHost.IsDetailOpen)
            return false;

        if (DataContext is MainWindowViewModel { IsInstanceCreatorOpen: true } viewModel)
        {
            viewModel.CloseInstanceCreatorCommand.Execute(null);
            return true;
        }

        return _layoutHost.TryCloseDetail();
    }

    public bool TryNavigateBack()
    {
        if (TryCloseModCatalogPreview())
            return true;

        if (DataContext is MainWindowViewModel { IsInstanceOverviewSection: false } viewModel)
        {
            viewModel.CloseInstanceSectionCommand.Execute(null);
            return true;
        }

        return TryCloseCompactContent();
    }

    private async Task PlayCreatorOpenAnimationAsync()
    {
        var revision = ++_creatorNavigationRevision;
        if (_creatorOpenedFromCompactList && _layoutHost.IsCompact)
        {
            await _creatorWizard.ShowWizardForCompactEntryAsync();
            if (revision != _creatorNavigationRevision ||
                DataContext is not MainWindowViewModel { IsInstanceCreatorOpen: true })
            {
                return;
            }

            _layoutHost.OpenDetail();
            UpdateBranchIndicator(animate: false);
            return;
        }

        if (!_layoutHost.IsCompact &&
            DataContext is MainWindowViewModel { HasInstances: true })
        {
            _creatorWizard.HideNavigationPane(animate: true);
        }

        await _creatorWizard.OpenAsync(
            () => DataContext is MainWindowViewModel { IsInstanceCreatorOpen: true },
            () => UpdateBranchIndicator(animate: false));
    }

    private async Task PlayCreatorCloseAnimationAsync()
    {
        var revision = ++_creatorNavigationRevision;
        if (_creatorOpenedFromCompactList && _layoutHost.IsCompact)
        {
            _creatorWizard.Cancel();
            _layoutHost.TryCloseDetail();
            await Task.Delay(CompactContentTransitionDuration);
            if (revision == _creatorNavigationRevision &&
                DataContext is MainWindowViewModel { IsInstanceCreatorOpen: false })
            {
                _creatorWizard.ShowOverviewImmediately();
                _creatorOpenedFromCompactList = false;
            }

            return;
        }

        await _creatorWizard.CloseAsync(
            () => DataContext is MainWindowViewModel { IsInstanceCreatorOpen: false },
            () =>
            {
                if (!_layoutHost.IsCompact &&
                    DataContext is MainWindowViewModel { HasInstances: true })
                {
                    _creatorWizard.ShowNavigationPane(animate: true);
                }

                _creatorOpenedFromCompactList = false;
            });
    }

    private void HideCreatorImmediately()
    {
        ++_creatorNavigationRevision;
        _creatorOpenedFromCompactList = false;
        _creatorWizard.ShowOverviewImmediately();
        if (!_layoutHost.IsCompact &&
            DataContext is MainWindowViewModel { HasInstances: true })
        {
            _creatorWizard.ShowNavigationPane(animate: false);
        }
    }

    private async Task PlaySectionOpenAnimationAsync()
    {
        CancelSectionAnimation();
        if (_layoutHost.IsCompact)
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
        if (_layoutHost.IsCompact)
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
        var compact = _layoutHost.IsCompact;
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

    private static bool IsModArchiveName(string? fileName)
        => !string.IsNullOrWhiteSpace(fileName) &&
           (fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".zip.disabled", StringComparison.OrdinalIgnoreCase));

    private void OnModFilesDragEntered(object? sender, DragEventArgs args)
    {
        args.Handled = true;
        if (DataContext is not MainWindowViewModel { IsManagedInstanceInstalled: true } ||
            !ContainsFiles(args.DataTransfer))
        {
            args.DragEffects = DragDropEffects.None;
            ShowModDropOverlay(false);
            return;
        }

        args.DragEffects = DragDropEffects.Copy;
        ShowModDropOverlay(true);
    }

    private void OnModFilesDragLeft(object? sender, DragEventArgs args)
    {
        args.Handled = true;
        ShowModDropOverlay(false);
    }

    private async void OnModFilesDropped(object? sender, DragEventArgs args)
    {
        args.Handled = true;
        ShowModDropOverlay(false);
        if (DataContext is not MainWindowViewModel viewModel ||
            args.DataTransfer is not IAsyncDataTransfer data ||
            !ContainsFiles(args.DataTransfer))
        {
            return;
        }

        try
        {
            var files = await data.TryGetFilesAsync() ?? [];
            var paths = files
                .Select(file => file.TryGetLocalPath())
                .OfType<string>()
                .Where(IsModArchiveName)
                .ToList();
            await viewModel.ImportModFilesAsync(paths);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed drop keeps the mod list untouched
        }
    }

    private static bool ContainsFiles(IDataTransfer data)
        => data.Formats is { } formats && formats.Contains(DataFormat.File);

    private void ShowModDropOverlay(bool visible)
    {
        if (_modDropActive == visible)
            return;

        _modDropActive = visible;
        ModDropOverlay.IsVisible = visible;
    }

    private void OnCatalogSearchKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key is not (Key.Enter or Key.Return) ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        args.Handled = true;
        viewModel.SearchModCatalogCommand.Execute(null);
    }

    private void OnCatalogModPreviewRequested(object? sender, TappedEventArgs args)
    {
        if (sender is not Border { DataContext: ModCatalogItemViewModel item } ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (args.Source is Button or CheckBox ||
            args.Source is Visual visual &&
            (visual.FindAncestorOfType<Button>() is not null ||
             visual.FindAncestorOfType<CheckBox>() is not null))
        {
            return;
        }

        viewModel.SelectModCatalogPreviewCommand.Execute(item);
    }

    private void OnModCatalogScrollChanged(object? sender, ScrollChangedEventArgs args)
    {
        if (args.OffsetDelta.Y == 0 ||
            sender is not ScrollViewer scrollViewer ||
            DataContext is not MainWindowViewModel viewModel ||
            !viewModel.CanLoadMoreModCatalog)
        {
            return;
        }

        if (scrollViewer.Offset.Y + scrollViewer.Viewport.Height >=
            scrollViewer.Extent.Height - 220)
        {
            viewModel.LoadMoreModCatalogCommand.Execute(null);
        }
    }

    private void OnConsoleScrollChanged(object? sender, ScrollChangedEventArgs args)
    {
        if (args.OffsetDelta.Y == 0 ||
            sender is not ScrollViewer scrollViewer ||
            DataContext is not MainWindowViewModel viewModel ||
            !viewModel.IsConsoleAutoScroll)
        {
            return;
        }

        var distanceFromBottom = scrollViewer.Extent.Height -
                                 scrollViewer.Offset.Y -
                                 scrollViewer.Viewport.Height;
        if (distanceFromBottom > 24)
            viewModel.IsConsoleAutoScroll = false;
    }

    private void ScrollConsoleToBottom()
    {
        if (DataContext is not MainWindowViewModel { IsConsoleAutoScroll: true } viewModel ||
            viewModel.ConsoleLines.Count == 0)
        {
            return;
        }

        ConsoleList.ScrollIntoView(viewModel.ConsoleLines[viewModel.ConsoleLines.Count - 1]);
    }

    private void OnCloseModDeleteFlyoutClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is not Control control)
            return;

        var popup = control.FindAncestorOfType<Popup>() ??
            control.GetLogicalAncestors().OfType<Popup>().FirstOrDefault();
        if (popup is not null)
            popup.IsOpen = false;
    }

}
