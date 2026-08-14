// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;

namespace HyPrism.Desktop.Features.Instances;

public sealed partial class ModCatalogItemViewModel(
    string id,
    string name,
    string author,
    string summary,
    string latestFileId) : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Author { get; } = author;
    public string Summary { get; } = summary;
    public string LatestFileId { get; } = latestFileId;

    public string Initial => string.IsNullOrWhiteSpace(Name)
        ? "M"
        : Name[..1].ToUpperInvariant();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private bool _isInstalling;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private bool _isInstalled;

    public bool CanInstall => !IsInstalling && !IsInstalled;
}
