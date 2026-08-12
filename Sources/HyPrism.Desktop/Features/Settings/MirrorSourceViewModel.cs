// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;
using HyPrism.Core.Models;

namespace HyPrism.Desktop.Features.Settings;

public sealed partial class MirrorSourceViewModel : ObservableObject
{
    private readonly Action<MirrorSourceViewModel> _enabledChanged;
    private bool _suppressEnabledChanged;

    public MirrorSourceViewModel(
        MirrorMeta definition,
        string sourceType,
        bool isLast,
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

    [ObservableProperty]
    private bool _isEnabled;

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
