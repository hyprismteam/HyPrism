using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.Core.App;

/// <summary>
/// Manages all launcher settings (preferences, UI config, behavior options).
/// Provides centralized access to configuration properties with automatic persistence.
/// </summary>
/// <remarks>
/// This service acts as a facade over <see cref="ConfigService"/> and <see cref="LocalizationService"/>,
/// exposing settings through a clean interface while handling persistence internally.
/// </remarks>
public class SettingsService : ISettingsService
{
    #region Fields and Constructor

    private readonly IConfigService _configService;
    private readonly ILocalizationService _localizationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsService"/> class.
    /// Applies the saved language setting to the localization service on startup.
    /// </summary>
    /// <param name="configService">The configuration service for persisting settings.</param>
    /// <param name="localizationService">The localization service for language management.</param>
    public SettingsService(IConfigService configService, ILocalizationService localizationService)
    {
        _configService = configService;
        _localizationService = localizationService;
        
        // Apply initial language override from config
        var savedLang = _configService.Configuration.Language;
        Logger.Info("Settings", $"SettingsService init - Config language: '{savedLang}', LocalizationService current: '{_localizationService.CurrentLanguage}'");
        if (!string.IsNullOrEmpty(savedLang))
        {
            _localizationService.CurrentLanguage = savedLang;
            Logger.Info("Settings", $"Applied language to LocalizationService: '{_localizationService.CurrentLanguage}'");
        }
    }
    
    /// <inheritdoc/>
    public event Action<string>? OnAccentColorChanged;
    
    /// <inheritdoc/>
    public event Action<string?>? OnBackgroundChanged;

    #endregion

    #region Localization Settings

    /// <inheritdoc/>
    public string GetLanguage() => _configService.Configuration.Language;

    /// <inheritdoc/>
    public bool SetLanguage(string languageCode)
    {
        var availableLanguages = LocalizationService.GetAvailableLanguages();
        if (availableLanguages.ContainsKey(languageCode))
        {
            _configService.Configuration.Language = languageCode;
            // Update the localization service which drives the UI
            _localizationService.CurrentLanguage = languageCode;
            _configService.SaveConfig();
            Logger.Info("Config", $"Language changed to: {languageCode}");
            return true;
        }
        return false;
    }

    #endregion

    #region Music Settings

    /// <inheritdoc/>
    public bool GetMusicEnabled() => _configService.Configuration.MusicEnabled;
    
    /// <inheritdoc/>
    public bool SetMusicEnabled(bool enabled)
    {
        _configService.Configuration.MusicEnabled = enabled;
        _configService.SaveConfig();
        return true;
    }

    #endregion

    #region Launcher Branch

    /// <inheritdoc/>
    public string GetLauncherBranch() => _configService.Configuration.LauncherBranch;
    
    /// <inheritdoc/>
    public bool SetLauncherBranch(string branch)
    {
        var normalizedBranch = branch?.ToLowerInvariant() ?? "release";
        if (normalizedBranch != "release" && normalizedBranch != "beta")
        {
            normalizedBranch = "release";
        }
        
        if (_configService.Configuration.LauncherBranch == normalizedBranch)
        {
            return false;
        }
        
        _configService.Configuration.LauncherBranch = normalizedBranch;
        _configService.SaveConfig();
        Logger.Info("Config", $"Launcher branch set to: {normalizedBranch}");
        return true;
    }

    #endregion

    #region Version & Branch Settings
    // NOTE: These methods use deprecated Config fields for backward compatibility
    // They will be removed in a future version when migration to Instances is complete

    #pragma warning disable CS0618 // Using obsolete fields for backward compatibility
    /// <inheritdoc/>
    public string GetVersionType() => _configService.Configuration.VersionType;

    /// <inheritdoc/>
    public bool SetVersionType(string type)
    {
        if (_configService.Configuration.VersionType == type) return false;
        _configService.Configuration.VersionType = type;
        _configService.SaveConfig();
        return true;
    }

    /// <inheritdoc/>
    public int GetSelectedVersion() => _configService.Configuration.SelectedVersion;

    /// <inheritdoc/>
    public bool SetSelectedVersion(int version)
    {
        if (_configService.Configuration.SelectedVersion == version) return false;
        _configService.Configuration.SelectedVersion = version;
        _configService.SaveConfig();
        return true;
    }
    #pragma warning restore CS0618

