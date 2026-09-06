// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Desktop.Features.Dashboard;

public sealed record InstanceItemViewModel(
    string Id,
    string Name,
    string Version,
    string Branch,
    bool IsInstalled,
    bool IsManaged)
{
    public string Initial => string.IsNullOrWhiteSpace(Name)
        ? "H"
        : Name[..1].ToUpperInvariant();
}
