using HyPrism.Models;
using HyPrism.Services.Core.App;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Tests.Core.App;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigService _config;
    private readonly LocalizationService _localization;
    private readonly SettingsService _svc;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HyPrismSettingsTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _config = new ConfigService(_tempDir);
        _localization = new LocalizationService();
        _svc = new SettingsService(_config, _localization);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }


    [Fact]
    public void GetLanguage_ReturnsConfigLanguage()
    {
        _config.Configuration.Language = "en-US";
        Assert.Equal("en-US", _svc.GetLanguage());
    }

    [Fact]
    public void SetLanguage_ValidCode_ReturnsTrueAndPersists()
    {
        var result = _svc.SetLanguage("en-US");
        Assert.True(result);
        Assert.Equal("en-US", _svc.GetLanguage());
    }

    [Fact]
    public void SetLanguage_InvalidCode_ReturnsFalse()
    {
        var result = _svc.SetLanguage("xx-XX");
        Assert.False(result);
    }


    [Fact]
    public void SetMusicEnabled_Persists()
    {
        _svc.SetMusicEnabled(false);
        Assert.False(_svc.GetMusicEnabled());

        _svc.SetMusicEnabled(true);
        Assert.True(_svc.GetMusicEnabled());
    }


    [Fact]
    public void SetOnlineMode_Persists()
    {
        _svc.SetOnlineMode(false);
        Assert.False(_svc.GetOnlineMode());
    }

    [Fact]
    public void SetAuthDomain_PersistsValue()
    {
        _svc.SetAuthDomain("auth.example.com");
        Assert.Equal("auth.example.com", _svc.GetAuthDomain());
    }


    [Fact]
    public void SetCloseAfterLaunch_Persists()
    {
        _svc.SetCloseAfterLaunch(true);
        Assert.True(_svc.GetCloseAfterLaunch());
    }


    [Fact]
    public void SetHasCompletedOnboarding_Persists()
    {
        _svc.SetHasCompletedOnboarding(true);
        Assert.True(_svc.GetHasCompletedOnboarding());
    }

    [Fact]
    public void ResetOnboarding_SetsToFalse()
    {
        _svc.SetHasCompletedOnboarding(true);
        _svc.ResetOnboarding();
        Assert.False(_svc.GetHasCompletedOnboarding());
    }


    [Fact]
    public void SetAccentColor_PersistsAndFiresEvent()
    {
        string? received = null;
        _svc.OnAccentColorChanged += c => received = c;

        _svc.SetAccentColor("#FF0000");

        Assert.Equal("#FF0000", _svc.GetAccentColor());
        Assert.Equal("#FF0000", received);
    }


    [Fact]
    public void DismissAnnouncement_MarksAsDismissed()
    {
        _svc.DismissAnnouncement("ann-001");
        Assert.True(_svc.IsAnnouncementDismissed("ann-001"));
    }

    [Fact]
    public void IsAnnouncementDismissed_UnknownId_ReturnsFalse()
    {
        Assert.False(_svc.IsAnnouncementDismissed("unknown-id"));
    }


    [Fact]
    public void SetGpuPreference_PersistsValue()
    {
        _svc.SetGpuPreference("integrated");
        Assert.Equal("integrated", _svc.GetGpuPreference());
    }


    [Fact]
    public void SetUseDualAuth_Persists()
    {
        _svc.SetUseDualAuth(false);
        Assert.False(_svc.GetUseDualAuth());

        _svc.SetUseDualAuth(true);
        Assert.True(_svc.GetUseDualAuth());
    }


    [Fact]
    public void SetUseCustomJava_Persists()
    {
        _svc.SetUseCustomJava(true);
        Assert.True(_svc.GetUseCustomJava());
    }

    [Fact]
    public void SetCustomJavaPath_Persists()
    {
        _svc.SetCustomJavaPath("/usr/lib/jvm/java-21/bin/java");
        Assert.Equal("/usr/lib/jvm/java-21/bin/java", _svc.GetCustomJavaPath());
    }
}
