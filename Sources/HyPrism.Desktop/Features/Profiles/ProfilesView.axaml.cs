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
using HyPrism.Desktop.Controls;

namespace HyPrism.Desktop.Features.Profiles;

/// <summary>
/// Hosts the responsive master-detail layout for the profile manager.
/// </summary>
public sealed partial class ProfilesView : UserControl
{
    private const double WideLayoutThreshold = 940;
    private const double WideContentMaxWidth = 720;
    private static readonly TimeSpan CompactContentTransitionDuration = TimeSpan.FromMilliseconds(320);

    private readonly WizardScreenTransition _creatorTransition;
    private INotifyPropertyChanged? _viewModel;
    private bool? _usesCompactLayout;
    private bool _compactContentOpen;
    private bool _returnToCompactContent;
    private bool _creatorOpenedFromCompactList;
    private bool _isCreatorVisible;
    private Button? _profileDragRow;
    private Control? _profileDragHandle;
    private string? _draggedProfileId;
    private Point _profileDragStart;
    private Point _profileDragStartInLayout;
    private Point _profileDragPreviewOrigin;
    private int _profileDragTargetIndex = -1;
    private bool _isProfileDragActive;
    private int _creatorNavigationRevision;

    private TranslateTransform MainTranslation
        => (TranslateTransform)ProfileMain.RenderTransform!;

    public ProfilesView()
    {
        InitializeComponent();
        _creatorTransition = new WizardScreenTransition(
            ProfileOverview,
            ProfileCreatorScreen,
            ProfilesListPane);
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
        if (args.PropertyName is nameof(ProfilesViewModel.HasProfiles))
            UpdateLayout(Bounds.Width);

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
            _creatorTransition.ResetNavigationPane();
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
            ProfilesLayout.ColumnDefinitions[0].Width = GridLength.Auto;
            ProfilesLayout.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(ProfilesListPane, 0);
            Grid.SetColumn(ProfileMain, 1);
            if (viewModel.IsCreationVisible)
                _creatorTransition.HideNavigationPane(animate: false);
            else
                _creatorTransition.ShowNavigationPane(animate: false);
            ProfileMain.IsHitTestVisible = true;
            SetMainOffsetWithoutTransition(0);
            return;
        }

        _creatorTransition.ResetNavigationPane();
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
        _creatorOpenedFromCompactList = _usesCompactLayout is true &&
                                        !_compactContentOpen &&
                                        DataContext is ProfilesViewModel { HasProfiles: true };
        if (_usesCompactLayout is true && !_creatorOpenedFromCompactList)
            OpenCompactContent();

        if (DataContext is ProfilesViewModel viewModel)
            viewModel.ShowCreateChoiceCommand.Execute(null);
    }

    private async void OnBeginOfficialProfileCreationClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ProfilesViewModel viewModel)
            return;

        await _creatorTransition.SwitchStepAsync(
            ProfileCreationChoiceContent,
            OfficialProfileCreationContent,
            forward: true,
            () => viewModel.BeginOfficialCreationCommand.Execute(null),
            () => viewModel.IsCreationVisible);
    }

    private async void OnBeginOfflineProfileCreationClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ProfilesViewModel viewModel)
            return;

        await _creatorTransition.SwitchStepAsync(
            ProfileCreationChoiceContent,
            OfflineProfileCreationContent,
            forward: true,
            () => viewModel.BeginOfflineCreationCommand.Execute(null),
            () => viewModel.IsCreationVisible);
    }

    private async void OnReturnToProfileCreationChoiceClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ProfilesViewModel viewModel)
            return;

        var outgoingStep = viewModel.IsOfficialCreationVisible
            ? OfficialProfileCreationContent
            : OfflineProfileCreationContent;
        await _creatorTransition.SwitchStepAsync(
            outgoingStep,
            ProfileCreationChoiceContent,
            forward: false,
            () => viewModel.ReturnToCreationChoiceCommand.Execute(null),
            () => viewModel.IsCreationVisible);
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
        {
            viewModel.CancelCreationCommand.Execute(null);
            return true;
        }

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
        RestoreCurrentCreatorStep();
        var revision = ++_creatorNavigationRevision;
        if (_creatorOpenedFromCompactList && _usesCompactLayout is true)
        {
            _creatorTransition.ShowWizardImmediately();
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            if (revision != _creatorNavigationRevision ||
                DataContext is not ProfilesViewModel { IsCreationVisible: true })
            {
                return;
            }

            OpenCompactContent();
            return;
        }

        if (_usesCompactLayout is false &&
            DataContext is ProfilesViewModel { HasProfiles: true })
        {
            _creatorTransition.HideNavigationPane(animate: true);
        }

        await _creatorTransition.OpenAsync(
            () => DataContext is ProfilesViewModel { IsCreationVisible: true });
    }

    private async Task PlayCreatorCloseAnimationAsync()
    {
        var revision = ++_creatorNavigationRevision;
        if (_creatorOpenedFromCompactList && _usesCompactLayout is true)
        {
            _creatorTransition.Cancel();
            _returnToCompactContent = false;
            _compactContentOpen = false;
            ProfileMain.IsHitTestVisible = false;
            ProfilesListPane.IsHitTestVisible = true;
            MainTranslation.X = Bounds.Width;
            await Task.Delay(CompactContentTransitionDuration);
            if (revision == _creatorNavigationRevision &&
                DataContext is ProfilesViewModel { IsCreationVisible: false } viewModel)
            {
                _creatorTransition.ShowOverviewImmediately();
                CompleteCreatorClose(viewModel);
            }

            return;
        }

        await _creatorTransition.CloseAsync(
            () => DataContext is ProfilesViewModel { IsCreationVisible: false },
            () =>
            {
                if (DataContext is ProfilesViewModel viewModel)
                {
                    if (_usesCompactLayout is false && viewModel.HasProfiles)
                        _creatorTransition.ShowNavigationPane(animate: true);

                    CompleteCreatorClose(viewModel);
                }
            });
    }

    private void HideCreatorImmediately()
    {
        ++_creatorNavigationRevision;
        _creatorOpenedFromCompactList = false;
        _creatorTransition.ShowOverviewImmediately();
        if (_usesCompactLayout is false &&
            DataContext is ProfilesViewModel { HasProfiles: true })
        {
            _creatorTransition.ShowNavigationPane(animate: false);
        }
        RestoreCurrentCreatorStep();
    }

    private void CompleteCreatorClose(ProfilesViewModel viewModel)
    {
        _creatorOpenedFromCompactList = false;
        viewModel.CompleteCreationTransition();
        RestoreCurrentCreatorStep();
    }

    private void RestoreCurrentCreatorStep()
    {
        if (DataContext is ProfilesViewModel { IsOfficialCreationVisible: true })
        {
            _creatorTransition.ShowStepImmediately(
                OfficialProfileCreationContent,
                ProfileCreationChoiceContent,
                OfflineProfileCreationContent);
            return;
        }

        if (DataContext is ProfilesViewModel { IsOfflineCreationVisible: true })
        {
            _creatorTransition.ShowStepImmediately(
                OfflineProfileCreationContent,
                ProfileCreationChoiceContent,
                OfficialProfileCreationContent);
            return;
        }

        _creatorTransition.ShowStepImmediately(
            ProfileCreationChoiceContent,
            OfflineProfileCreationContent,
            OfficialProfileCreationContent);
    }
}
