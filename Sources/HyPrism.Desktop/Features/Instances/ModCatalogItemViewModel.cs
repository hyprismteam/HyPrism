// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

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
    string installedFileId = "") : ObservableObject
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
    private bool _isInstalling;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private bool _isInstalled;

    [ObservableProperty]
    private Bitmap? _icon;

    public bool CanInstall => !IsInstalling && !IsInstalled;

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
