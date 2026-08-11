// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPrism.Desktop.Localization;
using HyPrism.Desktop.Features.About;
using HyPrism.Desktop.Platform;
using HyPrism.Core.Infrastructure;
using HyPrism.Core.Game.Launch;

namespace HyPrism.Desktop.Features.Settings;

public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private const int MinimumJavaMemoryMb = 1024;
    private const int JavaMemoryStepMb = 256;
    private static readonly HashSet<string> BotLogins = new(StringComparer.OrdinalIgnoreCase)
    {
        "copilot",
        "github-actions",
        "dependabot",
        "renovate",
        "semantic-release-bot",
        "allcontributors",
        "imgbot",
        "codecov",
        "snyk-bot",
        "greenkeeper",
        "google-labs-jules"
    };

    private readonly IDesktopSettingsStore _settings;
    private readonly IExternalUriLauncher _uriLauncher;
    private readonly StringLocalizer _localizer;
    private readonly IFilePicker? _filePicker;
    private readonly IGitHubClient? _gitHubClient;
    private bool _updatingJavaMemory;
    private bool _updatingJavaArguments;
    private bool _aboutDataLoadStarted;
    private bool _aboutDataLoaded;
    private bool _disposed;
    private int _aboutContributorSlotCapacity = 9;
    private GitHubCommit? _latestMainCommit;
    private readonly List<AboutContributorViewModel> _aboutContributorPool = [];

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
    [NotifyPropertyChangedFor(nameof(ActiveCategoryTitle))]
    private string _selectedCategory = "general";

    [ObservableProperty] private SettingChoiceViewModel _selectedLanguage;
    [ObservableProperty] private BackgroundChoiceViewModel _selectedBackground;
    [ObservableProperty] private SettingChoiceViewModel _selectedGpuPreference;
    [ObservableProperty] private bool _closeAfterLaunch;
    [ObservableProperty] private bool _launchAfterDownload;
    [ObservableProperty] private bool _showAlphaMods;
    [ObservableProperty] private bool _musicEnabled;
    [ObservableProperty] private bool _disableNews;
    [ObservableProperty] private bool _showDiscordAnnouncements;
    [ObservableProperty] private bool _onlineMode;
    [ObservableProperty] private bool _useDualAuth;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseBundledJava))]
    private bool _useCustomJava;
    [ObservableProperty] private string _authDomain;
    [ObservableProperty] private string _customJavaPath;
    [ObservableProperty] private string _javaArguments;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JavaMaximumRamValue))]
    [NotifyPropertyChangedFor(nameof(JavaInitialRamMaximum))]
    private double _javaMaximumRamMb;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JavaInitialRamValue))]
    private double _javaInitialRamMb;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasJavaPathError))]
    private string _javaPathError = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasJavaArgumentsError))]
    private string _javaArgumentsError = string.Empty;
    [ObservableProperty] private string _gameEnvironmentVariables;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isCompactLayout;
    [ObservableProperty] private bool _isAboutDataLoading;
    [ObservableProperty] private bool _hasAboutLatestCommit;
    [ObservableProperty] private bool _hasMoreAboutContributors;
    [ObservableProperty] private string _aboutLatestCommitSha = string.Empty;
    [ObservableProperty] private string _aboutLatestCommitHint = string.Empty;
    [ObservableProperty] private string _aboutContributorOverflow = string.Empty;

    public SettingsViewModel(
        IDesktopSettingsStore settings,
        IExternalUriLauncher uriLauncher,
        StringLocalizer localizer,
        IFilePicker? filePicker = null,
        IGitHubClient? gitHubClient = null)
    {
        _settings = settings;
        _uriLauncher = uriLauncher;
        _localizer = localizer;
        _filePicker = filePicker;
        _gitHubClient = gitHubClient;

        Categories = new ObservableCollection<SettingCategoryViewModel>(
        [
            new("general", localizer["settings.general"], "general.png"),
            new("downloads", localizer["settings.downloads.title"], "downloads.png"),
            new("java", localizer["settings.java"], "java.png"),
            new("visual", localizer["settings.visual"], "visual.png"),
            new("network", localizer["settings.network"], "network.png"),
            new("graphics", localizer["settings.graphics"], "graphics.png"),
            new("variables", localizer["settings.variables"], "variables.png"),
            new("data", localizer["settings.data"], "data.png"),
            new("about", localizer["settings.about"], "about.png")
        ]);
        Categories[0].IsSelected = true;

        Languages = new ObservableCollection<SettingChoiceViewModel>(
            localizer.AvailableLanguages.Select(language =>
                new SettingChoiceViewModel(language.Key, language.Value, GetFlagCountryCode(language.Key))));
        var availableBackgrounds = settings.AvailableBackgrounds;
        Backgrounds = new ObservableCollection<BackgroundChoiceViewModel>(
            [new("auto", localizer["settings.visualSettings.autoShuffle"], availableBackgrounds.Take(3)),
             .. availableBackgrounds.Select(name => new BackgroundChoiceViewModel(name, name, [name]))]);
        GpuPreferences = new ObservableCollection<SettingChoiceViewModel>(
        [
            new("dedicated", localizer["settings.graphicsSettings.gpu_dedicated"]),
            new("integrated", localizer["settings.graphicsSettings.gpu_integrated"]),
            new("auto", localizer["settings.graphicsSettings.gpu_auto"])
        ]);
        AboutTeamMembers = new ObservableCollection<AboutTeamMemberViewModel>(
        [
            new("yyyumeniku", "YY", "creatorRole"),
            new("sanasol", "SA", "authRole"),
            new("Daniel Freak", "DF", "codevRole", "freakdaniel"),
            new("XargonWan", "XW", "cicdRole"),
            new("FowlBytez", "FB", "testerRole"),
            new("Aarav2709", "A", "siteRole")
        ]);

        _selectedLanguage = FindChoice(Languages, settings.Language);
        _selectedBackground = FindBackgroundChoice(Backgrounds, settings.BackgroundMode);
        UpdateSelectedBackgroundState();
        _selectedGpuPreference = FindChoice(GpuPreferences, settings.GpuPreference);
        _closeAfterLaunch = settings.CloseAfterLaunch;
        _launchAfterDownload = settings.LaunchAfterDownload;
        _showAlphaMods = settings.ShowAlphaMods;
        _musicEnabled = settings.MusicEnabled;
        _disableNews = settings.DisableNews;
        _showDiscordAnnouncements = settings.ShowDiscordAnnouncements;
        _onlineMode = settings.OnlineMode;
        _useDualAuth = settings.UseDualAuth;
        _useCustomJava = settings.UseCustomJava;
        _authDomain = settings.AuthDomain;
        _customJavaPath = settings.CustomJavaPath;
        var persistedJavaArguments = settings.JavaArguments;
        _javaArguments = JvmArgumentBuilder.RemoveHeapArguments(persistedJavaArguments);
        DetectedSystemMemoryMb = Math.Max(4096, SystemMemoryProvider.GetSystemMemoryMb());
        MaximumJavaRamMb = Math.Max(
            MinimumJavaMemoryMb,
            Math.Floor(DetectedSystemMemoryMb * 0.75 / JavaMemoryStepMb) * JavaMemoryStepMb);
        _javaMaximumRamMb = NormalizeJavaMemory(
            JvmArgumentBuilder.ParseMaximumHeapMb(persistedJavaArguments) ?? 4096,
            MaximumJavaRamMb);
        var defaultInitialMemory = Math.Max(
            MinimumJavaMemoryMb,
            Math.Floor(_javaMaximumRamMb / 2 / JavaMemoryStepMb) * JavaMemoryStepMb);
        _javaInitialRamMb = NormalizeJavaMemory(
            JvmArgumentBuilder.ParseInitialHeapMb(persistedJavaArguments) ?? (int)defaultInitialMemory,
            _javaMaximumRamMb);
        _gameEnvironmentVariables = settings.GameEnvironmentVariables;

        RefreshLocalization();
    }

    public ObservableCollection<SettingCategoryViewModel> Categories { get; }
    public ObservableCollection<SettingChoiceViewModel> Languages { get; }
    public ObservableCollection<BackgroundChoiceViewModel> Backgrounds { get; }
    public ObservableCollection<SettingChoiceViewModel> GpuPreferences { get; }
    public ObservableCollection<AboutTeamMemberViewModel> AboutTeamMembers { get; }
    public ObservableCollection<AboutContributorViewModel> AboutContributors { get; } = [];

    public int DetectedSystemMemoryMb { get; }
    public double MinimumJavaRamMb => MinimumJavaMemoryMb;
    public double MaximumJavaRamMb { get; }
    public double JavaMemoryTickFrequency => JavaMemoryStepMb;
    public double JavaInitialRamMaximum => JavaMaximumRamMb;
    public bool UseBundledJava => !UseCustomJava;
    public bool HasJavaPathError => !string.IsNullOrWhiteSpace(JavaPathError);
    public bool HasJavaArgumentsError => !string.IsNullOrWhiteSpace(JavaArgumentsError);
    public string JavaMaximumRamValue => FormatMemory(JavaMaximumRamMb);
    public string JavaInitialRamValue => FormatMemory(JavaInitialRamMb);

    public string PageTitle { get; private set; } = string.Empty;
    public string PageDescription { get; private set; } = string.Empty;
    public string BackLabel { get; private set; } = string.Empty;
    public string LanguageCategoryTitle { get; private set; } = string.Empty;
    public string GeneralTitle { get; private set; } = string.Empty;
    public string DownloadsTitle { get; private set; } = string.Empty;
    public string JavaTitle { get; private set; } = string.Empty;
    public string VisualTitle { get; private set; } = string.Empty;
    public string NetworkTitle { get; private set; } = string.Empty;
    public string GraphicsTitle { get; private set; } = string.Empty;
    public string VariablesTitle { get; private set; } = string.Empty;
    public string DataTitle { get; private set; } = string.Empty;
    public string AboutTitle { get; private set; } = string.Empty;
    public string SaveLabel { get; private set; } = string.Empty;
    public string LanguageLabel { get; private set; } = string.Empty;
    public string LanguageHint { get; private set; } = string.Empty;
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
    public string BundledJavaLabel { get; private set; } = string.Empty;
    public string BundledJavaHint { get; private set; } = string.Empty;
    public string CustomJavaLabel { get; private set; } = string.Empty;
    public string CustomJavaHint { get; private set; } = string.Empty;
    public string CustomJavaPathPlaceholder { get; private set; } = string.Empty;
    public string SelectLabel { get; private set; } = string.Empty;
    public string RamAllocationLabel { get; private set; } = string.Empty;
    public string MaximumRamLabel { get; private set; } = string.Empty;
    public string InitialRamLabel { get; private set; } = string.Empty;
    public string JavaArgumentsLabel { get; private set; } = string.Empty;
    public string JavaArgumentsHint { get; private set; } = string.Empty;
    public string JavaArgumentsPlaceholder { get; private set; } = string.Empty;
    public string GpuLabel { get; private set; } = string.Empty;
    public string GpuHint { get; private set; } = string.Empty;
    public string EnvPresetsLabel { get; private set; } = string.Empty;
    public string EnvLabel { get; private set; } = string.Empty;
    public string EnvHint { get; private set; } = string.Empty;
    public string InstanceFolderLabel { get; private set; } = string.Empty;
    public string InstanceFolder { get; private set; } = string.Empty;
    public string AboutDisclaimer { get; private set; } = string.Empty;
    public string BugReportLabel { get; private set; } = string.Empty;
    public string AboutProjectTitle { get; private set; } = string.Empty;
    public string AboutGitHubHint { get; private set; } = string.Empty;
    public string AboutDocumentationLabel { get; private set; } = string.Empty;
    public string AboutDocumentationHint { get; private set; } = string.Empty;
    public string AboutCommunityTitle { get; private set; } = string.Empty;
    public string AboutDiscordHint { get; private set; } = string.Empty;
    public string AboutBugReportHint { get; private set; } = string.Empty;
    public string AboutTeamTitle { get; private set; } = string.Empty;
    public string AboutTeamHint { get; private set; } = string.Empty;
    public string AboutLegalTitle { get; private set; } = string.Empty;
    public string AboutLicenseLabel { get; private set; } = string.Empty;
    public string AboutLicenseHint { get; private set; } = string.Empty;
    public string AboutCreditsLabel { get; private set; } = string.Empty;
    public string AboutCreditsHint { get; private set; } = string.Empty;
    public string AboutCurrentVersionLabel { get; private set; } = string.Empty;
    public string AboutCurrentVersionHint { get; private set; } = string.Empty;
    public string AboutCurrentVersion { get; private set; } = string.Empty;
    public string AboutLatestCommitLabel { get; private set; } = string.Empty;
    public string AboutContributorsTitle { get; private set; } = string.Empty;
    public string AboutHytaleEulaLabel { get; private set; } = string.Empty;
    public string AboutHytaleEulaHint { get; private set; } = string.Empty;

    public string ActiveCategoryTitle =>
        Categories.FirstOrDefault(category => category.Id == SelectedCategory)?.Label ?? GeneralTitle;

    public bool IsGeneral => SelectedCategory == "general";
    public bool IsDownloads => SelectedCategory == "downloads";
    public bool IsJava => SelectedCategory == "java";
    public bool IsVisual => SelectedCategory == "visual";
    public bool IsNetwork => SelectedCategory == "network";
    public bool IsGraphics => SelectedCategory == "graphics";
    public bool IsVariables => SelectedCategory == "variables";
    public bool IsData => SelectedCategory == "data";
    public bool IsAbout => SelectedCategory == "about";
    public void RefreshLocalization()
    {
        PageTitle = _localizer["dock.settings"];
        PageDescription = _localizer["desktopSettings.description"];
        BackLabel = _localizer["common.back"];
        LanguageCategoryTitle = _localizer["desktopSettings.categories.language"];
        GeneralTitle = _localizer["desktopSettings.categories.miscellaneous"];
        DownloadsTitle = _localizer["settings.downloads.title"];
        JavaTitle = _localizer["settings.java"];
        VisualTitle = _localizer["settings.visualSettings.title"];
        NetworkTitle = _localizer["settings.network"];
        GraphicsTitle = _localizer["settings.graphicsSettings.title"];
        VariablesTitle = _localizer["settings.variablesSettings.title"];
        DataTitle = _localizer["settings.dataSettings.title"];
        AboutTitle = _localizer["settings.aboutSettings.title"];
        SaveLabel = _localizer["common.save"];
        LanguageLabel = _localizer["settings.languageSettings.interfaceLanguage"];
        LanguageHint = _localizer["settings.languageSettings.interfaceLanguageHint"];
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
        BundledJavaLabel = _localizer["settings.javaSettings.useBundledJava"];
        BundledJavaHint = _localizer["settings.javaSettings.useBundledJavaHint"];
        CustomJavaLabel = _localizer["settings.javaSettings.useCustomJava"];
        CustomJavaHint = _localizer["settings.javaSettings.useCustomJavaHint"];
        CustomJavaPathPlaceholder = _localizer["settings.javaSettings.customJavaPathPlaceholder"];
        SelectLabel = _localizer["common.select"];
        RamAllocationLabel = _localizer["settings.javaSettings.ramAllocation"];
        MaximumRamLabel = _localizer["settings.javaSettings.maxRam"];
        InitialRamLabel = _localizer["settings.javaSettings.initialRam"];
        JavaArgumentsLabel = _localizer["settings.javaSettings.jvmArguments"];
        JavaArgumentsHint = _localizer["settings.javaSettings.jvmArgumentsHint"];
        JavaArgumentsPlaceholder = _localizer["settings.javaSettings.jvmArgumentsPlaceholder"];
        GpuLabel = _localizer["settings.graphicsSettings.gpuPreference"];
        GpuHint = _localizer["settings.graphicsSettings.gpuPreferenceHint"];
        EnvPresetsLabel = _localizer["settings.variablesSettings.commonPresets"];
        EnvLabel = _localizer["settings.variablesSettings.customEnvVars"];
        EnvHint = _localizer["settings.variablesSettings.customEnvVarsHint"];
        InstanceFolderLabel = _localizer["settings.dataSettings.instanceFolder"];
        InstanceFolder = string.IsNullOrWhiteSpace(_settings.InstanceDirectory)
            ? _localizer["desktopSettings.defaultLocation"]
            : _settings.InstanceDirectory;
        AboutDisclaimer = _localizer["settings.aboutSettings.disclaimer"];
        BugReportLabel = _localizer["settings.aboutSettings.bugReport"];
        AboutProjectTitle = _localizer["settings.aboutSettings.project"];
        AboutGitHubHint = _localizer["settings.aboutSettings.githubHint"];
        AboutDocumentationLabel = _localizer["settings.aboutSettings.documentation"];
        AboutDocumentationHint = _localizer["settings.aboutSettings.documentationHint"];
        AboutCommunityTitle = _localizer["settings.aboutSettings.community"];
        AboutDiscordHint = _localizer["settings.aboutSettings.discordHint"];
        AboutBugReportHint = _localizer["settings.aboutSettings.bugReportHint"];
        AboutTeamTitle = _localizer["settings.aboutSettings.coreTeam"];
        AboutTeamHint = _localizer["settings.aboutSettings.coreTeamDescription"];
        AboutLegalTitle = _localizer["settings.aboutSettings.legal"];
        AboutLicenseLabel = _localizer["settings.aboutSettings.license"];
        AboutLicenseHint = _localizer["settings.aboutSettings.licenseHint"];
        AboutCreditsLabel = _localizer["settings.aboutSettings.credits"];
        AboutCreditsHint = _localizer["settings.aboutSettings.creditsHint"];
        AboutCurrentVersionLabel = _localizer["settings.aboutSettings.currentVersion"];
        AboutCurrentVersionHint = _localizer["settings.aboutSettings.currentVersionHint"];
        AboutCurrentVersion = GetApplicationVersion();
        AboutLatestCommitLabel = _localizer["settings.aboutSettings.latestMainCommit"];
        AboutContributorsTitle = _localizer["settings.aboutSettings.contributors"];
        AboutHytaleEulaLabel = _localizer["settings.aboutSettings.hytaleEula"];
        AboutHytaleEulaHint = _localizer["settings.aboutSettings.hytaleEulaHint"];
        AboutLatestCommitHint = _latestMainCommit?.Message ?? _localizer[
            _aboutDataLoaded
                ? "settings.aboutSettings.latestMainCommitUnavailable"
                : "settings.aboutSettings.latestMainCommitLoading"];

        foreach (var member in AboutTeamMembers)
            member.Role = _localizer[$"settings.aboutSettings.{member.RoleKey}"];

        UpdateCategoryLabel("general", _localizer["settings.general"]);
        UpdateCategoryLabel("downloads", _localizer["settings.downloads.title"]);
        UpdateCategoryLabel("java", _localizer["settings.java"]);
        UpdateCategoryLabel("visual", _localizer["desktopSettings.categories.appearance"]);
        UpdateCategoryLabel("network", _localizer["settings.network"]);
        UpdateCategoryLabel("graphics", _localizer["settings.graphics"]);
        UpdateCategoryLabel("variables", _localizer["settings.variables"]);
        UpdateCategoryLabel("data", _localizer["settings.data"]);
        UpdateCategoryLabel("about", _localizer["settings.about"]);
        UpdateCategoryDescription("general", _localizer["settings.categoryDescriptions.general"]);
        UpdateCategoryDescription("downloads", _localizer["settings.categoryDescriptions.downloads"]);
        UpdateCategoryDescription("java", _localizer["settings.categoryDescriptions.java"]);
        UpdateCategoryDescription("visual", _localizer["settings.categoryDescriptions.visual"]);
        UpdateCategoryDescription("network", _localizer["settings.categoryDescriptions.network"]);
        UpdateCategoryDescription("graphics", _localizer["settings.categoryDescriptions.graphics"]);
        UpdateCategoryDescription("variables", _localizer["settings.categoryDescriptions.variables"]);
        UpdateCategoryDescription("data", _localizer["settings.categoryDescriptions.data"]);
        UpdateCategoryDescription("about", _localizer["settings.categoryDescriptions.about"]);
        Backgrounds.First(choice => choice.Value == "auto").Display =
            _localizer["settings.visualSettings.autoShuffle"];
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

        if (_localizer.SetLanguage(value.Value))
            _settings.Language = _localizer.CurrentLanguage;
    }
    partial void OnSelectedBackgroundChanged(BackgroundChoiceViewModel value)
    {
        if (value is null)
            return;

        UpdateSelectedBackgroundState();
        _settings.BackgroundMode = value.Value;
    }
    partial void OnSelectedGpuPreferenceChanged(SettingChoiceViewModel value) => _settings.GpuPreference = value.Value;
    partial void OnCloseAfterLaunchChanged(bool value) => _settings.CloseAfterLaunch = value;
    partial void OnLaunchAfterDownloadChanged(bool value) => _settings.LaunchAfterDownload = value;
    partial void OnShowAlphaModsChanged(bool value) => _settings.ShowAlphaMods = value;
    partial void OnMusicEnabledChanged(bool value) => _settings.MusicEnabled = value;
    partial void OnDisableNewsChanged(bool value) => _settings.DisableNews = value;
    partial void OnShowDiscordAnnouncementsChanged(bool value) => _settings.ShowDiscordAnnouncements = value;
    partial void OnOnlineModeChanged(bool value) => _settings.OnlineMode = value;
    partial void OnUseDualAuthChanged(bool value) => _settings.UseDualAuth = value;
    partial void OnUseCustomJavaChanged(bool value)
    {
        JavaPathError = string.Empty;
        _settings.UseCustomJava = value;
    }

    partial void OnCustomJavaPathChanged(string value) => JavaPathError = string.Empty;
    partial void OnJavaArgumentsChanged(string value)
    {
        if (_updatingJavaArguments)
            return;

        var withoutHeap = JvmArgumentBuilder.RemoveHeapArguments(value);
        if (JvmArgumentBuilder.ContainsHeapArguments(value))
        {
            _updatingJavaArguments = true;
            try
            {
                JavaArguments = withoutHeap;
            }
            finally
            {
                _updatingJavaArguments = false;
            }

            JavaArgumentsError = _localizer["settings.javaSettings.jvmMemoryArgumentsManaged"];
            return;
        }

        JavaArgumentsError = string.Empty;
    }
    partial void OnJavaMaximumRamMbChanged(double value) => PersistJavaMemory();
    partial void OnJavaInitialRamMbChanged(double value) => PersistJavaMemory();

    [RelayCommand]
    private void SelectCategory(SettingCategoryViewModel? category)
    {
        if (category is null)
            return;

        SelectedCategory = category.Id;
        foreach (var item in Categories)
            item.IsSelected = ReferenceEquals(item, category);
        StatusMessage = string.Empty;

        if (string.Equals(category.Id, "about", StringComparison.Ordinal))
            _ = LoadAboutDataAsync();
    }

    [RelayCommand]
    private void SaveNetwork()
    {
        _settings.AuthDomain = AuthDomain;
        ShowSaved();
    }

    [RelayCommand]
    private void SelectBundledJava() => UseCustomJava = false;

    [RelayCommand]
    private void SelectCustomJava() => UseCustomJava = true;

    [RelayCommand]
    private void SelectBackground(BackgroundChoiceViewModel? background)
    {
        if (background is not null && !ReferenceEquals(SelectedBackground, background))
            SelectedBackground = background;
    }

    [RelayCommand]
    private async Task BrowseJava()
    {
        if (_filePicker is null)
            return;

        var selectedPath = await _filePicker.BrowseJavaExecutableAsync();
        if (!string.IsNullOrWhiteSpace(selectedPath))
            CustomJavaPath = selectedPath;
    }

    [RelayCommand]
    private void SaveJavaPath()
    {
        var normalizedPath = CustomJavaPath.Trim();
        if (normalizedPath.Length == 0)
        {
            JavaPathError = _localizer["settings.javaSettings.customJavaPathRequired"];
            return;
        }

        if (!File.Exists(normalizedPath))
        {
            JavaPathError = _localizer["settings.javaSettings.customJavaPathNotFound"];
            return;
        }

        CustomJavaPath = normalizedPath;
        JavaPathError = string.Empty;
        _settings.CustomJavaPath = normalizedPath;
        UseCustomJava = true;
        ShowSaved();
    }

    [RelayCommand]
    private void SaveJavaArguments()
    {
        var withoutHeap = JvmArgumentBuilder.RemoveHeapArguments(JavaArguments);
        var sanitized = JvmArgumentBuilder.Sanitize(withoutHeap);
        var containedBlockedArguments =
            !string.Equals(sanitized, withoutHeap.Trim(), StringComparison.Ordinal);

        JavaArguments = sanitized;
        JavaArgumentsError = containedBlockedArguments
            ? _localizer["settings.javaSettings.jvmArgumentsBlocked"]
            : string.Empty;
        _settings.JavaArguments = BuildPersistedJavaArguments(sanitized);
        ShowSaved();
    }

    [RelayCommand]
    private void SaveVariables()
    {
        _settings.GameEnvironmentVariables = GameEnvironmentVariables;
        ShowSaved();
    }

    [RelayCommand] private Task OpenGitHub() => LaunchExternalAsync("https://github.com/hyprismteam/HyPrism");
    [RelayCommand] private Task OpenDocumentation() => LaunchExternalAsync("https://hyprismteam.github.io/HyPrism/docs/");
    [RelayCommand] private Task OpenDiscord() => LaunchExternalAsync("https://discord.gg/hyprism");
    [RelayCommand] private Task OpenBugReport() => LaunchExternalAsync("https://github.com/hyprismteam/HyPrism/issues/new/choose");
    [RelayCommand] private Task OpenLicense() => LaunchExternalAsync("https://github.com/hyprismteam/HyPrism/blob/main/LICENSE");
    [RelayCommand] private Task OpenHytaleEula() => LaunchExternalAsync("https://hytale.com/eula");
    [RelayCommand] private Task OpenIcons8() => LaunchExternalAsync("https://icons8.com");
    [RelayCommand]
    private Task OpenLatestCommit()
        => LaunchExternalAsync(_latestMainCommit?.HtmlUrl);

    [RelayCommand]
    private Task OpenTeamMember(AboutTeamMemberViewModel? member)
        => LaunchExternalAsync(member is null
            ? null
            : $"https://github.com/{member.GitHubLogin}");

    [RelayCommand]
    private Task OpenContributor(AboutContributorViewModel? contributor)
        => LaunchExternalAsync(contributor?.ProfileUrl);

    [RelayCommand]
    private Task OpenAllContributors()
        => LaunchExternalAsync("https://github.com/hyprismteam/HyPrism/graphs/contributors");

    private Task LaunchExternalAsync(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
           (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? _uriLauncher.LaunchAsync(uri)
            : Task.FromResult(false);

    private async Task LoadAboutDataAsync()
    {
        if (_gitHubClient is null || _aboutDataLoadStarted || _disposed)
            return;

        _aboutDataLoadStarted = true;
        IsAboutDataLoading = true;

        var contributorsTask = _gitHubClient.GetContributorsAsync();
        var commitTask = _gitHubClient.GetLatestMainCommitAsync();
        var teamAvatarTasks = AboutTeamMembers
            .Select(async member => new
            {
                Member = member,
                Avatar = await LoadGitHubAvatarAsync(
                    $"https://github.com/{Uri.EscapeDataString(member.GitHubLogin)}.png")
                    .ConfigureAwait(false)
            })
            .ToArray();

        var contributors = await contributorsTask.ConfigureAwait(false);
        var teamLogins = AboutTeamMembers
            .Select(member => member.GitHubLogin)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleContributors = contributors
            .Where(contributor => !teamLogins.Contains(contributor.Login) && !IsBot(contributor))
            .ToList();
        var contributorViewModels = visibleContributors
            .Select(contributor => new AboutContributorViewModel(
                contributor.Login,
                contributor.HtmlUrl,
                contributor.Contributions,
                contributor.AvatarUrl))
            .ToList();

        await Task.WhenAll(teamAvatarTasks).ConfigureAwait(false);
        var commit = await commitTask.ConfigureAwait(false);

        if (_disposed)
        {
            foreach (var contributor in contributorViewModels)
                contributor.Dispose();
            foreach (var result in teamAvatarTasks.Select(task => task.Result))
                result.Avatar?.Dispose();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _latestMainCommit = commit;
            _aboutDataLoaded = true;
            HasAboutLatestCommit = commit is not null;
            AboutLatestCommitSha = commit is null
                ? string.Empty
                : commit.Sha[..Math.Min(7, commit.Sha.Length)];
            AboutLatestCommitHint = commit?.Message
                                    ?? _localizer["settings.aboutSettings.latestMainCommitUnavailable"];

            foreach (var result in teamAvatarTasks.Select(task => task.Result))
                result.Member.Avatar = result.Avatar;

            foreach (var contributor in _aboutContributorPool)
                contributor.Dispose();
            _aboutContributorPool.Clear();
            _aboutContributorPool.AddRange(contributorViewModels);
            RebuildVisibleAboutContributors();
            IsAboutDataLoading = false;
        });
    }

    /// <summary>
    /// Updates how many contributor circles fit in the available row
    /// </summary>
    /// <param name="slotCount">The number of circle slots available in the contributors container</param>
    public void UpdateAboutContributorCapacity(int slotCount)
    {
        var normalizedCapacity = Math.Max(1, slotCount);
        if (_aboutContributorSlotCapacity == normalizedCapacity)
            return;

        _aboutContributorSlotCapacity = normalizedCapacity;
        RebuildVisibleAboutContributors();
    }

    private void RebuildVisibleAboutContributors()
    {
        var visibleCount = _aboutContributorPool.Count > _aboutContributorSlotCapacity
            ? Math.Max(0, _aboutContributorSlotCapacity - 1)
            : _aboutContributorPool.Count;

        AboutContributors.Clear();
        foreach (var contributor in _aboutContributorPool.Take(visibleCount))
            AboutContributors.Add(contributor);

        var remainingCount = _aboutContributorPool.Count - visibleCount;
        HasMoreAboutContributors = remainingCount > 0;
        AboutContributorOverflow = $"+{remainingCount}";
        _ = LoadVisibleContributorAvatarsAsync();
    }

    private async Task LoadVisibleContributorAvatarsAsync()
    {
        if (_gitHubClient is null || _disposed)
            return;

        var contributors = AboutContributors
            .Where(contributor => !contributor.AvatarLoadStarted)
            .ToArray();
        foreach (var contributor in contributors)
            contributor.AvatarLoadStarted = true;

        var results = await Task.WhenAll(contributors.Select(async contributor => new
        {
            Contributor = contributor,
            Avatar = await LoadGitHubAvatarAsync(contributor.AvatarUrl).ConfigureAwait(false)
        })).ConfigureAwait(false);

        if (_disposed)
        {
            foreach (var result in results)
                result.Avatar?.Dispose();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var result in results)
                result.Contributor.Avatar = result.Avatar;
        });
    }

    private async Task<Bitmap?> LoadGitHubAvatarAsync(string url)
    {
        try
        {
            var bytes = await _gitHubClient!.LoadAvatarAsync(url, 96).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
                return null;

            return await Task.Run(() =>
            {
                using var stream = new MemoryStream(bytes, writable: false);
                return Bitmap.DecodeToWidth(stream, 96, BitmapInterpolationMode.HighQuality);
            }).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsBot(GitHubUser contributor)
    {
        if (string.Equals(contributor.Type, "Bot", StringComparison.OrdinalIgnoreCase))
            return true;

        var login = contributor.Login;
        return BotLogins.Contains(login) ||
               login.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase) ||
               login.EndsWith("-bot", StringComparison.OrdinalIgnoreCase) ||
               login.EndsWith("_bot", StringComparison.OrdinalIgnoreCase) ||
               login.EndsWith("bot[bot]", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowSaved() => StatusMessage = "✓";

    private void PersistJavaMemory()
    {
        if (_updatingJavaMemory)
            return;

        _updatingJavaMemory = true;
        try
        {
            var normalizedMaximum = NormalizeJavaMemory(JavaMaximumRamMb, MaximumJavaRamMb);
            var normalizedInitial = NormalizeJavaMemory(JavaInitialRamMb, normalizedMaximum);
            JavaMaximumRamMb = normalizedMaximum;
            JavaInitialRamMb = normalizedInitial;

            _settings.JavaArguments = BuildPersistedJavaArguments(JavaArguments);

            OnPropertyChanged(nameof(JavaMaximumRamValue));
            OnPropertyChanged(nameof(JavaInitialRamValue));
            OnPropertyChanged(nameof(JavaInitialRamMaximum));
        }
        finally
        {
            _updatingJavaMemory = false;
        }
    }

    private static double NormalizeJavaMemory(double value, double maximum)
    {
        var rounded = Math.Round(value / JavaMemoryStepMb, MidpointRounding.AwayFromZero) * JavaMemoryStepMb;
        return Math.Clamp(rounded, MinimumJavaMemoryMb, maximum);
    }

    private string BuildPersistedJavaArguments(string customArguments)
    {
        var updated = JvmArgumentBuilder.SetMaximumHeapMb(customArguments, (int)JavaMaximumRamMb);
        return JvmArgumentBuilder.SetInitialHeapMb(updated, (int)JavaInitialRamMb);
    }

    private void UpdateSelectedBackgroundState()
    {
        foreach (var background in Backgrounds)
            background.IsSelected = ReferenceEquals(background, SelectedBackground);
    }

    private static string FormatMemory(double memoryMb)
    {
        var memoryGb = memoryMb / 1024;
        return Math.Abs(memoryGb - Math.Round(memoryGb)) < 0.001
            ? $"{memoryGb:0} GB"
            : $"{memoryGb:0.#} GB";
    }

    private static SettingChoiceViewModel FindChoice(
        IEnumerable<SettingChoiceViewModel> choices,
        string? value)
        => choices.FirstOrDefault(choice => string.Equals(choice.Value, value, StringComparison.OrdinalIgnoreCase))
           ?? choices.First();

    private static BackgroundChoiceViewModel FindBackgroundChoice(
        IEnumerable<BackgroundChoiceViewModel> choices,
        string? value)
        => choices.FirstOrDefault(choice => string.Equals(choice.Value, value, StringComparison.OrdinalIgnoreCase))
           ?? choices.First();

    private void UpdateCategoryLabel(string id, string label)
        => Categories.First(category => category.Id == id).Label = label;

    private void UpdateCategoryDescription(string id, string description)
        => Categories.First(category => category.Id == id).Description = description;

    private static void UpdateChoiceDisplay(
        IEnumerable<SettingChoiceViewModel> choices,
        string value,
        string display)
        => choices.First(choice => choice.Value == value).Display = display;

    private static string GetFlagCountryCode(string cultureName)
    {
        var separatorIndex = cultureName.LastIndexOf('-');
        return separatorIndex >= 0
            ? cultureName[(separatorIndex + 1)..].ToUpperInvariant()
            : cultureName.ToUpperInvariant();
    }

    private static string GetApplicationVersion()
    {
        var assembly = typeof(SettingsViewModel).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var metadataIndex = informationalVersion.IndexOf('+');
            return metadataIndex > 0
                ? informationalVersion[..metadataIndex]
                : informationalVersion;
        }

        var version = assembly.GetName().Version;
        return version is null
            ? "0.0.0"
            : $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var member in AboutTeamMembers)
            member.Dispose();
        foreach (var contributor in _aboutContributorPool)
            contributor.Dispose();
        _aboutContributorPool.Clear();
        AboutContributors.Clear();
    }
}

