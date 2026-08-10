// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Services.Core.Ipc.Responses;

/// <summary>Identifies the current OS platform.</summary>
public record PlatformInfo(string Os, bool IsLinux, bool IsWindows, bool IsMacOS);

/// <summary>Named language available in the UI.</summary>
public record LanguageInfo(string Code, string Name);

/// <summary>Localisation set/switch result.</summary>
public record SetLanguageResult(bool Success, string Language);
