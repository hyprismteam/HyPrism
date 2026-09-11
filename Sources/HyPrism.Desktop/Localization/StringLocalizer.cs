// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using System.Globalization;
using System.Resources;

namespace HyPrism.Desktop.Localization;

public sealed class StringLocalizer
{
    private const string DefaultLanguage = "en-US";
    private static readonly CultureInfo DefaultCulture = CultureInfo.GetCultureInfo(DefaultLanguage);
    private static readonly ResourceManager Resources = new(
        "HyPrism.Desktop.Localization.Resources",
        typeof(StringLocalizer).Assembly);
    private static readonly IReadOnlyDictionary<string, string> Languages = LoadAvailableLanguages();

    public StringLocalizer(string? language)
    {
        CurrentLanguage = NormalizeLanguage(language);
        ApplyCulture(CurrentLanguage);
    }

    public event Action<string>? LanguageChanged;

    public string CurrentLanguage { get; private set; }

    public IReadOnlyDictionary<string, string> AvailableLanguages => Languages;

    public string this[string key]
        => Resources.GetString(key, CultureInfo.GetCultureInfo(CurrentLanguage))
           ?? Resources.GetString(key, DefaultCulture)
           ?? key;

    public string Format(string key, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, this[key], args);

    public bool SetLanguage(string? language)
    {
        var normalized = NormalizeLanguage(language);
        if (string.Equals(CurrentLanguage, normalized, StringComparison.OrdinalIgnoreCase))
            return false;

        CurrentLanguage = normalized;
        ApplyCulture(normalized);
        LanguageChanged?.Invoke(normalized);
        return true;
    }

    private static string NormalizeLanguage(string? language)
    {
        if (!string.IsNullOrWhiteSpace(language))
        {
            if (Languages.ContainsKey(language))
                return language;

            var languagePrefix = language.Split('-', 2)[0];
            var matchingLanguage = Languages.Keys.FirstOrDefault(candidate =>
                candidate.StartsWith($"{languagePrefix}-", StringComparison.OrdinalIgnoreCase));
            if (matchingLanguage is not null)
                return matchingLanguage;
        }

        return DefaultLanguage;
    }

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
            CultureInfo.CurrentCulture = DefaultCulture;
            CultureInfo.CurrentUICulture = DefaultCulture;
        }
    }

    private static IReadOnlyDictionary<string, string> LoadAvailableLanguages()
    {
        var cultureList = Resources.GetString("_supportedCultures", CultureInfo.InvariantCulture)
            ?? throw new MissingManifestResourceException("The default locale does not define _supportedCultures.");
        var languages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var language in cultureList.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var culture = CultureInfo.GetCultureInfo(language);
            var nativeName = Resources.GetString("_langName", culture);
            languages[language] = string.IsNullOrWhiteSpace(nativeName) ? culture.NativeName : nativeName;
        }

        if (!languages.ContainsKey(DefaultLanguage))
            throw new MissingManifestResourceException($"The default locale '{DefaultLanguage}' is not registered.");

        return new ReadOnlyDictionary<string, string>(languages);
    }
}
