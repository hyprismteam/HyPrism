// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HyPrism.Desktop.Controls;

namespace HyPrism.Desktop.Features.Settings;

public sealed partial class SettingsView : UserControl
{
    private double _backgroundPickerWidth;
    private readonly WizardHost _downloadSourceWizard;
    private readonly AdaptiveMasterDetailHost _layoutHost;
    private INotifyPropertyChanged? _viewModel;
    private bool _isDownloadSourceWizardVisible;

    public SettingsView()
    {
        InitializeComponent();
        _downloadSourceWizard = new WizardHost(
            SettingsOverview,
            DownloadSourceWizardScreen,
            SettingsCategoryRail,
            DownloadSourceWizardReveal.Anchor,
            DownloadSourceWizardReveal.MotionTarget,
            DownloadSourceWizardReveal.Animation,
            SourceAdditionChoiceContent,
            AutomaticSourceAdditionContent,
            ManualSourceAdditionContent);
        _layoutHost = new AdaptiveMasterDetailHost(
            SettingsLayout,
            SettingsCategoryRail,
            SettingsMain,
            CompactSettingsToolbar,
            SettingsContentHost,
            compact => SettingsCategoryRail.Classes.Set("compact", compact));
        DataContextChanged += OnDataContextChanged;
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

        ApplyJavaArgumentModalBackground();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SettingsViewModel.IsAddingJavaArgument))
        {
            ApplyJavaArgumentModalBackground();
            if (DataContext is SettingsViewModel { IsAddingJavaArgument: true })
                Dispatcher.UIThread.Post(() => NewJavaArgumentTextBox.Focus(), DispatcherPriority.Loaded);
        }

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
        _layoutHost.Update(e.NewSize.Width, hasMaster: true);
        var viewModel = DataContext as SettingsViewModel;
        if (viewModel is not null)
            viewModel.IsCompactLayout = _layoutHost.IsCompact;

        if (_layoutHost.IsCompact)
        {
            _downloadSourceWizard.ResetNavigationPane();
            return;
        }

        if (viewModel?.IsAddingMirror == true)
            _downloadSourceWizard.HideNavigationPane(animate: false);
        else
            _downloadSourceWizard.ShowNavigationPane(animate: false);
    }

    private void OnSettingsCategoryClicked(object? sender, RoutedEventArgs e)
    {
        _layoutHost.RememberDetail();
        Dispatcher.UIThread.Post(SettingsContent.ScrollToHome, DispatcherPriority.Background);
        if (!_layoutHost.IsCompact)
            return;

        _layoutHost.OpenDetail();
    }

    private void OnCompactSettingsBackClicked(object? sender, RoutedEventArgs e)
        => TryCloseCompactContent();

    private static void OnMirrorMenuPointerPressed(object? sender, PointerPressedEventArgs args)
        => args.Handled = true;

    private void OnInstanceFolderChangeActionPointerExited(object? sender, PointerEventArgs args)
    {
        if (DataContext is SettingsViewModel viewModel)
            viewModel.ArmInstanceFolderChangeCancellation();
    }

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

        await _downloadSourceWizard.SwitchStepAsync(
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

        await _downloadSourceWizard.SwitchStepAsync(
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
        await _downloadSourceWizard.SwitchStepAsync(
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
        if (DataContext is SettingsViewModel { IsAddingJavaArgument: true } javaViewModel)
        {
            javaViewModel.CancelAddJavaArgumentCommand.Execute(null);
            return true;
        }

        if (DataContext is SettingsViewModel { IsAddingMirror: true } viewModel)
        {
            viewModel.CancelAddMirrorCommand.Execute(null);
            return true;
        }

        return _layoutHost.TryCloseDetail();
    }

    private void ApplyJavaArgumentModalBackground()
    {
        var isOpen = DataContext is SettingsViewModel { IsAddingJavaArgument: true };
        SettingsLayout.IsHitTestVisible = !isOpen;
        ((BlurEffect)SettingsLayout.Effect!).Radius = isOpen ? 6 : 0;
    }

    private async Task PlayDownloadSourceWizardOpenAsync()
    {
        if (!_layoutHost.IsCompact)
            _downloadSourceWizard.HideNavigationPane(animate: true);

        await _downloadSourceWizard.OpenAsync(
            () => DataContext is SettingsViewModel { IsAddingMirror: true });
    }

    private async Task PlayDownloadSourceWizardCloseAsync()
    {
        await _downloadSourceWizard.CloseAsync(
            () => DataContext is SettingsViewModel { IsAddingMirror: false },
            () =>
            {
                if (!_layoutHost.IsCompact)
                    _downloadSourceWizard.ShowNavigationPane(animate: true);

                if (DataContext is SettingsViewModel viewModel)
                    viewModel.CompleteMirrorAdditionTransition();
            });
    }

    private void HideDownloadSourceWizardImmediately()
    {
        _downloadSourceWizard.ShowOverviewImmediately(() =>
        {
            if (DataContext is SettingsViewModel viewModel)
                viewModel.CompleteMirrorAdditionTransition();
        });
        if (!_layoutHost.IsCompact)
            _downloadSourceWizard.ShowNavigationPane(animate: false);
    }
}