public sealed partial class AboutTeamMemberViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Creates a core team entry shown on the About page
    /// </summary>
    /// <param name="displayName">The name displayed in the launcher</param>
    /// <param name="initials">The fallback initials used before an avatar is available</param>
    /// <param name="roleKey">The localization key suffix for the team role</param>
    /// <param name="gitHubLogin">The GitHub login, or <c>null</c> when it matches the display name</param>
    public AboutTeamMemberViewModel(
        string displayName,
        string initials,
        string roleKey,
        string? gitHubLogin = null)
    {
        DisplayName = displayName;
        Initials = initials;
        RoleKey = roleKey;
        GitHubLogin = gitHubLogin ?? displayName;
    }

    public string DisplayName { get; }
    public string Initials { get; }
    public string RoleKey { get; }
    public string GitHubLogin { get; }
    public bool HasAvatar => Avatar is not null;
    [ObservableProperty] private string _role = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatar))]
    private Bitmap? _avatar;

    partial void OnAvatarChanging(Bitmap? oldValue, Bitmap? newValue)
        => oldValue?.Dispose();

    /// <inheritdoc />
    public void Dispose()
        => Avatar = null;
}

/// <summary>
/// Represents a repository contributor displayed on the About page
/// </summary>
public sealed partial class AboutContributorViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Creates a contributor entry
    /// </summary>
    /// <param name="login">The GitHub login</param>
    /// <param name="profileUrl">The public GitHub profile URL</param>
    /// <param name="contributions">The contribution count reported by GitHub</param>
    /// <param name="avatarUrl">The GitHub avatar URL</param>
    public AboutContributorViewModel(
        string login,
        string profileUrl,
        int contributions,
        string avatarUrl)
    {
        Login = login;
        ProfileUrl = profileUrl;
        Contributions = contributions;
        AvatarUrl = avatarUrl;
    }

    public string Login { get; }
    public string ProfileUrl { get; }
    public int Contributions { get; }
    public string AvatarUrl { get; }
    public bool HasAvatar => Avatar is not null;
    public string Initial => string.IsNullOrWhiteSpace(Login) ? "?" : Login[..1].ToUpperInvariant();
    internal bool AvatarLoadStarted { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatar))]
    private Bitmap? _avatar;

    partial void OnAvatarChanging(Bitmap? oldValue, Bitmap? newValue)
        => oldValue?.Dispose();

    /// <inheritdoc />
    public void Dispose()
    {
        Avatar = null;
    }
}

