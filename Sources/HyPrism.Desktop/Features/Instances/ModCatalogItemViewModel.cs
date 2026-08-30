// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using HyPrism.Core.Game.Mods;

namespace HyPrism.Desktop.Features.Instances;

public sealed partial class ModCatalogItemViewModel(
    string id,
    string name,
    string author,
    string summary,
    string latestFileId,
    string slug = "",
    string iconUrl = "",
    int downloadCount = 0,
    int releaseType = 1,
    IReadOnlyList<string>? screenshotUrls = null,
    string installedFileId = "",
    string recommendedFileId = "",
    ModCompatibilityStatus compatibility = ModCompatibilityStatus.Unknown,
    string compatibilityLabel = "") : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Author { get; } = author;
    public string Summary { get; } = summary;
    public string LatestFileId { get; } = latestFileId;
    public string Slug { get; } = slug;
    public string IconUrl { get; } = iconUrl;
    public int DownloadCount { get; } = downloadCount;
    public IReadOnlyList<string> ScreenshotUrls { get; } = screenshotUrls ?? [];
    public string RecommendedFileId { get; } = recommendedFileId;
    public ModCompatibilityStatus Compatibility { get; } = compatibility;
    public string CompatibilityLabel { get; } = compatibilityLabel;
    public bool IsCompatible => Compatibility is ModCompatibilityStatus.Compatible;
    public bool IsIncompatible => Compatibility is ModCompatibilityStatus.Incompatible;
    public bool IsCompatibilityUnknown => Compatibility is ModCompatibilityStatus.Unknown;

    [ObservableProperty]
    private string _installedFileId = installedFileId;

    public string Initial => string.IsNullOrWhiteSpace(Name)
        ? "M"
        : Name[..1].ToUpperInvariant();

    public string CurseForgeUrl => string.IsNullOrWhiteSpace(Slug)
        ? $"https://www.curseforge.com/hytale/mods/{Id}"
        : $"https://www.curseforge.com/hytale/mods/{Slug}";

    public string DownloadCountLabel => FormatDownloads(DownloadCount);

    public int ReleaseType { get; } = releaseType;

    public string ReleaseBadge => ReleaseType switch
    {
        2 => "beta",
        3 => "alpha",
        _ => string.Empty
    };

    public bool ShowsReleaseBadge => ReleaseType is 2 or 3;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    [NotifyPropertyChangedFor(nameof(CanSelect))]
    private bool _isInstalling;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    [NotifyPropertyChangedFor(nameof(CanSelect))]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private Bitmap? _icon;

    public bool CanInstall => !IsInstalling && !IsInstalled;
    public bool CanSelect => CanInstall && !IsIncompatible && !string.IsNullOrWhiteSpace(RecommendedFileId);

    public bool ShowsIcon => Icon is not null;

    partial void OnIconChanged(Bitmap? value)
        => OnPropertyChanged(nameof(ShowsIcon));

    private static string FormatDownloads(int downloads)
        => downloads switch
        {
            >= 1_000_000 => $"{downloads / 1_000_000.0:0.#}M",
            >= 1_000 => $"{downloads / 1_000.0:0.#}k",
            _ => downloads.ToString(System.Globalization.CultureInfo.CurrentCulture)
        };
}
