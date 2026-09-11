// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HyPrism.Desktop.Features.Instances;

public sealed partial class InstanceModItemViewModel(
    string id,
    string name,
    string version,
    string author,
    bool isEnabled,
    string iconUrl = "",
    string curseForgeId = "",
    int releaseType = 1) : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Version { get; } = version;
    public string Author { get; } = author;
    public string IconUrl { get; } = iconUrl;
    public string CurseForgeId { get; } = curseForgeId;
    public int ReleaseType { get; } = releaseType;

    public string Initial => string.IsNullOrWhiteSpace(Name)
        ? "M"
        : Name[..1].ToUpperInvariant();

    public bool HasExternalPage =>
        !string.IsNullOrWhiteSpace(CurseForgeId) || !string.IsNullOrWhiteSpace(Name);

    public string CurseForgeUrl => !string.IsNullOrWhiteSpace(CurseForgeId)
        ? $"https://www.curseforge.com/hytale/mods/{CurseForgeId}"
        : $"https://www.curseforge.com/hytale/mods/search?search={Uri.EscapeDataString(Name)}";

    public string ReleaseBadge => ReleaseType switch
    {
        2 => "beta",
        3 => "alpha",
        _ => string.Empty
    };

    public bool ShowsReleaseBadge => ReleaseType is 2 or 3;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInteract))]
    private bool _isEnabled = isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInteract))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsUpdateBadge))]
    [NotifyPropertyChangedFor(nameof(UpdateBadgeText))]
    private string _updateVersion = string.Empty;

    [ObservableProperty]
    private Bitmap? _icon;

    public bool CanInteract => !IsBusy;

    public bool ShowsUpdateBadge => !string.IsNullOrWhiteSpace(UpdateVersion);

    public string UpdateBadgeText => UpdateVersion;

    public bool ShowsIcon => Icon is not null;

    partial void OnIconChanged(Bitmap? value)
        => OnPropertyChanged(nameof(ShowsIcon));
}