    #endregion

    #region Close After Launch Setting

    /// <inheritdoc/>
    public bool GetCloseAfterLaunch() => _configService.Configuration.CloseAfterLaunch;
    
    /// <inheritdoc/>
    public bool SetCloseAfterLaunch(bool enabled)
    {
        _configService.Configuration.CloseAfterLaunch = enabled;
        _configService.SaveConfig();
        Logger.Info("Config", $"Close after launch set to: {enabled}");
        return true;
    }

    #endregion

    #region Launch After Download Setting

    /// <inheritdoc/>
    public bool GetLaunchAfterDownload() => _configService.Configuration.LaunchAfterDownload;

    /// <inheritdoc/>
    public bool SetLaunchAfterDownload(bool enabled)
    {
        _configService.Configuration.LaunchAfterDownload = enabled;
        _configService.SaveConfig();
        Logger.Info("Config", $"Launch after download set to: {enabled}");
        return true;
    }

    #endregion

    #region Discord Announcements Settings

    /// <inheritdoc/>
    public bool GetShowDiscordAnnouncements() => _configService.Configuration.ShowDiscordAnnouncements;
    
    /// <inheritdoc/>
    public bool SetShowDiscordAnnouncements(bool enabled)
    {
        _configService.Configuration.ShowDiscordAnnouncements = enabled;
        _configService.SaveConfig();
        Logger.Info("Config", $"Show Discord announcements set to: {enabled}");
        return true;
    }

    /// <inheritdoc/>
    public bool IsAnnouncementDismissed(string announcementId)
    {
        return _configService.Configuration.DismissedAnnouncementIds.Contains(announcementId);
    }

    /// <inheritdoc/>
    public bool DismissAnnouncement(string announcementId)
    {
        var config = _configService.Configuration;
        if (!config.DismissedAnnouncementIds.Contains(announcementId))
        {
            config.DismissedAnnouncementIds.Add(announcementId);
            _configService.SaveConfig();
            Logger.Info("Discord", $"Announcement {announcementId} dismissed");
        }
        return true;
    }

    #endregion

    #region News Settings

    /// <inheritdoc/>
    public bool GetDisableNews() => _configService.Configuration.DisableNews;
    
    /// <inheritdoc/>
    public bool SetDisableNews(bool disabled)
    {
        _configService.Configuration.DisableNews = disabled;
        _configService.SaveConfig();
        Logger.Info("Config", $"News disabled set to: {disabled}");
        return true;
    }

    #endregion

    #region Background Settings

    /// <inheritdoc/>
    public string GetBackgroundMode() => _configService.Configuration.BackgroundMode;
    
    /// <inheritdoc/>
    public bool SetBackgroundMode(string mode)
    {
        _configService.Configuration.BackgroundMode = mode;
        _configService.SaveConfig();
        OnBackgroundChanged?.Invoke(mode);
        Logger.Info("Config", $"Background mode set to: {mode}");
        return true;
    }

    /// <inheritdoc/>
    public List<string> GetAvailableBackgrounds()
    {
        var backgrounds = new List<string>();
        // All backgrounds are now JPG format (PNG converted to save space)
        var jpgs = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 22, 23, 24, 25, 26, 27, 28, 29, 30 };

        foreach (var i in jpgs) backgrounds.Add($"bg_{i}.jpg");
        
