// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
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
    public void MissingTranslatedResource_UsesEnglishResXFallback()
    {
        using var culture = new CultureScope();

        var localizer = new StringLocalizer("de-DE");

        Assert.Equal("Offline Account", localizer["desktopSettings.accountOffline"]);
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
}
