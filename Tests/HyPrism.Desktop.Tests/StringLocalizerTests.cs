// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using HyPrism.Desktop.Localization;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class StringLocalizerTests
{
    [Fact]
    public void AvailableLanguages_AreDiscoveredFromEmbeddedLocales()
    {
        var localizer = new StringLocalizer("en-US");

        Assert.Equal(13, localizer.AvailableLanguages.Count);
        Assert.Equal("English", localizer.AvailableLanguages["en-US"]);
        Assert.Equal("Italiano", localizer.AvailableLanguages["it-IT"]);
        Assert.Equal("Русский", localizer.AvailableLanguages["ru-RU"]);
    }

    [Fact]
    public void Constructor_LegacyShortCode_SelectsMatchingEmbeddedLocale()
    {
        using var culture = new CultureScope();

        var localizer = new StringLocalizer("ru");

        Assert.Equal("ru-RU", localizer.CurrentLanguage);
        Assert.Equal("Новости", localizer["dock.news"]);
    }

    [Fact]
    public void Constructor_UnknownCode_FallsBackToEnglish()
    {
        using var culture = new CultureScope();

        var localizer = new StringLocalizer("xx-XX");

        Assert.Equal("en-US", localizer.CurrentLanguage);
        Assert.Equal("News", localizer["dock.news"]);
    }

    [Fact]
    public void LocalizedResource_ReturnsTranslatedValue()
    {
        using var culture = new CultureScope();

        var localizer = new StringLocalizer("de-DE");

        Assert.Equal("Offline-Konto", localizer["desktopSettings.accountOffline"]);
    }

    [Fact]
    public void SupportedLocaleResources_MatchDefaultKeysAndPlaceholders()
    {
        using var culture = new CultureScope();
        var localizer = new StringLocalizer("en-US");
        var resourceManager = new ResourceManager(
            "HyPrism.Desktop.Localization.Resources",
            typeof(StringLocalizer).Assembly);
        var defaultValues = ReadResourceSet(resourceManager, CultureInfo.InvariantCulture);
        var expectedKeys = defaultValues.Keys
            .Where(key => key != "_supportedCultures")
            .ToHashSet(StringComparer.Ordinal);

        foreach (var language in localizer.AvailableLanguages.Keys.Where(language => language != "en-US"))
        {
            var localizedValues = ReadResourceSet(resourceManager, CultureInfo.GetCultureInfo(language));
            var localizedKeys = localizedValues.Keys.ToHashSet(StringComparer.Ordinal);

            Assert.True(
                expectedKeys.SetEquals(localizedKeys),
                $"{language} keys differ. Missing: {string.Join(", ", expectedKeys.Except(localizedKeys))}. " +
                $"Extra: {string.Join(", ", localizedKeys.Except(expectedKeys))}");

            foreach (var key in expectedKeys)
            {
                Assert.Equal(
                    ExtractPlaceholders(defaultValues[key]),
                    ExtractPlaceholders(localizedValues[key]));
            }
        }
    }

    [Fact]
    public void SetLanguage_AppliesTranslationsCultureAndNotification()
    {
        using var culture = new CultureScope();
        var localizer = new StringLocalizer("en-US");
        string? changedLanguage = null;
        localizer.LanguageChanged += language => changedLanguage = language;

        var changed = localizer.SetLanguage("ru-RU");

        Assert.True(changed);
        Assert.Equal("ru-RU", changedLanguage);
        Assert.Equal("ru-RU", CultureInfo.CurrentUICulture.Name);
        Assert.Equal("Настройки", localizer["dock.settings"]);
    }

    [Theory]
    [InlineData("en-US", "Profile type")]
    [InlineData("ru-RU", "Тип профиля")]
    public void ProfileStatistics_UseProfileTypeLabel(string language, string expected)
    {
        using var culture = new CultureScope();
        var localizer = new StringLocalizer(language);

        Assert.Equal(expected, localizer["profiles.editor"]);
        Assert.NotEqual("startup.loading.title", localizer["startup.loading.title"]);
        Assert.NotEqual("startup.loading.content", localizer["startup.loading.content"]);
        Assert.NotEqual("startup.loading.ready", localizer["startup.loading.ready"]);
    }

    [Theory]
    [InlineData("be-BY", "Увайсці")]
    [InlineData("de-DE", "Anmelden")]
    [InlineData("en-US", "Sign in")]
    [InlineData("es-ES", "Iniciar sesión")]
    [InlineData("fr-FR", "Se connecter")]
    [InlineData("it-IT", "Accedi")]
    [InlineData("ja-JP", "サインイン")]
    [InlineData("ko-KR", "로그인")]
    [InlineData("pt-BR", "Entrar")]
    [InlineData("ru-RU", "Войти")]
    [InlineData("tr-TR", "Giriş yap")]
    [InlineData("uk-UA", "Увійти")]
    [InlineData("zh-CN", "登录")]
    public void ProfileWizardSignInActionUsesConciseLocalizedLabel(
        string language,
        string expected)
    {
        using var culture = new CultureScope();
        var localizer = new StringLocalizer(language);

        Assert.Equal(expected, localizer["profiles.wizard.loginHytale"]);
    }

    [Theory]
    [InlineData("en-US", "Illustrations provided by Icons8", "Animated materials", "Animated icons provided by Lordicon")]
    [InlineData("ru-RU", "Иллюстрации предоставлены Icons8", "Анимированные материалы", "Анимированные иконки предоставлены Lordicon")]
    public void AboutPage_ProvidesVisualAttribution(
        string language,
        string expectedIllustrationsHint,
        string expectedLabel,
        string expectedHint)
    {
        using var culture = new CultureScope();
        var localizer = new StringLocalizer(language);

        Assert.Equal(
            expectedIllustrationsHint,
            localizer["settings.aboutSettings.creditsHint"]);
        Assert.Equal(expectedLabel, localizer["settings.aboutSettings.lordicon"]);
        Assert.Equal(expectedHint, localizer["settings.aboutSettings.lordiconHint"]);
    }

    [Theory]
    [InlineData("en-US", "Built with")]
    [InlineData("ru-RU", "Создано с помощью")]
    public void AboutPage_LocalizesBuiltWithLabel(string language, string expected)
    {
        using var culture = new CultureScope();
        var localizer = new StringLocalizer(language);

        Assert.Equal(expected, localizer["settings.aboutSettings.builtWith"]);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }

    private static Dictionary<string, string> ReadResourceSet(
        ResourceManager resourceManager,
        CultureInfo culture)
    {
        var resourceSet = resourceManager.GetResourceSet(culture, true, false)
            ?? throw new MissingManifestResourceException($"Resource set for {culture.Name} was not found.");

        return resourceSet.Cast<DictionaryEntry>().ToDictionary(
            entry => (string)entry.Key,
            entry => (string?)entry.Value ?? string.Empty,
            StringComparer.Ordinal);
    }

    private static string[] ExtractPlaceholders(string value)
        => Regex.Matches(value, @"\{\{[^{}]+\}\}|(?<!\{)\{\d+(?::[^}]*)?\}(?!\})")
            .Select(match => match.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
