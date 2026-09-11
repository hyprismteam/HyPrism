// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;
using HyPrism.Core.Game.Mods;
using HyPrism.Core.Models;

namespace HyPrism.Desktop.Features.Instances;

public sealed partial class ModCatalogFileItemViewModel(
    ModFileInfo file,
    string releaseLabel,
    bool isInstalled,
    ModCompatibilityStatus compatibility,
    string compatibilityLabel) : ObservableObject
{
    public string Id => file.Id;
    public string DisplayName => string.IsNullOrWhiteSpace(file.DisplayName)
        ? file.FileName
        : file.DisplayName;
    public string GameVersionsLabel => file.GameVersions.Count > 0
        ? string.Join(", ", file.GameVersions)
        : CompatibilityLabel;
    public string ReleaseLabel { get; } = releaseLabel;
    public int ReleaseType => file.ReleaseType;
    public bool IsRelease => ReleaseType == 1;
    public bool IsBeta => ReleaseType == 2;
    public bool IsAlpha => ReleaseType == 3;
    public ModCompatibilityStatus Compatibility { get; } = compatibility;
    public string CompatibilityLabel { get; } = compatibilityLabel;
    public bool IsCompatible => Compatibility is ModCompatibilityStatus.Compatible;
    public bool IsIncompatible => Compatibility is ModCompatibilityStatus.Incompatible;
    public bool IsCompatibilityUnknown => Compatibility is ModCompatibilityStatus.Unknown;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    [NotifyPropertyChangedFor(nameof(CanSelect))]
    private bool _isInstalled = isInstalled;
    [ObservableProperty]
    private bool _isSelected;
    public bool HasGameVersions => file.GameVersions.Count > 0;
    public bool CanInstall => !IsInstalled && !IsIncompatible;
    public bool CanSelect => IsInstalled || !IsIncompatible;
}
