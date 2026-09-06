// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Encodings.Web;
using System.Text.Json;

namespace HyPrism.Core.Infrastructure;

internal static class JsonDefaults
{
    public static JsonSerializerOptions Indented { get; } = new()
    {
        WriteIndented = true
    };

    public static JsonSerializerOptions CaseInsensitive { get; } = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static JsonSerializerOptions CaseInsensitiveIndented { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static JsonSerializerOptions CamelCase { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static JsonSerializerOptions CamelCaseInsensitive { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static JsonSerializerOptions IndentedUnsafeRelaxed { get; } = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
