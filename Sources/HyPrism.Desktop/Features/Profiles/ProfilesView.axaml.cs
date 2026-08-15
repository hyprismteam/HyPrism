// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HyPrism.Desktop.Features.Profiles;

/// <summary>
/// Hosts the responsive master-detail layout for the profile manager.
/// </summary>
public sealed partial class ProfilesView : UserControl
{
    private const double WideLayoutThreshold = 940;
    private const double WideContentMaxWidth = 720;
    private static readonly TimeSpan CreatorAnimationPhaseDuration = TimeSpan.FromMilliseconds(190);

    private INotifyPropertyChanged? _viewModel;
    private bool? _usesCompactLayout;
    private bool _compactContentOpen;
    private bool _returnToCompactContent;
    private bool _isCreatorVisible;
    private Button? _profileDragRow;
    private Control? _profileDragHandle;
    private string? _draggedProfileId;
    private Point _profileDragStart;
    private Point _profileDragStartInLayout;
    private Point _profileDragPreviewOrigin;
    private int _profileDragTargetIndex = -1;
    private bool _isProfileDragActive;
    private CancellationTokenSource? _creatorAnimationCancellation;

    private TranslateTransform MainTranslation
        => (TranslateTransform)ProfileMain.RenderTransform!;

    public ProfilesView()
    {
        InitializeComponent();
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
        _isCreatorVisible = DataContext is ProfilesViewModel { IsCreationVisible: true };
        if (_isCreatorVisible)
            _ = PlayCreatorOpenAnimationAsync();
        else
            HideCreatorImmediately();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ProfilesViewModel.HasProfiles) or
            nameof(ProfilesViewModel.IsCreationVisible))
        {
            UpdateLayout(Bounds.Width);
        }

        if (args.PropertyName is nameof(ProfilesViewModel.IsCreationVisible))
        {
            var isCreatorVisible = DataContext is ProfilesViewModel { IsCreationVisible: true };
            if (_isCreatorVisible == isCreatorVisible)
                return;

            _isCreatorVisible = isCreatorVisible;
            if (isCreatorVisible)
                _ = PlayCreatorOpenAnimationAsync();
            else
                _ = PlayCreatorCloseAnimationAsync();
        }
    }

    private void OnProfilesViewSizeChanged(object? sender, SizeChangedEventArgs args)
        => UpdateLayout(args.NewSize.Width);

    private void UpdateLayout(double width)
    {
        if (width <= 0 || DataContext is not ProfilesViewModel viewModel)
            return;

        var compact = width < WideLayoutThreshold;
        var layoutModeChanged = _usesCompactLayout != compact;
        Classes.Set("compact", compact);
        Classes.Set("wide", !compact);

        if (layoutModeChanged)
        {
            if (compact)
                _compactContentOpen = _returnToCompactContent;
            else if (_usesCompactLayout is true)
                _returnToCompactContent = _compactContentOpen;

            _usesCompactLayout = compact;
        }

        ProfilesContentHost.Margin = compact
            ? new Thickness(24, 16, 24, 36)
            : new Thickness(32, 28, 32, 40);
        ProfilesContentHost.MaxWidth = compact
            ? double.PositiveInfinity
            : WideContentMaxWidth;

        if (!viewModel.HasProfiles)
        {
            ProfilesLayout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            ProfilesLayout.ColumnDefinitions[1].Width = new GridLength(0);
            Grid.SetColumn(ProfileMain, 0);
            Grid.SetColumnSpan(ProfileMain, 2);
            ProfilesListPane.IsHitTestVisible = false;
            ProfileMain.IsHitTestVisible = true;
            CompactProfilesToolbar.IsVisible = false;
            SetMainOffsetWithoutTransition(0);
            return;
        }

        CompactProfilesToolbar.IsVisible = compact && viewModel.IsProfileEditorVisible;
        Grid.SetColumnSpan(ProfileMain, 1);

        if (!compact)
        {
            ProfilesLayout.ColumnDefinitions[0].Width = new GridLength(276);
            ProfilesLayout.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(ProfilesListPane, 0);
            Grid.SetColumn(ProfileMain, 1);
            ProfilesListPane.IsHitTestVisible = true;
            ProfileMain.IsHitTestVisible = true;
            SetMainOffsetWithoutTransition(0);
            return;
        }

        ProfilesLayout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        ProfilesLayout.ColumnDefinitions[1].Width = new GridLength(0);
        Grid.SetColumn(ProfilesListPane, 0);
        Grid.SetColumn(ProfileMain, 0);
        Grid.SetColumnSpan(ProfileMain, 2);
        ProfilesListPane.IsHitTestVisible = !_compactContentOpen;
        ProfileMain.IsHitTestVisible = _compactContentOpen;
        SetMainOffsetWithoutTransition(_compactContentOpen ? 0 : width);
    }

    private void OnProfileSelectedClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: ProfileItemViewModel profile } &&
            DataContext is ProfilesViewModel viewModel)
        {
            viewModel.SelectProfileCommand.Execute(profile);
        }

        _returnToCompactContent = true;
        if (_usesCompactLayout is true)
            OpenCompactContent();
    }

    private void OnCreateProfileClicked(object? sender, RoutedEventArgs args)
    {
        _returnToCompactContent = true;
        if (_usesCompactLayout is true)
            OpenCompactContent();
    }

    private void OnCompactProfilesBackClicked(object? sender, RoutedEventArgs args)
        => TryCloseCompactContent();

    private static void OnProfileMenuPointerPressed(object? sender, PointerPressedEventArgs args)
        => args.Handled = true;

    private void OnToggleProfileMenuPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (sender is Border { DataContext: ProfileItemViewModel profile })
            profile.IsMenuOpen = !profile.IsMenuOpen;

        args.Handled = true;
    }

    private void OnCloseProfileMenuClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: ProfileItemViewModel profile })
            profile.IsMenuOpen = false;
    }

    private void OnProfileDragHandlePressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is not Border { DataContext: ProfileItemViewModel profile } handle ||
            !args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _profileDragHandle = handle;
        _profileDragRow = handle.FindAncestorOfType<Button>();
        _draggedProfileId = profile.Id;
        _profileDragStart = args.GetPosition(ProfilesItems);
        _profileDragStartInLayout = args.GetPosition(ProfilesLayout);
        _profileDragPreviewOrigin = _profileDragRow?.TranslatePoint(default, ProfilesLayout) ?? default;
        _profileDragTargetIndex = -1;
        _isProfileDragActive = false;
        ProfileDragPreviewName.Text = profile.Name;
        ProfileDragPreviewType.Text = profile.AccountType;
        ProfileDragPreview.Width = _profileDragRow?.Bounds.Width ?? ProfilesListPane.Bounds.Width;
        ProfileDragPreview.Height = _profileDragRow?.Bounds.Height ?? 72;
        args.Pointer.Capture(handle);
        args.Handled = true;
    }

    private void OnProfileDragHandleMoved(object? sender, PointerEventArgs args)
    {
        if (_profileDragHandle is null || _draggedProfileId is null)
            return;

        var position = args.GetPosition(ProfilesItems);
        if (!_isProfileDragActive)
        {
            var delta = position - _profileDragStart;
            if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 5)
                return;

            _isProfileDragActive = true;
            _profileDragRow?.Classes.Add("dragging");
            ProfileDragPreview.IsVisible = true;
        }

        var pointerInLayout = args.GetPosition(ProfilesLayout);
        var transform = (TranslateTransform)ProfileDragPreview.RenderTransform!;
        transform.X = _profileDragPreviewOrigin.X +
                      pointerInLayout.X -
                      _profileDragStartInLayout.X + 10;
        transform.Y = _profileDragPreviewOrigin.Y +
                      pointerInLayout.Y -
                      _profileDragStartInLayout.Y + 8;
        _profileDragTargetIndex = GetProfileDropTargetIndex(position.Y);
        args.Handled = true;
    }

    private void OnProfileDragHandleReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (_isProfileDragActive &&
            _profileDragTargetIndex >= 0 &&
            _draggedProfileId is not null &&
            DataContext is ProfilesViewModel viewModel)
        {
            viewModel.MoveProfile(_draggedProfileId, _profileDragTargetIndex);
        }

        args.Pointer.Capture(null);
        ResetProfileDragState();
        args.Handled = true;
    }

    private int GetProfileDropTargetIndex(double pointerY)
    {
        var rows = ProfilesItems.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.Classes.Contains("instancesListItem"))
            .Select(button => new
            {
                Button = button,
                Origin = button.TranslatePoint(default, ProfilesItems)
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

    private void ResetProfileDragState()
    {
        _profileDragRow?.Classes.Remove("dragging");
        ProfileDragPreview.IsVisible = false;
        ProfileDragPreviewName.Text = string.Empty;
        ProfileDragPreviewType.Text = string.Empty;
        _profileDragHandle = null;
        _profileDragRow = null;
        _draggedProfileId = null;
        _profileDragTargetIndex = -1;
        _isProfileDragActive = false;
    }

    private async void OnCopyUuidClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ProfilesViewModel { SelectedProfile: { } profile })
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is not null)
            await topLevel.Clipboard.SetTextAsync(profile.Uuid);
    }

    private void OnCloseCompactProfileMenuClicked(object? sender, RoutedEventArgs args)
        => CompactProfileMenuPopup.IsRequestedOpen = false;

    private void OnToggleCompactProfileMenuClicked(object? sender, RoutedEventArgs args)
        => CompactProfileMenuPopup.IsRequestedOpen = !CompactProfileMenuPopup.IsRequestedOpen;

    public bool TryCloseCompactContent()
    {
        if (_usesCompactLayout is not true || !_compactContentOpen)
            return false;

        if (DataContext is ProfilesViewModel { IsCreationVisible: true } viewModel)
            viewModel.CancelCreationCommand.Execute(null);

        _returnToCompactContent = false;
        _compactContentOpen = false;
        ProfileMain.IsHitTestVisible = false;
        ProfilesListPane.IsHitTestVisible = true;
        MainTranslation.X = Bounds.Width;
        return true;
    }

    private void OpenCompactContent()
    {
        _compactContentOpen = true;
        ProfilesListPane.IsHitTestVisible = false;
        ProfileMain.IsHitTestVisible = true;
        MainTranslation.X = 0;
    }

    private void SetMainOffsetWithoutTransition(double offset)
    {
        var transitions = MainTranslation.Transitions;
        MainTranslation.Transitions = null;
        MainTranslation.X = offset;
        MainTranslation.Transitions = transitions;
    }

    private async Task PlayCreatorOpenAnimationAsync()
    {
        CancelCreatorAnimation();

        var overviewTranslation = (TranslateTransform)ProfileOverview.RenderTransform!;
        var wizardTranslation = (TranslateTransform)ProfileCreatorScreen.RenderTransform!;
        ProfileOverview.IsHitTestVisible = false;
        ProfileCreatorScreen.IsHitTestVisible = false;
        _creatorAnimationCancellation = new CancellationTokenSource();
        var cancellationToken = _creatorAnimationCancellation.Token;

        try
        {
            ProfileOverview.Opacity = 0;
            overviewTranslation.X = -28;
            await Task.Delay(CreatorAnimationPhaseDuration, cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                DataContext is not ProfilesViewModel { IsCreationVisible: true })
            {
                return;
            }

            ProfileOverview.IsVisible = false;
            PrepareWizardForEntry(wizardTranslation);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            if (cancellationToken.IsCancellationRequested)
                return;

            ProfileCreatorScreen.IsHitTestVisible = true;
            ProfileCreatorScreen.Opacity = 1;
            wizardTranslation.X = 0;
        }
        catch (OperationCanceledException)
        {
            // A reverse navigation replaces the pending wizard transition
        }
    }

    private async Task PlayCreatorCloseAnimationAsync()
    {
        CancelCreatorAnimation();
        if (!ProfileCreatorScreen.IsVisible)
            return;

        _creatorAnimationCancellation = new CancellationTokenSource();
        var cancellationToken = _creatorAnimationCancellation.Token;
        var wizardTranslation = (TranslateTransform)ProfileCreatorScreen.RenderTransform!;
        ProfileCreatorScreen.IsHitTestVisible = false;
        ProfileCreatorScreen.Opacity = 0;
        wizardTranslation.X = 28;

        try
        {
            await Task.Delay(CreatorAnimationPhaseDuration, cancellationToken);
            if (!cancellationToken.IsCancellationRequested &&
                DataContext is ProfilesViewModel { IsCreationVisible: false })
            {
                ProfileCreatorScreen.IsVisible = false;
                var overviewTranslation = (TranslateTransform)ProfileOverview.RenderTransform!;
                PrepareOverviewForEntry(overviewTranslation);
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
                if (cancellationToken.IsCancellationRequested)
                    return;

                ProfileOverview.IsHitTestVisible = true;
                ProfileOverview.Opacity = 1;
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
        var overviewTranslation = (TranslateTransform)ProfileOverview.RenderTransform!;
        var wizardTranslation = (TranslateTransform)ProfileCreatorScreen.RenderTransform!;
        var overviewTransitions = ProfileOverview.Transitions;
        var wizardTransitions = ProfileCreatorScreen.Transitions;
        var overviewTranslationTransitions = overviewTranslation.Transitions;
        var wizardTranslationTransitions = wizardTranslation.Transitions;
        ProfileOverview.Transitions = null;
        ProfileCreatorScreen.Transitions = null;
        overviewTranslation.Transitions = null;
        wizardTranslation.Transitions = null;
        ProfileOverview.Opacity = 1;
        overviewTranslation.X = 0;
        ProfileOverview.IsVisible = true;
        ProfileOverview.IsHitTestVisible = true;
        ProfileCreatorScreen.Opacity = 0;
        wizardTranslation.X = 36;
        ProfileCreatorScreen.IsVisible = false;
        ProfileCreatorScreen.IsHitTestVisible = false;
        ProfileOverview.Transitions = overviewTransitions;
        ProfileCreatorScreen.Transitions = wizardTransitions;
        overviewTranslation.Transitions = overviewTranslationTransitions;
        wizardTranslation.Transitions = wizardTranslationTransitions;
    }

    private void PrepareWizardForEntry(TranslateTransform wizardTranslation)
    {
        var transitions = ProfileCreatorScreen.Transitions;
        var translationTransitions = wizardTranslation.Transitions;
        ProfileCreatorScreen.Transitions = null;
        wizardTranslation.Transitions = null;
        ProfileCreatorScreen.Opacity = 0;
        wizardTranslation.X = 28;
        ProfileCreatorScreen.IsVisible = true;
        ProfileCreatorScreen.Transitions = transitions;
        wizardTranslation.Transitions = translationTransitions;
    }

    private void PrepareOverviewForEntry(TranslateTransform overviewTranslation)
    {
        var transitions = ProfileOverview.Transitions;
        var translationTransitions = overviewTranslation.Transitions;
        ProfileOverview.Transitions = null;
        overviewTranslation.Transitions = null;
        ProfileOverview.Opacity = 0;
        overviewTranslation.X = -28;
        ProfileOverview.IsVisible = true;
        ProfileOverview.Transitions = transitions;
        overviewTranslation.Transitions = translationTransitions;
    }

    private void CancelCreatorAnimation()
    {
        _creatorAnimationCancellation?.Cancel();
        _creatorAnimationCancellation?.Dispose();
        _creatorAnimationCancellation = null;
    }
}