public sealed partial class BackgroundChoiceViewModel : ObservableObject
{
    public BackgroundChoiceViewModel(string value, string display, IEnumerable<string> previewNames)
    {
        Value = value;
        _display = display;
        Previews = new ObservableCollection<Bitmap>(previewNames.Select(LoadPreview));
    }

    public string Value { get; }
    public bool IsAuto => string.Equals(Value, "auto", StringComparison.Ordinal);
    public bool IsSingle => !IsAuto;
    public ObservableCollection<Bitmap> Previews { get; }
    public Bitmap? Preview => Previews.FirstOrDefault();
    public Bitmap? PreviewOne => Previews.ElementAtOrDefault(0);
    public Bitmap? PreviewTwo => Previews.ElementAtOrDefault(1);
    public Bitmap? PreviewThree => Previews.ElementAtOrDefault(2);
    [ObservableProperty] private string _display;
    [ObservableProperty] private bool _isSelected;

    private static Bitmap LoadPreview(string name)
    {
        var uri = new Uri($"avares://HyPrism.Desktop/Assets/Backgrounds/{name}");
        using var stream = AssetLoader.Open(uri);
        return Bitmap.DecodeToWidth(stream, 360, BitmapInterpolationMode.MediumQuality);
    }
}

public sealed partial class SettingCategoryViewModel : ObservableObject
{
    public SettingCategoryViewModel(string id, string label, string icon)
    {
        Id = id;
        var iconUri = $"avares://HyPrism.Desktop/Assets/Fluent/{icon}";
        using var iconStream = AssetLoader.Open(new Uri(iconUri));
        Icon = new Bitmap(iconStream);
        _label = label;
    }

    public string Id { get; }
    public Bitmap Icon { get; }
    [ObservableProperty] private string _label;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private bool _isSelected;
}

public sealed partial class SettingChoiceViewModel : ObservableObject
{
    public SettingChoiceViewModel(string value, string display, string? flagCountryCode = null)
    {
        Value = value;
        _display = display;

        if (!string.IsNullOrWhiteSpace(flagCountryCode))
        {
            var iconUri = $"avares://HyPrism.Desktop/Assets/Flags/{flagCountryCode}.png";
            using var iconStream = AssetLoader.Open(new Uri(iconUri));
            Icon = new Bitmap(iconStream);
        }
    }

    public string Value { get; }
    public Bitmap? Icon { get; }
    public bool HasIcon => Icon is not null;
    [ObservableProperty] private string _display;
}
