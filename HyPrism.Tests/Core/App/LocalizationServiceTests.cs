using HyPrism.Services.Core.App;

namespace HyPrism.Tests.Core.App;

public class LocalizationServiceTests
{
    private readonly LocalizationService _svc = new();


    [Fact]
    public void GetAvailableLanguages_ReturnsNonEmptyDictionary()
    {
        var langs = LocalizationService.GetAvailableLanguages();
        Assert.NotEmpty(langs);
    }

    [Fact]
    public void GetAvailableLanguages_ContainsEnUS()
    {
        var langs = LocalizationService.GetAvailableLanguages();
        Assert.True(langs.ContainsKey("en-US"), "Available languages must include 'en-US'");
    }


    [Fact]
    public void CurrentLanguage_DefaultValue_IsSet()
    {
        Assert.False(string.IsNullOrEmpty(_svc.CurrentLanguage));
    }

    [Fact]
    public void CurrentLanguage_Set_UpdatesValue()
    {
        _svc.CurrentLanguage = "en-US";
        Assert.Equal("en-US", _svc.CurrentLanguage);
    }

    [Fact]
    public void CurrentLanguage_Changed_FiresLanguageChangedEvent()
    {
        // Default is "en-US"; event only fires when the value actually changes.
        // Switch to a different language first, then subscribe and change back.
        _svc.CurrentLanguage = "ru-RU";

        string? received = null;
        _svc.LanguageChanged += lang => received = lang;

        _svc.CurrentLanguage = "en-US";

        Assert.Equal("en-US", received);
    }


    [Fact]
    public void Indexer_ReturnsKey()
    {
        // Backend doesn't translate — it echoes the key back
        Assert.Equal("some.key", _svc["some.key"]);
    }

    [Fact]
    public void Translate_ReturnsKey()
    {
        Assert.Equal("dashboard.play", _svc.Translate("dashboard.play"));
    }
}
