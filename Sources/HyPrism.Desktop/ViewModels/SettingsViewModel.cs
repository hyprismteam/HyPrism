// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPrism.Desktop.Localization;
using HyPrism.Services.Core.App;
using HyPrism.Services.Core.Platform;

namespace HyPrism.Desktop.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IBrowserService _browser;
    private readonly JsonLocalizer _localizer;
    private readonly Action<string>? _languageChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneral))]
    [NotifyPropertyChangedFor(nameof(IsDownloads))]
    [NotifyPropertyChangedFor(nameof(IsJava))]
    [NotifyPropertyChangedFor(nameof(IsVisual))]
    [NotifyPropertyChangedFor(nameof(IsNetwork))]
    [NotifyPropertyChangedFor(nameof(IsGraphics))]
    [NotifyPropertyChangedFor(nameof(IsVariables))]
    [NotifyPropertyChangedFor(nameof(IsData))]
    [NotifyPropertyChangedFor(nameof(IsAbout))]
    [NotifyPropertyChangedFor(nameof(IsDeveloper))]
    private string _selectedCategory = "general";

    [ObservableProperty] private SettingChoiceViewModel _selectedLanguage;
    [ObservableProperty] private SettingChoiceViewModel _selectedLauncherBranch;
    [ObservableProperty] private SettingChoiceViewModel _selectedBackground;
    [ObservableProperty] private SettingChoiceViewModel _selectedGpuPreference;
    [ObservableProperty] private bool _closeAfterLaunch;
    [ObservableProperty] private bool _launchAfterDownload;
    [ObservableProperty] private bool _showAlphaMods;
    [ObservableProperty] private bool _musicEnabled;
    [ObservableProperty] private bool _disableNews;
    [ObservableProperty] private bool _showDiscordAnnouncements;
    [ObservableProperty] private bool _onlineMode;
    [ObservableProperty] private bool _useDualAuth;
    [ObservableProperty] private bool _useCustomJava;
    [ObservableProperty] private string _authDomain;
    [ObservableProperty] private string _customJavaPath;
    [ObservableProperty] private string _javaArguments;
    [ObservableProperty] private string _gameEnvironmentVariables;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public SettingsViewModel(
        ISettingsService settings,
        IBrowserService browser,
        JsonLocalizer localizer,
        Action<string>? languageChanged = null)
    {
        _settings = settings;
        _browser = browser;
        _localizer = localizer;
        _languageChanged = languageChanged;

        Categories = new ObservableCollection<SettingCategoryViewModel>(
        [
            new("general", localizer["settings.general"]),
            new("downloads", localizer["settings.downloads.title"]),
            new("java", localizer["settings.java"]),
            new("visual", localizer["settings.visual"]),
            new("network", localizer["settings.network"]),
            new("graphics", localizer["settings.graphics"]),
            new("variables", localizer["settings.variables"]),
            new("data", localizer["settings.data"]),
            new("about", localizer["settings.about"]),
            new("developer", localizer["settings.developer"])
        ]);
        Categories[0].IsSelected = true;

        Languages = new ObservableCollection<SettingChoiceViewModel>(
        [
            new("en-US", "English"),
            new("ru-RU", "Русский"),
            new("uk-UA", "Українська"),
            new("be-BY", "Беларуская"),
            new("de-DE", "Deutsch"),
            new("fr-FR", "Français"),
            new("es-ES", "Español"),
            new("it-IT", "Italiano"),
            new("pt-BR", "Português"),
            new("tr-TR", "Türkçe"),
            new("ja-JP", "日本語"),
            new("ko-KR", "한국어"),
            new("zh-CN", "简体中文")
        ]);
        LauncherBranches = new ObservableCollection<SettingChoiceViewModel>(
        [
            new("release", localizer["settings.generalSettings.updateChannelStable"]),
            new("beta", localizer["settings.generalSettings.updateChannelBeta"])
        ]);
        Backgrounds = new ObservableCollection<SettingChoiceViewModel>(
            [new("auto", localizer["settings.visualSettings.autoShuffle"]),
             .. (settings.GetAvailableBackgrounds() ?? []).Select(name => new SettingChoiceViewModel(name, name))]);
        GpuPreferences = new ObservableCollection<SettingChoiceViewModel>(
        [
            new("dedicated", localizer["settings.graphicsSettings.gpu_dedicated"]),
            new("integrated", localizer["settings.graphicsSettings.gpu_integrated"]),
            new("auto", localizer["settings.graphicsSettings.gpu_auto"])
        ]);

        _selectedLanguage = FindChoice(Languages, settings.GetLanguage());
        _selectedLauncherBranch = FindChoice(LauncherBranches, settings.GetLauncherBranch());
        _selectedBackground = FindChoice(Backgrounds, settings.GetBackgroundMode());
        _selectedGpuPreference = FindChoice(GpuPreferences, settings.GetGpuPreference());
        _closeAfterLaunch = settings.GetCloseAfterLaunch();
        _launchAfterDownload = settings.GetLaunchAfterDownload();
        _showAlphaMods = settings.GetShowAlphaMods();
        _musicEnabled = settings.GetMusicEnabled();
        _disableNews = settings.GetDisableNews();
        _showDiscordAnnouncements = settings.GetShowDiscordAnnouncements();
        _onlineMode = settings.GetOnlineMode();
        _useDualAuth = settings.GetUseDualAuth();
        _useCustomJava = settings.GetUseCustomJava();
        _authDomain = settings.GetAuthDomain() ?? string.Empty;
        _customJavaPath = settings.GetCustomJavaPath() ?? string.Empty;
        _javaArguments = settings.GetJavaArguments() ?? string.Empty;
        _gameEnvironmentVariables = settings.GetGameEnvironmentVariables() ?? string.Empty;

        RefreshLocalization();
    }

    public ObservableCollection<SettingCategoryViewModel> Categories { get; }
    public ObservableCollection<SettingChoiceViewModel> Languages { get; }
    public ObservableCollection<SettingChoiceViewModel> LauncherBranches { get; }
    public ObservableCollection<SettingChoiceViewModel> Backgrounds { get; }
    public ObservableCollection<SettingChoiceViewModel> GpuPreferences { get; }

    public string PageTitle { get; private set; } = string.Empty;
    public string PageDescription { get; private set; } = string.Empty;
    public string GeneralTitle { get; private set; } = string.Empty;
    public string DownloadsTitle { get; private set; } = string.Empty;
    public string JavaTitle { get; private set; } = string.Empty;
    public string VisualTitle { get; private set; } = string.Empty;
    public string NetworkTitle { get; private set; } = string.Empty;
    public string GraphicsTitle { get; private set; } = string.Empty;
    public string VariablesTitle { get; private set; } = string.Empty;
    public string DataTitle { get; private set; } = string.Empty;
    public string AboutTitle { get; private set; } = string.Empty;
    public string DeveloperTitle { get; private set; } = string.Empty;
    public string SaveLabel { get; private set; } = string.Empty;
    public string LanguageLabel { get; private set; } = string.Empty;
    public string LanguageHint { get; private set; } = string.Empty;
    public string UpdateChannelLabel { get; private set; } = string.Empty;
    public string UpdateChannelHint { get; private set; } = string.Empty;
    public string CloseAfterLaunchLabel { get; private set; } = string.Empty;
    public string CloseAfterLaunchHint { get; private set; } = string.Empty;
    public string AlphaModsLabel { get; private set; } = string.Empty;
    public string AlphaModsHint { get; private set; } = string.Empty;
    public string LaunchAfterDownloadLabel { get; private set; } = string.Empty;
    public string LaunchAfterDownloadHint { get; private set; } = string.Empty;
    public string DownloadsInfo { get; private set; } = string.Empty;
    public string MusicLabel { get; private set; } = string.Empty;
    public string MusicHint { get; private set; } = string.Empty;
    public string BackgroundLabel { get; private set; } = string.Empty;
    public string AccentLabel { get; private set; } = string.Empty;
    public string HideNewsLabel { get; private set; } = string.Empty;
    public string HideNewsHint { get; private set; } = string.Empty;
    public string DiscordAnnouncementsLabel { get; private set; } = string.Empty;
    public string DiscordAnnouncementsHint { get; private set; } = string.Empty;
    public string OnlineModeLabel { get; private set; } = string.Empty;
    public string OnlineModeHint { get; private set; } = string.Empty;
    public string AuthServerLabel { get; private set; } = string.Empty;
    public string AuthServerHint { get; private set; } = string.Empty;
    public string DualAuthLabel { get; private set; } = string.Empty;
    public string DualAuthHint { get; private set; } = string.Empty;
    public string JavaRuntimeLabel { get; private set; } = string.Empty;
    public string JavaRuntimeHint { get; private set; } = string.Empty;
    public string CustomJavaLabel { get; private set; } = string.Empty;
    public string CustomJavaPathPlaceholder { get; private set; } = string.Empty;
    public string JavaArgumentsLabel { get; private set; } = string.Empty;
    public string JavaArgumentsHint { get; private set; } = string.Empty;
    public string GpuLabel { get; private set; } = string.Empty;
    public string GpuHint { get; private set; } = string.Empty;
    public string EnvPresetsLabel { get; private set; } = string.Empty;
    public string EnvLabel { get; private set; } = string.Empty;
    public string EnvHint { get; private set; } = string.Empty;
    public string InstanceFolderLabel { get; private set; } = string.Empty;
    public string InstanceFolder { get; private set; } = string.Empty;
    public string AboutDescription { get; private set; } = string.Empty;
    public string AboutDisclaimer { get; private set; } = string.Empty;
    public string BugReportLabel { get; private set; } = string.Empty;
    public string ReplayIntroLabel { get; private set; } = string.Empty;
    public string DeveloperWarning { get; private set; } = string.Empty;

    public bool IsGeneral => SelectedCategory == "general";
    public bool IsDownloads => SelectedCategory == "downloads";
    public bool IsJava => SelectedCategory == "java";
    public bool IsVisual => SelectedCategory == "visual";
    public bool IsNetwork => SelectedCategory == "network";
    public bool IsGraphics => SelectedCategory == "graphics";
    public bool IsVariables => SelectedCategory == "variables";
    public bool IsData => SelectedCategory == "data";
    public bool IsAbout => SelectedCategory == "about";
    public bool IsDeveloper => SelectedCategory == "developer";

    public void RefreshLocalization()
    {
        PageTitle = _localizer["dock.settings"];
        PageDescription = _localizer["desktopSettings.description"];
        GeneralTitle = _localizer["settings.generalSettings.title"];
        DownloadsTitle = _localizer["settings.downloads.title"];
        JavaTitle = _localizer["settings.java"];
        VisualTitle = _localizer["settings.visualSettings.title"];
        NetworkTitle = _localizer["settings.network"];
        GraphicsTitle = _localizer["settings.graphicsSettings.title"];
        VariablesTitle = _localizer["settings.variablesSettings.title"];
        DataTitle = _localizer["settings.dataSettings.title"];
        AboutTitle = _localizer["settings.aboutSettings.title"];
        DeveloperTitle = _localizer["settings.developerSettings.title"];
        SaveLabel = _localizer["common.save"];
        LanguageLabel = _localizer["settings.languageSettings.interfaceLanguage"];
        LanguageHint = _localizer["settings.languageSettings.interfaceLanguageHint"];
        UpdateChannelLabel = _localizer["settings.generalSettings.updateChannel"];
        UpdateChannelHint = _localizer["settings.generalSettings.updateChannelHint"];
        CloseAfterLaunchLabel = _localizer["settings.generalSettings.closeLauncher"];
        CloseAfterLaunchHint = _localizer["settings.generalSettings.closeLauncherHint"];
        AlphaModsLabel = _localizer["settings.generalSettings.showAlphaMods"];
        AlphaModsHint = _localizer["settings.generalSettings.showAlphaModsHint"];
        LaunchAfterDownloadLabel = _localizer["settings.downloads.launchAfterDownload"];
        LaunchAfterDownloadHint = _localizer["settings.downloads.launchAfterDownloadHint"];
        DownloadsInfo = _localizer["settings.downloads.howDownloadsWorkDescription"];
        MusicLabel = _localizer["desktopSettings.music"];
        MusicHint = _localizer["desktopSettings.musicHint"];
        BackgroundLabel = _localizer["settings.visualSettings.background"];
        AccentLabel = _localizer["settings.visualSettings.accentColor"];
        HideNewsLabel = _localizer["settings.visualSettings.hideNews"];
        HideNewsHint = _localizer["settings.visualSettings.hideNewsHint"];
        DiscordAnnouncementsLabel = _localizer["discord.showAnnouncements"];
        DiscordAnnouncementsHint = _localizer["desktopSettings.discordAnnouncementsHint"];
        OnlineModeLabel = _localizer["settings.networkSettings.onlineMode"];
        OnlineModeHint = _localizer["settings.networkSettings.onlineModeHint"];
        AuthServerLabel = _localizer["settings.networkSettings.authServer"];
        AuthServerHint = _localizer["settings.networkSettings.authServerHint"];
        DualAuthLabel = _localizer["settings.generalSettings.dualAuth"];
        DualAuthHint = _localizer["settings.generalSettings.dualAuthHint"];
        JavaRuntimeLabel = _localizer["settings.javaSettings.javaRuntime"];
        JavaRuntimeHint = _localizer["settings.javaSettings.javaRuntimeHint"];
        CustomJavaLabel = _localizer["settings.javaSettings.useCustomJava"];
        CustomJavaPathPlaceholder = _localizer["settings.javaSettings.customJavaPathPlaceholder"];
        JavaArgumentsLabel = _localizer["settings.javaSettings.jvmArguments"];
        JavaArgumentsHint = _localizer["settings.javaSettings.jvmArgumentsHint"];
        GpuLabel = _localizer["settings.graphicsSettings.gpuPreference"];
        GpuHint = _localizer["settings.graphicsSettings.gpuPreferenceHint"];
        EnvPresetsLabel = _localizer["settings.variablesSettings.commonPresets"];
        EnvLabel = _localizer["settings.variablesSettings.customEnvVars"];
        EnvHint = _localizer["settings.variablesSettings.customEnvVarsHint"];
        InstanceFolderLabel = _localizer["settings.dataSettings.instanceFolder"];
        InstanceFolder = string.IsNullOrWhiteSpace(_settings.GetInstanceDirectory())
            ? _localizer["desktopSettings.defaultLocation"]
            : _settings.GetInstanceDirectory();
        AboutDescription = _localizer["settings.aboutSettings.description"];
        AboutDisclaimer = _localizer["settings.aboutSettings.disclaimer"];
        BugReportLabel = _localizer["settings.aboutSettings.bugReport"];
        ReplayIntroLabel = _localizer["settings.aboutSettings.replayIntro"];
        DeveloperWarning = _localizer["settings.developerSettings.warning"];

        UpdateCategoryLabel("general", _localizer["settings.general"]);
        UpdateCategoryLabel("downloads", _localizer["settings.downloads.title"]);
        UpdateCategoryLabel("java", _localizer["settings.java"]);
        UpdateCategoryLabel("visual", _localizer["settings.visual"]);
        UpdateCategoryLabel("network", _localizer["settings.network"]);
        UpdateCategoryLabel("graphics", _localizer["settings.graphics"]);
        UpdateCategoryLabel("variables", _localizer["settings.variables"]);
        UpdateCategoryLabel("data", _localizer["settings.data"]);
        UpdateCategoryLabel("about", _localizer["settings.about"]);
        UpdateCategoryLabel("developer", _localizer["settings.developer"]);
        UpdateChoiceDisplay(LauncherBranches, "release", _localizer["settings.generalSettings.updateChannelStable"]);
        UpdateChoiceDisplay(LauncherBranches, "beta", _localizer["settings.generalSettings.updateChannelBeta"]);
        UpdateChoiceDisplay(Backgrounds, "auto", _localizer["settings.visualSettings.autoShuffle"]);
        UpdateChoiceDisplay(GpuPreferences, "dedicated", _localizer["settings.graphicsSettings.gpu_dedicated"]);
        UpdateChoiceDisplay(GpuPreferences, "integrated", _localizer["settings.graphicsSettings.gpu_integrated"]);
        UpdateChoiceDisplay(GpuPreferences, "auto", _localizer["settings.graphicsSettings.gpu_auto"]);

        OnPropertyChanged(string.Empty);
    }

    partial void OnSelectedLanguageChanged(SettingChoiceViewModel value)
    {
        if (value is null ||
            string.Equals(_localizer.CurrentLanguage, value.Value, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_settings.SetLanguage(value.Value))
            _languageChanged?.Invoke(value.Value);
    }
    partial void OnSelectedLauncherBranchChanged(SettingChoiceViewModel value) => _settings.SetLauncherBranch(value.Value);
    partial void OnSelectedBackgroundChanged(SettingChoiceViewModel value) => _settings.SetBackgroundMode(value.Value);
    partial void OnSelectedGpuPreferenceChanged(SettingChoiceViewModel value) => _settings.SetGpuPreference(value.Value);
    partial void OnCloseAfterLaunchChanged(bool value) => _settings.SetCloseAfterLaunch(value);
    partial void OnLaunchAfterDownloadChanged(bool value) => _settings.SetLaunchAfterDownload(value);
    partial void OnShowAlphaModsChanged(bool value) => _settings.SetShowAlphaMods(value);
    partial void OnMusicEnabledChanged(bool value) => _settings.SetMusicEnabled(value);
    partial void OnDisableNewsChanged(bool value) => _settings.SetDisableNews(value);
    partial void OnShowDiscordAnnouncementsChanged(bool value) => _settings.SetShowDiscordAnnouncements(value);
    partial void OnOnlineModeChanged(bool value) => _settings.SetOnlineMode(value);
    partial void OnUseDualAuthChanged(bool value) => _settings.SetUseDualAuth(value);
    partial void OnUseCustomJavaChanged(bool value) => _settings.SetUseCustomJava(value);

    [RelayCommand]
    private void SelectCategory(SettingCategoryViewModel? category)
    {
        if (category is null)
            return;

        SelectedCategory = category.Id;
        foreach (var item in Categories)
            item.IsSelected = ReferenceEquals(item, category);
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void SaveNetwork()
    {
        _settings.SetAuthDomain(AuthDomain);
        ShowSaved();
    }

    [RelayCommand]
    private void SaveJava()
    {
        _settings.SetCustomJavaPath(CustomJavaPath);
        _settings.SetJavaArguments(JavaArguments);
        ShowSaved();
    }

    [RelayCommand]
    private void SaveVariables()
    {
        _settings.SetGameEnvironmentVariables(GameEnvironmentVariables);
        ShowSaved();
    }

    [RelayCommand]
    private void ResetOnboarding()
    {
        _settings.ResetOnboarding();
        ShowSaved();
    }

    [RelayCommand] private void OpenGitHub() => _browser.OpenURL("https://github.com/HyPrismTeam/HyPrism");
    [RelayCommand] private void OpenDiscord() => _browser.OpenURL("https://discord.gg/hyprism");
    [RelayCommand] private void OpenBugReport() => _browser.OpenURL("https://github.com/HyPrismTeam/HyPrism/issues/new");

    private void ShowSaved() => StatusMessage = "✓";

    private static SettingChoiceViewModel FindChoice(
        IEnumerable<SettingChoiceViewModel> choices,
        string? value)
        => choices.FirstOrDefault(choice => string.Equals(choice.Value, value, StringComparison.OrdinalIgnoreCase))
           ?? choices.First();

    private void UpdateCategoryLabel(string id, string label)
        => Categories.First(category => category.Id == id).Label = label;

    private static void UpdateChoiceDisplay(
        IEnumerable<SettingChoiceViewModel> choices,
        string value,
        string display)
        => choices.First(choice => choice.Value == value).Display = display;
}

public sealed partial class SettingCategoryViewModel : ObservableObject
{
    public SettingCategoryViewModel(string id, string label)
    {
        Id = id;
        _label = label;
    }

    public string Id { get; }
    [ObservableProperty] private string _label;
    [ObservableProperty] private bool _isSelected;
}

public sealed partial class SettingChoiceViewModel : ObservableObject
{
    public SettingChoiceViewModel(string value, string display)
    {
        Value = value;
        _display = display;
    }

    public string Value { get; }
    [ObservableProperty] private string _display;
}