        return backgrounds.OrderBy(x => 
        {
            var num = int.Parse(System.Text.RegularExpressions.Regex.Match(x, @"\d+").Value);
            return num;
        }).ToList();
    }

    #endregion

    #region Accent Color Settings

    /// <inheritdoc/>
    public string GetAccentColor() => _configService.Configuration.AccentColor;
    
    /// <inheritdoc/>
    public bool SetAccentColor(string color)
    {
        _configService.Configuration.AccentColor = color;
        _configService.SaveConfig();
        OnAccentColorChanged?.Invoke(color);
        Logger.Info("Config", $"Accent color set to: {color}");
        return true;
    }

    #endregion

    #region Onboarding State

    /// <inheritdoc/>
    public bool GetHasCompletedOnboarding() => _configService.Configuration.HasCompletedOnboarding;
    
    /// <inheritdoc/>
    public bool SetHasCompletedOnboarding(bool completed)
    {
        _configService.Configuration.HasCompletedOnboarding = completed;
        _configService.SaveConfig();
        Logger.Info("Config", $"Onboarding completed: {completed}");
        return true;
    }

    /// <inheritdoc/>
    public bool ResetOnboarding()
    {
        _configService.Configuration.HasCompletedOnboarding = false;
        _configService.SaveConfig();
        Logger.Info("Config", "Onboarding reset - will show on next launch");
        return true;
    }

    #endregion

    #region Online Mode Settings

    /// <inheritdoc/>
    public bool GetOnlineMode() => _configService.Configuration.OnlineMode;
    
    /// <inheritdoc/>
    public bool SetOnlineMode(bool online)
    {
        _configService.Configuration.OnlineMode = online;
        _configService.SaveConfig();
        Logger.Info("Config", $"Online mode set to: {online}");
        return true;
    }

    #endregion

    #region Auth Domain Settings

    /// <inheritdoc/>
    public string GetAuthDomain() => _configService.Configuration.AuthDomain;

    /// <inheritdoc/>
    public string GetJavaArguments() => _configService.Configuration.JavaArguments;

    /// <inheritdoc/>
    public bool GetUseCustomJava() => _configService.Configuration.UseCustomJava;

    /// <inheritdoc/>
    public string GetCustomJavaPath() => _configService.Configuration.CustomJavaPath;
    
    /// <inheritdoc/>
    public bool SetAuthDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            domain = "sessions.sanasol.ws";
        }
        _configService.Configuration.AuthDomain = domain;
        _configService.SaveConfig();
        Logger.Info("Config", $"Auth domain set to: {domain}");
        return true;
    }

    /// <inheritdoc/>
    public bool SetJavaArguments(string args)
    {
        _configService.Configuration.JavaArguments = args?.Trim() ?? "";
        _configService.SaveConfig();
        return true;
    }

    /// <inheritdoc/>
    public bool SetUseCustomJava(bool enabled)
    {
        _configService.Configuration.UseCustomJava = enabled;
        _configService.SaveConfig();
        return true;
    }

    /// <inheritdoc/>
    public bool SetCustomJavaPath(string path)
    {
        _configService.Configuration.CustomJavaPath = path?.Trim() ?? "";
        _configService.SaveConfig();
        return true;
    }

    #endregion

    #region GPU Preference Settings

    /// <inheritdoc/>
    public string GetGpuPreference() => _configService.Configuration.GpuPreference;
    
    /// <inheritdoc/>
    public bool SetGpuPreference(string preference)
    {
        var normalized = preference?.ToLowerInvariant() ?? "dedicated";
        if (normalized != "dedicated" && normalized != "integrated" && normalized != "auto")
        {
            normalized = "dedicated";
        }
        
        _configService.Configuration.GpuPreference = normalized;
        _configService.SaveConfig();
        Logger.Info("Config", $"GPU preference set to: {normalized}");
        return true;
    }

    /// <inheritdoc/>
    public bool GetUseDualAuth() => _configService.Configuration.UseDualAuth;

    /// <inheritdoc/>
    public bool SetUseDualAuth(bool useDualAuth)
    {
        _configService.Configuration.UseDualAuth = useDualAuth;
        _configService.SaveConfig();
        Logger.Info("Config", $"DualAuth mode set to: {useDualAuth}");
        return true;
    }

    /// <inheritdoc/>
    public string GetGameEnvironmentVariables() => _configService.Configuration.GameEnvironmentVariables;
    
    /// <inheritdoc/>
    public bool SetGameEnvironmentVariables(string envVars)
    {
        _configService.Configuration.GameEnvironmentVariables = envVars ?? "";
        _configService.SaveConfig();
        Logger.Info("Config", $"Game environment variables set to: {envVars}");
        return true;
    }

    /// <inheritdoc/>
    public string GetInstanceDirectory() => _configService.Configuration.InstanceDirectory;

    /// <inheritdoc/>
    public bool GetShowAlphaMods() => _configService.Configuration.ShowAlphaMods;

    /// <inheritdoc/>
    public bool SetShowAlphaMods(bool show)
    {
        _configService.Configuration.ShowAlphaMods = show;
        _configService.SaveConfig();
        return true;
    }

    #endregion
}
