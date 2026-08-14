// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Desktop.Features.Instances;

public sealed record InstanceModItemViewModel(
    string Id,
    string Name,
    string Version,
    string Author,
    bool IsEnabled)
{
    public string Initial => string.IsNullOrWhiteSpace(Name)
        ? "M"
        : Name[..1].ToUpperInvariant();
}
