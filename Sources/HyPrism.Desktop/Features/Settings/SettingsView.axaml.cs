// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HyPrism.Desktop.Controls;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace HyPrism.Desktop.Features.Settings;

public sealed partial class SettingsView : UserControl
{
    private const double WideLayoutThreshold = 940;
    private const double WideContentMaxWidth = 720;

    private bool? _usesCompactLayout;
    private bool _compactContentOpen;
    private bool _returnToCompactContent;
    private double _backgroundPickerWidth;
    private readonly Stopwatch _availabilitySpinnerClock = new();
    private readonly DispatcherTimer _availabilitySpinnerTimer;
    private readonly WizardScreenTransition _downloadSourceTransition;
    private INotifyPropertyChanged? _viewModel;
    private bool _isDownloadSourceWizardVisible;

    private TranslateTransform MainTranslation
        => (TranslateTransform)SettingsMain.RenderTransform!;

    public SettingsView()
    {
        InitializeComponent();
        _downloadSourceTransition = new WizardScreenTransition(
            SettingsOverview,
            DownloadSourceWizardScreen,
            SettingsCategoryRail,
            DownloadSourceWizardAnimationAnchor,
            DownloadSourceWizardAnimationMotion);
        _availabilitySpinnerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _availabilitySpinnerTimer.Tick += OnAvailabilitySpinnerTick;
        AttachedToVisualTree += (_, _) =>
        {
            _availabilitySpinnerClock.Restart();
            _availabilitySpinnerTimer.Start();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _availabilitySpinnerTimer.Stop();
            _availabilitySpinnerClock.Reset();
        };
        DataContextChanged += OnDataContextChanged;
    }

    private void OnAvailabilitySpinnerTick(object? sender, EventArgs args)
    {
        if (!IsEffectivelyVisible)
            return;

        const double rotationDurationMilliseconds = 800;
        var angle = _availabilitySpinnerClock.Elapsed.TotalMilliseconds % rotationDurationMilliseconds /
                    rotationDurationMilliseconds * 360;
        foreach (var spinner in this.GetVisualDescendants()
                     .OfType<ShapePath>()
                     .Where(path => path.Classes.Contains("sourceAvailabilitySpinner")))
        {
            spinner.RenderTransform ??= new RotateTransform();
            ((RotateTransform)spinner.RenderTransform).Angle = angle;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as INotifyPropertyChanged;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _isDownloadSourceWizardVisible = DataContext is SettingsViewModel { IsAddingMirror: true };
        if (_isDownloadSourceWizardVisible)
            _ = PlayDownloadSourceWizardOpenAsync();
        else
            HideDownloadSourceWizardImmediately();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SettingsViewModel.IsAddingMirror))
            return;

        var isVisible = DataContext is SettingsViewModel { IsAddingMirror: true };
        if (_isDownloadSourceWizardVisible == isVisible)
            return;

