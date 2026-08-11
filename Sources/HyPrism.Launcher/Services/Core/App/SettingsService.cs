// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Infrastructure;

namespace HyPrism.Services.Core.App;

/// <summary>
/// Manages all launcher settings (preferences, UI config, behavior options).
/// Provides centralized access to configuration properties with automatic persistence.
/// </summary>
public class SettingsService : ISettingsService
{
    #region Fields and Constructor

    private readonly IConfigStore _configService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsService"/> class.
    /// </summary>
    /// <param name="configService">The configuration service for persisting settings.</param>
    public SettingsService(IConfigStore configService)
    {
        _configService = configService;
    }
    
    /// <inheritdoc/>
    public event Action<string?>? OnBackgroundChanged;

    #endregion

    #region Localization Settings

    /// <inheritdoc/>
    public string GetLanguage() => _configService.Configuration.Language;

    /// <inheritdoc/>
    public bool SetLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return false;

        _configService.Configuration.Language = languageCode;
        _configService.SaveConfig();
        Logger.Info("Config", $"Language preference changed to: {languageCode}");
        return true;
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
        var pngs = new HashSet<int> { 4, 6, 9, 12, 16, 19 };
        var ids = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 22, 23, 24, 25, 26, 27, 28, 29, 30 };

        foreach (var id in ids)
            backgrounds.Add($"bg_{id}.{(pngs.Contains(id) ? "png" : "jpg")}");
        
        return backgrounds.OrderBy(x => 
        {
            var num = int.Parse(System.Text.RegularExpressions.Regex.Match(x, @"\d+").Value);
            return num;
        }).ToList();
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
