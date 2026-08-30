// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;
using HyPrism.Core.Models;

namespace HyPrism.Desktop.Features.Instances;

public sealed partial class ModCatalogFileItemViewModel(
    ModFileInfo file,
    string releaseLabel,
    bool isInstalled) : ObservableObject
{
    public string Id => file.Id;
    public string DisplayName => string.IsNullOrWhiteSpace(file.DisplayName)
        ? file.FileName
        : file.DisplayName;
    public string GameVersionsLabel => string.Join(", ", file.GameVersions);
    public string ReleaseLabel { get; } = releaseLabel;
    [ObservableProperty]
    private bool _isInstalled = isInstalled;
    public bool HasGameVersions => file.GameVersions.Count > 0;
}