        _isDownloadSourceWizardVisible = isVisible;
        if (isVisible)
            _ = PlayDownloadSourceWizardOpenAsync();
        else
            _ = PlayDownloadSourceWizardCloseAsync();
    }

    private void OnSettingsViewSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < WideLayoutThreshold;
        if (_usesCompactLayout != compact)
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
            ApplyLayoutMode(compact, e.NewSize.Width);
        }
        else if (compact && !_compactContentOpen)
        {
            SetMainOffsetWithoutTransition(e.NewSize.Width);
        }

        SettingsContentHost.Margin = compact
            ? new Thickness(24, 16, 24, 36)
            : new Thickness(32, 28, 32, 40);
        SettingsContentHost.MaxWidth = compact
            ? double.PositiveInfinity
            : WideContentMaxWidth;
    }

    private void ApplyLayoutMode(bool compact, double width)
    {
        SettingsCategoryRail.Classes.Set("compact", compact);
        var viewModel = DataContext as SettingsViewModel;
        if (viewModel is not null)
            viewModel.IsCompactLayout = compact;

        CompactSettingsToolbar.IsVisible = compact;
        if (compact)
        {
            _downloadSourceTransition.ResetNavigationPane();
            SettingsLayout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            SettingsLayout.ColumnDefinitions[1].Width = new GridLength(0);
            Grid.SetColumn(SettingsMain, 0);
            SettingsCategoryRail.IsHitTestVisible = !_compactContentOpen;
            SettingsMain.IsHitTestVisible = _compactContentOpen;
            SetMainOffsetWithoutTransition(_compactContentOpen ? 0 : width);
            return;
        }

        SettingsLayout.ColumnDefinitions[0].Width = GridLength.Auto;
        SettingsLayout.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(SettingsMain, 1);
        if (viewModel?.IsAddingMirror == true)
            _downloadSourceTransition.HideNavigationPane(animate: false);
        else
            _downloadSourceTransition.ShowNavigationPane(animate: false);
        SettingsCategoryRail.IsHitTestVisible = true;
        SettingsMain.IsHitTestVisible = true;
        SetMainOffsetWithoutTransition(0);
    }

    private void OnSettingsCategoryClicked(object? sender, RoutedEventArgs e)
    {
        _returnToCompactContent = true;
        Dispatcher.UIThread.Post(SettingsContent.ScrollToHome, DispatcherPriority.Background);
        if (_usesCompactLayout is not true)
            return;

        _compactContentOpen = true;
        SettingsCategoryRail.IsHitTestVisible = false;
        SettingsMain.IsHitTestVisible = true;
        MainTranslation.X = 0;
    }

    private void OnCompactSettingsBackClicked(object? sender, RoutedEventArgs e)
        => TryCloseCompactContent();

    private static void OnMirrorMenuPointerPressed(object? sender, PointerPressedEventArgs args)
        => args.Handled = true;

    private void OnToggleMirrorMenuPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (sender is Border { DataContext: MirrorSourceViewModel mirror })
            mirror.IsMenuOpen = !mirror.IsMenuOpen;

        args.Handled = true;
    }

    private void OnCloseMirrorMenuClicked(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: MirrorSourceViewModel mirror })
            mirror.IsMenuOpen = false;
    }

    private async void OnBeginAutomaticSourceAdditionClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not SettingsViewModel viewModel)
            return;

        await _downloadSourceTransition.SwitchStepAsync(
            SourceAdditionChoiceContent,
            AutomaticSourceAdditionContent,
            forward: true,
            () => viewModel.BeginAutomaticMirrorAdditionCommand.Execute(null),
            () => viewModel.IsAddingMirror);
    }

    private async void OnBeginManualSourceAdditionClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not SettingsViewModel viewModel)
            return;

        await _downloadSourceTransition.SwitchStepAsync(
            SourceAdditionChoiceContent,
            ManualSourceAdditionContent,
            forward: true,
            () => viewModel.BeginManualMirrorAdditionCommand.Execute(null),
            () => viewModel.IsAddingMirror);
    }

    private async void OnReturnToSourceAdditionChoiceClicked(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not SettingsViewModel viewModel)
            return;

        var outgoingStep = viewModel.IsManualSourceVisible
            ? ManualSourceAdditionContent
            : AutomaticSourceAdditionContent;
        await _downloadSourceTransition.SwitchStepAsync(
            outgoingStep,
            SourceAdditionChoiceContent,
            forward: false,
            () => viewModel.ReturnToMirrorAdditionChoiceCommand.Execute(null),
            () => viewModel.IsAddingMirror);
    }

    private void OnBackgroundPickerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || Math.Abs(_backgroundPickerWidth - e.NewSize.Width) < 0.5)
            return;

        _backgroundPickerWidth = e.NewSize.Width;
        Dispatcher.UIThread.Post(
            () => UpdateBackgroundPickerLayout(e.NewSize.Width),
            DispatcherPriority.Loaded);
    }

    private void OnAboutContributorsSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        const double containerPadding = 28;
        const double contributorSlotWidth = 64;

        if (DataContext is not SettingsViewModel viewModel || e.NewSize.Width <= containerPadding)
            return;

        var slots = Math.Max(
            1,
            (int)Math.Floor((e.NewSize.Width - containerPadding) / contributorSlotWidth));
        viewModel.UpdateAboutContributorCapacity(slots);
    }

    private void UpdateBackgroundPickerLayout(double availableWidth)
    {
        const double targetSlotWidth = 185;
        const double minimumSlotWidth = 150;
        const double tileMargin = 12;

        var maximumColumns = Math.Max(1, (int)Math.Floor(availableWidth / minimumSlotWidth));
        var columns = Math.Clamp(
            (int)Math.Round(availableWidth / targetSlotWidth, MidpointRounding.AwayFromZero),
            1,
            maximumColumns);
        var slotWidth = Math.Floor((availableWidth - 1) / columns);
        var tileWidth = Math.Max(120, slotWidth - tileMargin);
        var tileHeight = Math.Round(tileWidth * 9 / 16);

        var panel = BackgroundPicker.GetVisualDescendants().OfType<WrapPanel>().FirstOrDefault();
        if (panel is not null)
        {
            panel.ItemWidth = slotWidth;
            panel.ItemHeight = tileHeight + tileMargin;
        }

        foreach (var button in BackgroundPicker.GetVisualDescendants()
                     .OfType<Button>()
                     .Where(button => button.Classes.Contains("backgroundChoice")))
        {
            button.Width = tileWidth;
            button.Height = tileHeight;
        }
    }

    public bool TryCloseCompactContent()
    {
        if (DataContext is SettingsViewModel { IsAddingMirror: true } viewModel)
        {
            viewModel.CancelAddMirrorCommand.Execute(null);
            return true;
        }

        if (_usesCompactLayout is not true || !_compactContentOpen)
            return false;

        _returnToCompactContent = false;
        _compactContentOpen = false;
        SettingsMain.IsHitTestVisible = false;
        SettingsCategoryRail.IsHitTestVisible = true;
        MainTranslation.X = Bounds.Width;
        return true;
    }

    private void SetMainOffsetWithoutTransition(double offset)
    {
        var transitions = MainTranslation.Transitions;
        MainTranslation.Transitions = null;
        MainTranslation.X = offset;
        MainTranslation.Transitions = transitions;
    }

    private async Task PlayDownloadSourceWizardOpenAsync()
    {
        RestoreDownloadSourceWizardStep();
        if (_usesCompactLayout is false)
            _downloadSourceTransition.HideNavigationPane(animate: true);

        await _downloadSourceTransition.OpenAsync(
            () => DataContext is SettingsViewModel { IsAddingMirror: true },
            RestartDownloadSourceWizardAnimation);
    }

    private void RestartDownloadSourceWizardAnimation()
    {
        DownloadSourceWizardAnimation.SeekToProgress(0);
        DownloadSourceWizardAnimation.Start();
    }

    private async Task PlayDownloadSourceWizardCloseAsync()
    {
        await _downloadSourceTransition.CloseAsync(
            () => DataContext is SettingsViewModel { IsAddingMirror: false },
            () =>
            {
                if (_usesCompactLayout is false)
                    _downloadSourceTransition.ShowNavigationPane(animate: true);

                if (DataContext is SettingsViewModel viewModel)
                    viewModel.CompleteMirrorAdditionTransition();

                RestoreDownloadSourceWizardStep();
            });
    }

    private void HideDownloadSourceWizardImmediately()
    {
        _downloadSourceTransition.ShowOverviewImmediately();
        if (_usesCompactLayout is false)
            _downloadSourceTransition.ShowNavigationPane(animate: false);

        if (DataContext is SettingsViewModel viewModel)
            viewModel.CompleteMirrorAdditionTransition();

        RestoreDownloadSourceWizardStep();
    }

    private void RestoreDownloadSourceWizardStep()
    {
        if (DataContext is SettingsViewModel { IsAutomaticSourceVisible: true })
        {
            _downloadSourceTransition.ShowStepImmediately(
                AutomaticSourceAdditionContent,
                SourceAdditionChoiceContent,
                ManualSourceAdditionContent);
            return;
        }

        if (DataContext is SettingsViewModel { IsManualSourceVisible: true })
        {
            _downloadSourceTransition.ShowStepImmediately(
                ManualSourceAdditionContent,
                SourceAdditionChoiceContent,
                AutomaticSourceAdditionContent);
            return;
        }

        _downloadSourceTransition.ShowStepImmediately(
            SourceAdditionChoiceContent,
            AutomaticSourceAdditionContent,
            ManualSourceAdditionContent);
    }
}
