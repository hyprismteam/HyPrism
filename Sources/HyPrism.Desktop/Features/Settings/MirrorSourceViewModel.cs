// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;
using HyPrism.Core.Game.Sources;
using HyPrism.Core.Models;

namespace HyPrism.Desktop.Features.Settings;

public sealed partial class MirrorSourceViewModel : ObservableObject
{
    private SourceAvailabilityState _availabilityState;
    private readonly Action<MirrorSourceViewModel> _enabledChanged;
    private bool _suppressEnabledChanged;

    public MirrorSourceViewModel(
        MirrorMeta definition,
        string sourceType,
        bool isLast,
        string checkingLabel,
        string disabledLabel,
        Action<MirrorSourceViewModel> enabledChanged)
    {
        Definition = definition;
        Name = definition.Name;
        Description = string.IsNullOrWhiteSpace(definition.Description)
            ? GetEndpoint(definition)
            : definition.Description;
        Endpoint = GetEndpoint(definition);
        SourceType = sourceType;
        IsLast = isLast;
        _availability = definition.Enabled ? checkingLabel : disabledLabel;
        _availabilityState = definition.Enabled
            ? SourceAvailabilityState.Checking
            : SourceAvailabilityState.Disabled;
        _ping = "—";
        _isEnabled = definition.Enabled;
        _enabledChanged = enabledChanged;
    }

    public MirrorMeta Definition { get; }
    public string Id => Definition.Id;
    public string Name { get; }
    public string Description { get; }
    public string Endpoint { get; }
    public string SourceType { get; private set; }
    public bool IsLast { get; }
    public bool IsChecking => _availabilityState == SourceAvailabilityState.Checking;
    public bool IsAvailable => _availabilityState == SourceAvailabilityState.Available;
    public bool IsUnavailable => _availabilityState is SourceAvailabilityState.Disabled or SourceAvailabilityState.Unavailable;

    [ObservableProperty] private string _availability;
    [ObservableProperty] private string _ping;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isMenuOpen;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressEnabledChanged)
            return;

        Definition.Enabled = value;
        _enabledChanged(this);
    }

    public void UpdateSourceType(string sourceType)
    {
        SourceType = sourceType;
        OnPropertyChanged(nameof(SourceType));
    }

    public void SetChecking(string label)
    {
        _availabilityState = SourceAvailabilityState.Checking;
        Availability = label;
        Ping = "—";
        NotifyAvailabilityChanged();
    }

    public void SetDisabled(string label)
    {
        _availabilityState = SourceAvailabilityState.Disabled;
        Availability = label;
        Ping = "—";
        NotifyAvailabilityChanged();
    }

    public void ApplyProbe(MirrorSpeedTestResult result, string availableLabel, string unavailableLabel)
    {
        _availabilityState = result.IsAvailable
            ? SourceAvailabilityState.Available
            : SourceAvailabilityState.Unavailable;
        Availability = result.IsAvailable ? availableLabel : unavailableLabel;
        Ping = result.IsAvailable && result.PingMs >= 0 ? $"{result.PingMs} ms" : "—";
        NotifyAvailabilityChanged();
    }

    public void RefreshAvailabilityLabel(
        string checkingLabel,
        string disabledLabel,
        string availableLabel,
        string unavailableLabel)
    {
        Availability = _availabilityState switch
        {
            SourceAvailabilityState.Checking => checkingLabel,
            SourceAvailabilityState.Disabled => disabledLabel,
            SourceAvailabilityState.Available => availableLabel,
            _ => unavailableLabel
        };
    }

    private void NotifyAvailabilityChanged()
    {
        OnPropertyChanged(nameof(IsChecking));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(IsUnavailable));
    }

    public void SetEnabledWithoutNotification(bool value)
    {
        _suppressEnabledChanged = true;
        try
        {
            IsEnabled = value;
        }
        finally
        {
            _suppressEnabledChanged = false;
        }
    }

    private static string GetEndpoint(MirrorMeta definition)
    {
        var rawEndpoint = definition.Pattern?.BaseUrl ?? definition.JsonIndex?.ApiUrl ?? string.Empty;
        return Uri.TryCreate(rawEndpoint, UriKind.Absolute, out var endpoint)
            ? endpoint.GetLeftPart(UriPartial.Path).TrimEnd('/')
            : rawEndpoint;
    }
}

internal enum SourceAvailabilityState
{
    Checking,
    Disabled,
    Available,
    Unavailable
}
