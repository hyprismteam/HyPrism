// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;

namespace HyPrism.Desktop.Features.Settings;

public sealed partial class AuthServerItemViewModel : ObservableObject
{
    private readonly Action<AuthServerItemViewModel> _selected;
    private bool _suppressSelectionChanged;

    public AuthServerItemViewModel(
        string value,
        bool isBuiltIn,
        bool isSelected,
        string checkingLabel,
        Action<AuthServerItemViewModel> selected)
    {
        Value = value;
        IsBuiltIn = isBuiltIn;
        _isSelected = isSelected;
        _availability = checkingLabel;
        _availabilityState = SourceAvailabilityState.Checking;
        _ping = "—";
        _selected = selected;
    }

    public string Value { get; }
    public bool IsBuiltIn { get; }
    public bool CanRemove => !IsBuiltIn;
    public bool IsChecking => _availabilityState == SourceAvailabilityState.Checking;
    public bool IsAvailable => _availabilityState == SourceAvailabilityState.Available;
    public bool IsUnavailable => _availabilityState == SourceAvailabilityState.Unavailable;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isLast;
    [ObservableProperty] private string _availability;
    [ObservableProperty] private string _ping;

    private SourceAvailabilityState _availabilityState;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && !_suppressSelectionChanged)
            _selected(this);
    }

    public void SetChecking(string label)
    {
        _availabilityState = SourceAvailabilityState.Checking;
        Availability = label;
        Ping = "—";
        NotifyAvailabilityChanged();
    }

    public void ApplyProbe(
        bool isAvailable,
        long pingMs,
        string availableLabel,
        string unavailableLabel,
        string pingTemplate)
    {
        _availabilityState = isAvailable
            ? SourceAvailabilityState.Available
            : SourceAvailabilityState.Unavailable;
        Availability = isAvailable ? availableLabel : unavailableLabel;
        Ping = isAvailable && pingMs >= 0
            ? FormatPing(pingTemplate, pingMs)
            : "—";
        NotifyAvailabilityChanged();
    }

    public void RefreshAvailabilityLabel(
        string checkingLabel,
        string availableLabel,
        string unavailableLabel)
    {
        Availability = _availabilityState switch
        {
            SourceAvailabilityState.Checking => checkingLabel,
            SourceAvailabilityState.Available => availableLabel,
            _ => unavailableLabel
        };
    }

    public void SetSelectedWithoutNotification(bool value)
    {
        _suppressSelectionChanged = true;
        try
        {
            IsSelected = value;
        }
        finally
        {
            _suppressSelectionChanged = false;
        }
    }

    private void NotifyAvailabilityChanged()
    {
        OnPropertyChanged(nameof(IsChecking));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(IsUnavailable));
    }

    private static string FormatPing(string template, long pingMs)
        => template.Replace(
            "{{ping}}",
            pingMs.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
}
