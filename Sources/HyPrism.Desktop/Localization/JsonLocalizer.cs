// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace HyPrism.Desktop.Localization;

public sealed class JsonLocalizer
{
    private readonly Dictionary<string, string> _fallback;
    private Dictionary<string, string> _strings;

    public JsonLocalizer(string? language)
    {
        _fallback = Load("en-US");
        CurrentLanguage = NormalizeLanguage(language);
        _strings = Load(CurrentLanguage);
        ApplyCulture(CurrentLanguage);
    }

    public string CurrentLanguage { get; private set; }

    public string this[string key]
        => _strings.TryGetValue(key, out var value)
            ? value
            : _fallback.TryGetValue(key, out value)
                ? value
                : key;

    public string Format(string key, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, this[key], args);

    public bool SetLanguage(string? language)
    {
        var normalized = NormalizeLanguage(language);
        if (string.Equals(CurrentLanguage, normalized, StringComparison.OrdinalIgnoreCase))
            return false;

        CurrentLanguage = normalized;
        _strings = Load(normalized);
        ApplyCulture(normalized);
        return true;
    }

    private static string NormalizeLanguage(string? language)
        => string.IsNullOrWhiteSpace(language) ? "en-US" : language;

    private static void ApplyCulture(string language)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(language);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        }
    }

    private static Dictionary<string, string> Load(string language)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"HyPrism.Desktop.Assets.Locales.{language}.json";
        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream is null)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        using var document = JsonDocument.Parse(stream);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        Flatten(document.RootElement, null, values);
        return values;
    }

    private static void Flatten(
        JsonElement element,
        string? prefix,
        IDictionary<string, string> target)
    {
        foreach (var property in element.EnumerateObject())
        {
            var key = string.IsNullOrEmpty(prefix)
                ? property.Name
                : $"{prefix}.{property.Name}";

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                Flatten(property.Value, key, target);
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.String)
                target[key] = property.Value.GetString() ?? key;
        }
    }
}
