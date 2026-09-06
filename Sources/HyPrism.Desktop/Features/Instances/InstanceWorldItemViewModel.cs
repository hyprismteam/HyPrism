// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Desktop.Features.Instances;

public sealed record InstanceWorldItemViewModel(
    string Name,
    string LastModified,
    string Size)
{
    public string Initial => string.IsNullOrWhiteSpace(Name)
        ? "W"
        : Name[..1].ToUpperInvariant();
}
