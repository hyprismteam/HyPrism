// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPrism.Desktop.Features.About;
using HyPrism.Desktop.Features.Dashboard;
using HyPrism.Desktop.Features.Instances;
using HyPrism.Desktop.Features.News;
using HyPrism.Desktop.Features.Settings;
using HyPrism.Desktop.Localization;
using HyPrism.Desktop.Platform;
using HyPrism.Core.Models;
using HyPrism.Core.Application.Progress;
using HyPrism.Core.Game;
using HyPrism.Core.Game.Instances;
using HyPrism.Core.Game.Launch;
using HyPrism.Core.Game.Mods;
using HyPrism.Core.Game.Sources;
using HyPrism.Core.Game.Versions;
using HyPrism.Core.Accounts;

namespace HyPrism.Desktop.Shell;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private const string DashboardPage = "dashboard";
    private const string InstancesPage = "instances";
    private const string NewsPage = "news";
    private const string ProfilesPage = "profiles";
    private const string SettingsPage = "settings";
    private const int InitialNewsCount = 12;
    private const int NewsPageSize = 8;
    private const int MaximumNewsCount = 30;
    private const int CompactTransitionMilliseconds = 320;
    private const int ArticleSkeletonDelayMilliseconds = 180;
    private static readonly TimeSpan InstanceVersionCacheMaxAge = TimeSpan.FromMinutes(15);

    private readonly IInstanceRepository _instances;
    private readonly IGameLaunchCoordinator _gameLaunchCoordinator;
    private readonly IGameInstallationWorkflow _installationWorkflow;
    private readonly IGameProcessTracker _gameProcess;
    private readonly IProgressReporter _progress;
    private readonly IDesktopSettingsStore _settingsStore;
    private readonly IHytaleNewsClient _newsClient;
    private readonly IExternalUriLauncher _uriLauncher;
    private readonly IFilePicker? _filePicker;
    private readonly IGitHubClient? _gitHubClient;
    private readonly IMirrorCatalog? _mirrorCatalog;
    private readonly IMirrorDiscovery? _mirrorDiscovery;
    private readonly IGameVersionCatalog? _versionCatalog;
    private readonly IModManager? _modManager;
    private readonly HttpClient _httpClient;
    private readonly StringLocalizer _localizer;
    private InstanceInfo? _selectedInstance;
    private InstanceInfo? _managedInstance;
    private readonly List<NewsItemViewModel> _allNews = [];
    private readonly Dictionary<string, NewsArticleViewModel> _articleViewModelCache =
        new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource _newsImagesCancellation = new();
    private CancellationTokenSource _articleImagesCancellation = new();
    private CancellationTokenSource _articlePresentationCancellation = new();
    private CancellationTokenSource _compactNewsTransitionCancellation = new();
    private CancellationTokenSource? _instanceVersionsCancellation;
    private DateTime? _gameSessionStartedAtUtc;
    private string? _gameSessionInstanceId;
    private string? _modsLoadedForInstanceId;
    private string? _worldsLoadedForInstanceId;
    private bool _hasLoadedNews;
    private bool _canLoadMoreNews = true;
    private readonly bool _isOfficialProfile;
    private int _articleLoadVersion;
    private long _compactNewsTransitionReadyAt;
    private SettingsViewModel _settings;

    [ObservableProperty]
    private string _currentPage = DashboardPage;

    [ObservableProperty]
    private string _currentPageTitle = string.Empty;

    [ObservableProperty]
    private string _userName = "HyPrism";

    [ObservableProperty]
    private string _userInitial = "H";

    [ObservableProperty]
    private string _accountType = "Offline Account";

    [ObservableProperty]
    private string _selectedInstanceName = string.Empty;

    [ObservableProperty]
    private string _selectedInstanceMeta = string.Empty;

    [ObservableProperty]
    private string _selectedInstanceState = string.Empty;

    [ObservableProperty]
    private string _selectedInstanceBranch = string.Empty;

    [ObservableProperty]
    private string _selectedInstanceVersion = string.Empty;

    [ObservableProperty]
    private string _selectedInstancePlayTime = string.Empty;

    [ObservableProperty]
    private string _managedInstanceName = string.Empty;

    [ObservableProperty]
    private string _managedInstanceState = string.Empty;

    [ObservableProperty]
    private string _managedInstanceBranch = string.Empty;

    [ObservableProperty]
    private string _managedInstanceVersion = string.Empty;

    [ObservableProperty]
    private string _managedInstancePlayTime = string.Empty;

    [ObservableProperty]
    private Bitmap? _dashboardBackground;

    [ObservableProperty]
    private string _primaryActionText = string.Empty;

    [ObservableProperty]
    private bool _canRunPrimaryAction;

    [ObservableProperty]
    private bool _canCancelActivity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunManagedInstanceAction))]
    [NotifyPropertyChangedFor(nameof(CanDeleteManagedInstance))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunManagedInstanceAction))]
    [NotifyPropertyChangedFor(nameof(CanDeleteManagedInstance))]
    private bool _isGameRunning;

    [ObservableProperty]
    private bool _isActivityVisible;

    [ObservableProperty]
    private double _activityProgress;

    [ObservableProperty]
    private string _activityProgressText = "0%";

    [ObservableProperty]
    private string _activityTitle = string.Empty;

    [ObservableProperty]
    private string _activityDetail = string.Empty;

    [ObservableProperty]
    private NewsItemViewModel? _featuredNews;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedNewsItem))]
    [NotifyPropertyChangedFor(nameof(IsNewsFeedVisible))]
    [NotifyPropertyChangedFor(nameof(IsNewsArticleContext))]
    [NotifyPropertyChangedFor(nameof(IsNewsArticleEmpty))]
    private NewsItemViewModel? _selectedNewsItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewsArticleVisible))]
    [NotifyPropertyChangedFor(nameof(IsNewsLandingVisible))]
    private NewsArticleViewModel? _selectedNewsArticle;

    [ObservableProperty]
    private bool _isNewsArticleBodyVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewsLandingVisible))]
    [NotifyPropertyChangedFor(nameof(IsNewsArticleStatusVisible))]
    private bool _isNewsArticleLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewsArticleStatusVisible))]
    private bool _isNewsArticleSkeletonVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewsArticleError))]
    [NotifyPropertyChangedFor(nameof(IsNewsLandingVisible))]
    [NotifyPropertyChangedFor(nameof(IsNewsArticleStatusVisible))]
    private string _newsArticleError = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewsReady))]
    private bool _isNewsLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewsError))]
    [NotifyPropertyChangedFor(nameof(IsNewsReady))]
    private string _newsError = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCompactNewsLayout))]
    private bool _isWideNewsLayout;

    [ObservableProperty]
    private bool _isCompactNewsTransitionActive;

    [ObservableProperty]
    private bool _isNewsArticleScrolled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMoreNews))]
    [NotifyPropertyChangedFor(nameof(CanShowLoadMore))]
    private bool _isLoadingMoreNews;

    [ObservableProperty]
    private int _compactNewsPageIndex;

    [ObservableProperty]
    private bool _isCompactDashboardLayout;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreateReleaseBranch))]
    [NotifyPropertyChangedFor(nameof(IsCreatePreReleaseBranch))]
    private string _newInstanceBranch = "release";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateInstance))]
    private bool _isInstanceVersionsLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateInstance))]
    private InstanceVersionItemViewModel? _selectedNewInstanceVersion;

    [ObservableProperty]
    private bool _isInstanceCreatorOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstanceCreationError))]
    private string _instanceCreationError = string.Empty;

    [ObservableProperty]
    private string _displayedInstanceSectionTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstanceOverviewSection))]
    [NotifyPropertyChangedFor(nameof(IsInstanceModsSection))]
    [NotifyPropertyChangedFor(nameof(IsInstanceBrowseSection))]
    [NotifyPropertyChangedFor(nameof(IsInstanceWorldsSection))]
    [NotifyPropertyChangedFor(nameof(IsInstanceConsoleSection))]
    [NotifyPropertyChangedFor(nameof(IsInstanceLogsSection))]
    [NotifyPropertyChangedFor(nameof(InstanceSectionTitle))]
    private string _instanceSection = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalledModsEmpty))]
    private bool _isInstanceModsLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModCatalogEmpty))]
    private bool _isModCatalogLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstanceWorldsEmpty))]
    private bool _isInstanceWorldsLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstanceContentError))]
    private string _instanceContentError = string.Empty;

    [ObservableProperty]
    private string _installedModsSearchQuery = string.Empty;

    [ObservableProperty]
    private string _modCatalogSearchQuery = string.Empty;

    public MainWindowViewModel(
        IInstanceRepository instances,
        IProfileManager profiles,
        IProfileRepository profileRepository,
        IGameLaunchCoordinator gameLaunchCoordinator,
        IGameInstallationWorkflow installationWorkflow,
        IGameProcessTracker gameProcess,
        IProgressReporter progress,
        IDesktopSettingsStore settingsStore,
        IHytaleNewsClient newsClient,
        IExternalUriLauncher uriLauncher,
        HttpClient httpClient,
        StringLocalizer localizer,
        IFilePicker? filePicker = null,
        IGitHubClient? gitHubClient = null,
        IMirrorCatalog? mirrorCatalog = null,
        IMirrorDiscovery? mirrorDiscovery = null,
        IGameVersionCatalog? versionCatalog = null,
        IModManager? modManager = null)
    {
        _instances = instances;
        _gameLaunchCoordinator = gameLaunchCoordinator;
        _installationWorkflow = installationWorkflow;
        _gameProcess = gameProcess;
        _progress = progress;
        _settingsStore = settingsStore;
        _newsClient = newsClient;
        _uriLauncher = uriLauncher;
        _filePicker = filePicker;
        _gitHubClient = gitHubClient;
        _mirrorCatalog = mirrorCatalog;
        _mirrorDiscovery = mirrorDiscovery;
        _versionCatalog = versionCatalog;
        _modManager = modManager;
        _httpClient = httpClient;
        _localizer = localizer;
        _localizer.LanguageChanged += ApplyLanguage;
        _settingsStore.BackgroundChanged += OnBackgroundChanged;
        _settings = CreateSettingsViewModel();
        DashboardBackground = LoadDashboardBackground(_settingsStore.BackgroundMode);

        UserName = profiles.GetNick();
        UserInitial = string.IsNullOrWhiteSpace(UserName)
            ? "H"
            : UserName[..1].ToUpperInvariant();
        _isOfficialProfile = profileRepository.GetSelectedProfile()?.IsOfficial == true;
        AccountType = _isOfficialProfile
            ? _localizer["desktopSettings.accountHytale"]
            : _localizer["desktopSettings.accountOffline"];

        _progress.DownloadProgressChanged += OnDownloadProgressChanged;
        _progress.GameStateChanged += OnGameStateChanged;
        _progress.ErrorOccurred += OnErrorOccurred;

        CurrentPageTitle = DashboardLabel;
        RefreshInstances();
        RefreshManagedInstanceContent();
    }

    public ObservableCollection<InstanceItemViewModel> AllInstances { get; } = [];
    public ObservableCollection<InstanceVersionItemViewModel> AvailableInstanceVersions { get; } = [];
    public ObservableCollection<InstanceModItemViewModel> InstalledMods { get; } = [];
    public ObservableCollection<InstanceModItemViewModel> VisibleInstalledMods { get; } = [];
    public ObservableCollection<ModCatalogItemViewModel> ModCatalogItems { get; } = [];
    public ObservableCollection<InstanceWorldItemViewModel> InstanceWorlds { get; } = [];
    public ObservableCollection<NewsItemViewModel> LatestNews { get; } = [];
    public SettingsViewModel Settings
    {
        get => _settings;
        private set => SetProperty(ref _settings, value);
    }

    public string DashboardLabel => _localizer["dock.dashboard"];
    public string InstancesLabel => _localizer["dock.instances"];
    public string NewsLabel => _localizer["dock.news"];
    public string ProfilesLabel => _localizer["dock.profiles"];
    public string SettingsLabel => _localizer["dock.settings"];
    public string SelectInstanceLabel => _localizer["main.selectInstance"];
    public string VersionLabel => _localizer["common.version"];
    public string BranchLabel => _localizer["common.branch"];
    public string ReleaseLabel => _localizer["common.release"];
    public string PreReleaseLabel => _localizer["common.preRelease"];
    public string InstancesSectionLabel => _localizer["instances.title"];
    public string SelectVersionLabel => _localizer["instances.selectVersion"];
    public string CreateInstanceLabel => _localizer["instances.create"];
    public string CreateInstanceTitle => _localizer["instances.createInstance"];
    public string NewInstanceTitle => _localizer["instances.newInstance"];
    public string NewInstanceHint => _localizer["instances.newInstanceHint"];
    public string CreateInstanceHint => _localizer["instances.createInstanceHint"];
    public string InstanceBranchHint => _localizer["instances.branchHint"];
    public string InstanceVersionHint => _localizer["instances.versionHint"];
    public string CancelLabel => _localizer["common.cancel"];
    public string NewsLoadingLabel => _localizer["news.loading"];
    public string NewsEmptyLabel => _localizer["news.noNewsFound"];
    public string BackLabel => _localizer["common.back"];
    public string OpenOriginalLabel => _localizer["news.readMore"];
    public string ArticleLoadingLabel => _localizer["news.articleLoading"];
    public string SelectArticleLabel => _localizer["news.selectArticle"];
    public string LoadMoreLabel => _localizer["news.loadMore"];
    public string HomeWelcomeTitle => _localizer["home.welcomeTitle"];
    public string HomeWelcomeHint => _localizer["home.welcomeHint"];
    public string HomeCurrentInstanceLabel => _localizer["home.currentInstance"];
    public string HomeCreateInstanceLabel => _localizer["home.createInstance"];
    public string InstanceModsLabel => _localizer["instances.tab.mods"];
    public string InstanceBrowseLabel => _localizer["instances.tab.browse"];
    public string InstanceWorldsLabel => _localizer["instances.tab.worlds"];
    public string InstanceModsTitle => _localizer["instances.mods.title"];
    public string InstanceModsHint => _localizer["instances.mods.hint"];
    public string InstanceModsSearchHint => _localizer["instances.mods.search"];
    public string InstanceModsEmptyTitle => _localizer["instances.mods.emptyTitle"];
    public string InstanceModsEmptyHint => _localizer["instances.mods.emptyHint"];
    public string InstanceBrowseTitle => _localizer["instances.browse.title"];
    public string InstanceBrowseHint => _localizer["instances.browse.hint"];
    public string InstanceBrowseSearchHint => _localizer["instances.browse.search"];
    public string InstanceBrowseEmptyTitle => _localizer["instances.browse.emptyTitle"];
    public string InstanceBrowseEmptyHint => _localizer["instances.browse.emptyHint"];
    public string InstanceWorldsTitle => _localizer["instances.worlds.title"];
    public string InstanceWorldsHint => _localizer["instances.worlds.hint"];
    public string InstanceWorldsEmptyTitle => _localizer["instances.worlds.emptyTitle"];
    public string InstanceWorldsEmptyHint => _localizer["instances.worlds.emptyHint"];
    public string InstanceConsoleTitle => _localizer["instances.console.title"];
    public string InstanceConsoleHint => _localizer["instances.console.hint"];
    public string InstanceConsoleEmptyTitle => _localizer["instances.console.emptyTitle"];
    public string InstanceConsoleEmptyHint => _localizer["instances.console.emptyHint"];
    public string InstanceLogsTitle => _localizer["instances.logs.title"];
    public string InstanceLogsHint => _localizer["instances.logs.hint"];
    public string InstanceLogsEmptyTitle => _localizer["instances.logs.emptyTitle"];
    public string InstanceLogsEmptyHint => _localizer["instances.logs.emptyHint"];
    public string InstanceNotInstalledTitle => _localizer["instances.content.notInstalledTitle"];
    public string InstanceNotInstalledHint => _localizer["instances.content.notInstalledHint"];
    public string RefreshLabel => _localizer["common.refresh"];
    public string InstallLabel => _localizer["instances.mods.install"];
    public string InstalledLabel => _localizer["instances.mods.installed"];
    public string EnabledLabel => _localizer["instances.mods.enabled"];
    public string DisabledLabel => _localizer["instances.mods.disabled"];
    public string InstanceContentBackLabel => _localizer["instances.content.back"];
    public string ManagedInstancePlayLabel => _localizer["instances.actions.play"];
    public string ManagedInstanceInstallLabel => _localizer["instances.actions.install"];
    public string ManagedInstanceOpenFolderLabel => _localizer["instances.actions.openFolder"];
    public string ManagedInstanceDeleteLabel => _localizer["instances.actions.delete"];
    public string ManagedInstanceDeleteTitle => _localizer["instances.actions.deleteTitle"];
    public string ManagedInstanceDeleteHint =>
        _localizer.Format("instances.actions.deleteHint", ManagedInstanceName);
    public string ManagedInstanceActionLabel =>
        IsManagedInstanceInstalled ? ManagedInstancePlayLabel : ManagedInstanceInstallLabel;
    public string InstanceStatusInfoLabel => _localizer["instances.info.status"];
    public string InstancePlayTimeInfoLabel => _localizer["instances.info.playtime"];
    public string InstanceModsInfoLabel => _localizer["instances.info.mods"];
    public string InstanceWorldsInfoLabel => _localizer["instances.info.worlds"];
    public string InstanceModsCountText => InstalledMods.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
    public string InstanceWorldsCountText => InstanceWorlds.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
    public string InstanceSectionTitle => GetInstanceSectionTitle(InstanceSection);

    private string GetInstanceSectionTitle(string section) => section switch
    {
        "mods" => InstanceModsTitle,
        "browse" => InstanceBrowseTitle,
        "worlds" => InstanceWorldsTitle,
        "console" => InstanceConsoleTitle,
        "logs" => InstanceLogsTitle,
        _ => ManagedInstanceName
    };

    public bool IsDashboard => CurrentPage == DashboardPage;
    public bool IsInstances => CurrentPage == InstancesPage;
    public bool IsNews => CurrentPage == NewsPage;
    public bool IsProfiles => CurrentPage == ProfilesPage;
    public bool IsSettings => CurrentPage == SettingsPage;
    public bool IsPlaceholderPage => !IsDashboard && !IsInstances && !IsNews && !IsSettings;
    public bool HasInstances => AllInstances.Count > 0;
    public bool HasSelectedInstance => _selectedInstance is not null;
    public bool HasManagedInstance => _managedInstance is not null;
    public bool HasAvailableInstanceVersions => AvailableInstanceVersions.Count > 0;
    public bool HasInstanceCreationError => !string.IsNullOrWhiteSpace(InstanceCreationError);
    public bool HasInstanceContentError => !string.IsNullOrWhiteSpace(InstanceContentError);
    public bool IsSelectedInstanceInstalled => _selectedInstance?.IsInstalled == true;
    public bool IsManagedInstanceInstalled => _managedInstance?.IsInstalled == true;
    public bool CanRunManagedInstanceAction =>
        _managedInstance is not null && !IsBusy && !IsGameRunning;
    public bool CanOpenManagedInstanceFolder => _managedInstance is not null;
    public bool CanDeleteManagedInstance =>
        _managedInstance is not null && !IsBusy && !IsGameRunning;
    public bool IsInstanceOverviewSection => string.IsNullOrEmpty(InstanceSection);
    public bool IsInstanceModsSection => InstanceSection == "mods";
    public bool IsInstanceBrowseSection => InstanceSection == "browse";
    public bool IsInstanceWorldsSection => InstanceSection == "worlds";
    public bool IsInstanceConsoleSection => InstanceSection == "console";
    public bool IsInstanceLogsSection => InstanceSection == "logs";
    public bool HasInstalledMods => VisibleInstalledMods.Count > 0;
    public bool HasModCatalogItems => ModCatalogItems.Count > 0;
    public bool HasInstanceWorlds => InstanceWorlds.Count > 0;
    public bool IsInstalledModsEmpty =>
        IsManagedInstanceInstalled && !IsInstanceModsLoading && !HasInstalledMods;
    public bool IsModCatalogEmpty =>
        IsManagedInstanceInstalled && !IsModCatalogLoading && !HasModCatalogItems;
    public bool IsInstanceWorldsEmpty =>
        IsManagedInstanceInstalled && !IsInstanceWorldsLoading && !HasInstanceWorlds;
    public bool CanCreateInstance => !IsInstanceVersionsLoading && SelectedNewInstanceVersion is not null;
    public bool IsCreateReleaseBranch =>
        string.Equals(NewInstanceBranch, "release", StringComparison.OrdinalIgnoreCase);
    public bool IsCreatePreReleaseBranch => !IsCreateReleaseBranch;
    public bool IsGlobalActivityVisible => IsActivityVisible && !IsDashboard;
    public bool IsPrimarySelectAction => _selectedInstance is null;
    public bool IsPrimaryStopAction => IsGameRunning || (IsBusy && CanCancelActivity);
    public bool IsPrimaryDownloadAction =>
        !IsBusy && !IsGameRunning && _selectedInstance is { IsInstalled: false };
    public bool IsPrimaryPlayAction =>
        !IsBusy && !IsGameRunning && _selectedInstance is { IsInstalled: true };
    public bool IsPrimaryPendingAction => IsBusy && !CanCancelActivity;
    public bool HasFeaturedNews => FeaturedNews is not null;
    public bool HasNewsError => !string.IsNullOrWhiteSpace(NewsError);
    public bool IsNewsReady => !IsNewsLoading && !HasNewsError;
    public bool IsNewsEmpty => IsNewsReady && FeaturedNews is null;
    public bool HasSelectedNewsItem => SelectedNewsItem is not null;
    public bool IsNewsFeedVisible => IsNews && SelectedNewsItem is null;
    public bool IsNewsArticleContext => IsNews && HasSelectedNewsItem;
    public bool IsNewsArticleEmpty => IsNews && SelectedNewsItem is null;
    public bool HasNewsArticleError => !string.IsNullOrWhiteSpace(NewsArticleError);
    public bool IsNewsLandingVisible =>
        IsNews && HasFeaturedNews && !IsNewsLoading && !HasNewsError;
    public bool IsNewsArticleVisible => IsNews && SelectedNewsArticle is not null;
    public bool IsNewsArticleStatusVisible =>
        IsNews && SelectedNewsArticle is null && (IsNewsArticleSkeletonVisible || HasNewsArticleError);
    public bool IsCompactNewsLayout => !IsWideNewsLayout;
    public bool HasMoreNews => _canLoadMoreNews && !IsLoadingMoreNews && _allNews.Count < MaximumNewsCount;
    public bool CanShowLoadMore => _canLoadMoreNews && _allNews.Count < MaximumNewsCount;

    public void MoveInstance(string instanceId, int targetIndex)
    {
        var sourceIndex = -1;
        for (var index = 0; index < AllInstances.Count; index++)
        {
            if (string.Equals(AllInstances[index].Id, instanceId, StringComparison.Ordinal))
            {
                sourceIndex = index;
                break;
            }
        }

        if (sourceIndex < 0 || AllInstances.Count < 2)
            return;

        targetIndex = Math.Clamp(targetIndex, 0, AllInstances.Count - 1);
        if (sourceIndex == targetIndex)
            return;

        AllInstances.Move(sourceIndex, targetIndex);
        _instances.SetInstanceOrder(AllInstances.Select(instance => instance.Id).ToArray());
    }

    [RelayCommand]
    private void Navigate(string? page)
    {
        CurrentPage = page switch
        {
            InstancesPage => InstancesPage,
            NewsPage => NewsPage,
            ProfilesPage => ProfilesPage,
            SettingsPage => SettingsPage,
            _ => DashboardPage
        };

        CurrentPageTitle = CurrentPage switch
        {
            InstancesPage => InstancesLabel,
            NewsPage => NewsLabel,
            ProfilesPage => ProfilesLabel,
            SettingsPage => SettingsLabel,
            _ => DashboardLabel
        };

        NotifyPageStateChanged();

        if (IsNews)
            _ = LoadNewsAsync();
    }

    [RelayCommand]
    private void OpenInstanceCreator()
    {
        IsInstanceCreatorOpen = true;
        InstanceCreationError = string.Empty;
        _ = LoadInstanceVersionsAsync(NewInstanceBranch);
    }

    [RelayCommand]
    private void CloseInstanceCreator()
    {
        IsInstanceCreatorOpen = false;
        InstanceCreationError = string.Empty;
    }

    [RelayCommand]
    private void SetNewInstanceBranch(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch) ||
            string.Equals(NewInstanceBranch, branch, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        NewInstanceBranch = branch;
        InstanceCreationError = string.Empty;
        _ = LoadInstanceVersionsAsync(branch);
    }

    [RelayCommand]
    private void SelectNewInstanceVersion(InstanceVersionItemViewModel? version)
    {
        if (version is null || version.Version == SelectedNewInstanceVersion?.Version)
            return;

        SelectedNewInstanceVersion = version;
        RefreshAvailableInstanceVersionSelection(version.Version);
    }

    [RelayCommand]
    private void CreateInstance()
    {
        if (IsInstanceVersionsLoading || SelectedNewInstanceVersion is null)
            return;

        try
        {
            var version = SelectedNewInstanceVersion.Version;
            var instance = _instances.CreateInstanceMeta(
                NewInstanceBranch,
                version,
                $"{FormatBranch(NewInstanceBranch)} {FormatVersion(version)}");
            _managedInstance = _instances.FindInstanceById(instance.Id);
            IsInstanceCreatorOpen = false;
            InstanceSection = string.Empty;
            RefreshInstances();
            RefreshManagedInstanceContent();
        }
        catch (Exception ex)
        {
            InstanceCreationError = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenInstanceDetails(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId) ||
            string.Equals(_managedInstance?.Id, instanceId, StringComparison.Ordinal))
        {
            return;
        }

        var instance = _instances.GetCachedInstances()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Id, instanceId, StringComparison.Ordinal));
        if (instance is null)
            return;

        RefreshInstanceInstalledState(instance);
        _managedInstance = instance;
        InstanceSection = string.Empty;
        UpdateManagedInstancePresentation();
        RefreshManagedInstanceContent();
    }

    [RelayCommand]
    private void SelectInstanceSection(string? section)
    {
        if (section is not ("mods" or "browse" or "worlds" or "console" or "logs"))
            return;

        DisplayedInstanceSectionTitle = GetInstanceSectionTitle(section);
        InstanceSection = section;
        InstanceContentError = string.Empty;

        if (section == "mods" && !IsInstanceModsLoading &&
            !string.Equals(_modsLoadedForInstanceId, _managedInstance?.Id, StringComparison.Ordinal))
            _ = LoadInstalledModsAsync();
        else if (section == "browse" && ModCatalogItems.Count == 0)
            _ = SearchModCatalogAsync();
        else if (section == "worlds" && !IsInstanceWorldsLoading &&
                 !string.Equals(_worldsLoadedForInstanceId, _managedInstance?.Id, StringComparison.Ordinal))
            _ = LoadInstanceWorldsAsync();
    }

    [RelayCommand]
    private void CloseInstanceSection()
    {
        InstanceSection = string.Empty;
        InstanceContentError = string.Empty;
    }

    [RelayCommand]
    private Task RefreshInstanceModsAsync()
        => LoadInstalledModsAsync();

    [RelayCommand]
    private Task SearchModCatalogAsync()
        => LoadModCatalogAsync(ModCatalogSearchQuery);

    [RelayCommand]
    private Task RefreshInstanceWorldsAsync()
        => LoadInstanceWorldsAsync();

    [RelayCommand]
    private async Task InstallModAsync(ModCatalogItemViewModel? item)
    {
        if (item is null || item.IsInstalling || item.IsInstalled ||
            _modManager is null || _managedInstance?.IsInstalled != true)
        {
            return;
        }

        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        item.IsInstalling = true;
        InstanceContentError = string.Empty;
        try
        {
            var installed = await _modManager.InstallModFileToInstanceAsync(
                item.Id,
                item.LatestFileId,
                instancePath);
            if (!installed)
            {
                InstanceContentError = _localizer["instances.mods.installFailed"];
                return;
            }

            item.IsInstalled = true;
            await LoadInstalledModsAsync();
        }
        catch (Exception ex)
        {
            InstanceContentError = ex.Message;
        }
        finally
        {
            item.IsInstalling = false;
        }
    }

    [RelayCommand]
    private async Task RunManagedInstanceAsync()
    {
        if (_managedInstance is not { } instance || !CanRunManagedInstanceAction)
            return;

        IsBusy = true;
        CanCancelActivity = !instance.IsInstalled;
        IsActivityVisible = true;
        ActivityProgress = 0;
        ActivityProgressText = "0%";
        ActivityTitle = _localizer["common.loading"];
        ActivityDetail = instance.Name;
        _gameSessionInstanceId = instance.Id;

        try
        {
            if (instance.IsInstalled)
            {
                await _gameLaunchCoordinator.LaunchAsync(
                    instance.Id,
                    authorizationUriPresenter: _uriLauncher.LaunchAsync);
            }
            else
            {
                var result = await _installationWorkflow.DownloadAndLaunchInstanceAsync(
                    instance.Id,
                    _uriLauncher.LaunchAsync);
                if (!result.Success && !result.Cancelled && !string.IsNullOrWhiteSpace(result.Error))
                    ShowError(result.Error);
            }
        }
        finally
        {
            IsBusy = false;
            CanCancelActivity = false;
            RefreshInstances();
        }
    }

    [RelayCommand]
    private async Task OpenManagedInstanceFolderAsync()
    {
        if (_managedInstance is null)
            return;

        var path = _instances.GetInstancePathById(_managedInstance.Id);
        if (!string.IsNullOrWhiteSpace(path))
            await _uriLauncher.LaunchDirectoryAsync(path);
    }

    [RelayCommand]
    private void DeleteManagedInstance()
    {
        if (_managedInstance is not { } instance || !CanDeleteManagedInstance)
            return;

        if (!_instances.DeleteGameById(instance.Id))
        {
            ShowError(_localizer["instances.deleteFailed"]);
            return;
        }

        _managedInstance = null;
        InstanceSection = string.Empty;
        RefreshInstances();
        RefreshManagedInstanceContent();
    }

    [RelayCommand]
    private async Task PrimaryActionAsync()
    {
        if (_selectedInstance is null)
        {
            Navigate(InstancesPage);
            return;
        }

        if (IsGameRunning)
        {
            _gameProcess.ExitGame();
            return;
        }

        if (IsBusy)
        {
            if (CanCancelActivity)
                CancelActivity();

            return;
        }

        IsBusy = true;
        CanCancelActivity = !_selectedInstance.IsInstalled;
        IsActivityVisible = true;
        ActivityProgress = 0;
        ActivityProgressText = "0%";
        ActivityTitle = _localizer["common.loading"];
        ActivityDetail = _selectedInstance.Name;
        _gameSessionInstanceId = _selectedInstance.Id;
        UpdateSelectedInstancePresentation();

        try
        {
            if (_selectedInstance.IsInstalled)
            {
                await _gameLaunchCoordinator.LaunchAsync(
                    _selectedInstance.Id,
                    authorizationUriPresenter: _uriLauncher.LaunchAsync);
            }
            else
            {
                _instances.SetSelectedInstance(_selectedInstance.Id);
                var result = await _installationWorkflow.DownloadAndLaunchAsync(_uriLauncher.LaunchAsync);

                if (!result.Success && !result.Cancelled && !string.IsNullOrWhiteSpace(result.Error))
                    ShowError(result.Error);
            }
        }
        finally
        {
            IsBusy = false;
            CanCancelActivity = false;
            RefreshInstances();
        }
    }

    [RelayCommand]
    private void CancelActivity()
        => _installationWorkflow.CancelDownload();

    private void RefreshInstances()
    {
        try
        {
            _instances.SyncInstancesWithConfig();
            var items = _instances.GetCachedInstances();
            var selectedInstanceId = _instances.GetSelectedInstance()?.Id;
            var managedInstanceId = _managedInstance?.Id;

            AllInstances.Clear();
            var presentedInstances = items
                .Select(instance =>
                {
                    RefreshInstanceInstalledState(instance);

                    return new InstanceItemViewModel(
                        instance.Id,
                        instance.Name,
                        FormatVersion(instance.Version),
                        FormatBranch(instance.Branch),
                        instance.IsInstalled);
                })
                .ToList();

            foreach (var item in presentedInstances)
            {
                AllInstances.Add(item);
            }

            _selectedInstance = items.FirstOrDefault(instance =>
                string.Equals(instance.Id, selectedInstanceId, StringComparison.Ordinal));
            _managedInstance = items.FirstOrDefault(instance =>
                    string.Equals(instance.Id, managedInstanceId, StringComparison.Ordinal))
                ?? items.FirstOrDefault();

            OnPropertyChanged(nameof(HasInstances));
            OnPropertyChanged(nameof(HasSelectedInstance));
            OnPropertyChanged(nameof(HasManagedInstance));

            if (_selectedInstance is not null)
                RefreshInstanceInstalledState(_selectedInstance);

            if (_managedInstance is not null)
                RefreshInstanceInstalledState(_managedInstance);

            UpdateSelectedInstancePresentation();
            UpdateManagedInstancePresentation();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void RefreshInstanceInstalledState(InstanceInfo instance)
    {
        var path = _instances.GetInstancePathById(instance.Id);
        instance.IsInstalled =
            !string.IsNullOrWhiteSpace(path) && _instances.IsClientPresent(path);
    }

    private void RefreshManagedInstanceContent()
    {
        InstalledMods.Clear();
        VisibleInstalledMods.Clear();
        ModCatalogItems.Clear();
        InstanceWorlds.Clear();
        _modsLoadedForInstanceId = null;
        _worldsLoadedForInstanceId = null;
        InstanceContentError = string.Empty;
        NotifyInstanceContentCollectionsChanged();

        if (_managedInstance?.IsInstalled != true)
            return;

        _ = LoadInstalledModsAsync();
        _ = LoadInstanceWorldsAsync();

        if (IsInstanceBrowseSection)
            _ = SearchModCatalogAsync();
    }

    private async Task LoadInstalledModsAsync()
    {
        if (_modManager is null || _managedInstance?.IsInstalled != true)
            return;

        var instanceId = _managedInstance.Id;
        var instancePath = _instances.GetInstancePathById(instanceId);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        IsInstanceModsLoading = true;
        InstanceContentError = string.Empty;
        try
        {
            var mods = await Task.Run(() => _modManager.GetInstanceInstalledMods(instancePath));
            if (!string.Equals(_managedInstance?.Id, instanceId, StringComparison.Ordinal))
                return;

            InstalledMods.Clear();
            foreach (var mod in mods.OrderBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                InstalledMods.Add(new InstanceModItemViewModel(
                    mod.Id,
                    mod.Name,
                    string.IsNullOrWhiteSpace(mod.Version) ? _localizer["common.unknown"] : mod.Version,
                    string.IsNullOrWhiteSpace(mod.Author) ? _localizer["common.unknown"] : mod.Author,
                    mod.Enabled));
            }

            FilterInstalledMods();
            RefreshCatalogInstalledState(mods);
            _modsLoadedForInstanceId = instanceId;
        }
        catch (Exception ex)
        {
            InstanceContentError = ex.Message;
        }
        finally
        {
            IsInstanceModsLoading = false;
        }
    }

    private async Task LoadModCatalogAsync(string query)
    {
        if (_modManager is null || _managedInstance?.IsInstalled != true)
            return;

        var instanceId = _managedInstance.Id;
        IsModCatalogLoading = true;
        InstanceContentError = string.Empty;
        try
        {
            var result = await _modManager.SearchModsAsync(query.Trim(), 0, 24, [], 2, 1);
            if (!string.Equals(_managedInstance?.Id, instanceId, StringComparison.Ordinal))
                return;

            var instancePath = _instances.GetInstancePathById(instanceId);
            var installed = string.IsNullOrWhiteSpace(instancePath)
                ? []
                : _modManager.GetInstanceInstalledMods(instancePath);
            ModCatalogItems.Clear();
            foreach (var mod in result.Mods)
            {
                ModCatalogItems.Add(new ModCatalogItemViewModel(
                    mod.Id,
                    mod.Name,
                    string.IsNullOrWhiteSpace(mod.Author) ? _localizer["common.unknown"] : mod.Author,
                    mod.Summary,
                    mod.LatestFileId)
                {
                    IsInstalled = IsCatalogModInstalled(mod.Id, installed)
                });
            }

            NotifyInstanceContentCollectionsChanged();
        }
        catch (Exception ex)
        {
            InstanceContentError = ex.Message;
        }
        finally
        {
            IsModCatalogLoading = false;
        }
    }

    private void FilterInstalledMods()
    {
        var query = InstalledModsSearchQuery.Trim();
        VisibleInstalledMods.Clear();
        foreach (var mod in InstalledMods.Where(mod =>
                     string.IsNullOrWhiteSpace(query) ||
                     mod.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                     mod.Author.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
        {
            VisibleInstalledMods.Add(mod);
        }

        NotifyInstanceContentCollectionsChanged();
    }

    private async Task LoadInstanceWorldsAsync()
    {
        if (_managedInstance?.IsInstalled != true)
            return;

        var instanceId = _managedInstance.Id;
        var instancePath = _instances.GetInstancePathById(instanceId);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        IsInstanceWorldsLoading = true;
        InstanceContentError = string.Empty;
        try
        {
            var worlds = await Task.Run(() => ReadInstanceWorlds(instancePath));
            if (!string.Equals(_managedInstance?.Id, instanceId, StringComparison.Ordinal))
                return;

            InstanceWorlds.Clear();
            foreach (var world in worlds)
                InstanceWorlds.Add(world);
            _worldsLoadedForInstanceId = instanceId;
            NotifyInstanceContentCollectionsChanged();
        }
        catch (Exception ex)
        {
            InstanceContentError = ex.Message;
        }
        finally
        {
            IsInstanceWorldsLoading = false;
        }
    }

    private IReadOnlyList<InstanceWorldItemViewModel> ReadInstanceWorlds(string instancePath)
    {
        var savesPath = Path.Combine(instancePath, "UserData", "Saves");
        if (!Directory.Exists(savesPath))
            return [];

        return Directory.EnumerateDirectories(savesPath)
            .Select(path => new DirectoryInfo(path))
            .OrderByDescending(directory => directory.LastWriteTimeUtc)
            .Select(directory => new InstanceWorldItemViewModel(
                directory.Name,
                directory.LastWriteTime.ToString("d", System.Globalization.CultureInfo.CurrentCulture),
                FormatBytes(GetDirectorySize(directory))))
            .ToList();
    }

    private static long GetDirectorySize(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = Math.Max(0, bytes);
        var unitIndex = 0;
        var displayValue = (double)value;
        while (displayValue >= 1024 && unitIndex < units.Length - 1)
        {
            displayValue /= 1024;
            unitIndex++;
        }

        return $"{displayValue:0.#} {units[unitIndex]}";
    }

    private void RefreshCatalogInstalledState(IReadOnlyCollection<InstalledMod> installedMods)
    {
        foreach (var item in ModCatalogItems)
            item.IsInstalled = IsCatalogModInstalled(item.Id, installedMods);
    }

    private static bool IsCatalogModInstalled(string catalogId, IEnumerable<InstalledMod> installedMods)
        => installedMods.Any(mod =>
            string.Equals(mod.CurseForgeId, catalogId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mod.Id, catalogId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mod.Id, $"cf-{catalogId}", StringComparison.OrdinalIgnoreCase));

    private void NotifyInstanceContentCollectionsChanged()
    {
        OnPropertyChanged(nameof(InstanceModsCountText));
        OnPropertyChanged(nameof(InstanceWorldsCountText));
        OnPropertyChanged(nameof(HasInstalledMods));
        OnPropertyChanged(nameof(HasModCatalogItems));
        OnPropertyChanged(nameof(HasInstanceWorlds));
        OnPropertyChanged(nameof(IsInstalledModsEmpty));
        OnPropertyChanged(nameof(IsModCatalogEmpty));
        OnPropertyChanged(nameof(IsInstanceWorldsEmpty));
    }

    private async Task LoadInstanceVersionsAsync(string branch)
    {
        _instanceVersionsCancellation?.Cancel();
        _instanceVersionsCancellation?.Dispose();
        _instanceVersionsCancellation = null;

        if (_versionCatalog is not null &&
            _versionCatalog.TryGetCachedVersions(branch, InstanceVersionCacheMaxAge, out var cachedVersions))
        {
            IsInstanceVersionsLoading = false;
            ApplyAvailableInstanceVersions(cachedVersions);
            return;
        }

        _instanceVersionsCancellation = new CancellationTokenSource();
        var cancellationToken = _instanceVersionsCancellation.Token;

        IsInstanceVersionsLoading = true;
        SelectedNewInstanceVersion = null;
        AvailableInstanceVersions.Clear();

        try
        {
            if (_versionCatalog is null)
            {
                InstanceCreationError = "The version catalog is unavailable";
                return;
            }

            var versions = await _versionCatalog.GetVersionListAsync(branch, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return;

            ApplyAvailableInstanceVersions(versions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            InstanceCreationError = ex.Message;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsInstanceVersionsLoading = false;
                OnPropertyChanged(nameof(HasAvailableInstanceVersions));
            }
        }
    }

    private void ApplyAvailableInstanceVersions(IReadOnlyList<int> versions)
    {
        SelectedNewInstanceVersion = null;
        AvailableInstanceVersions.Clear();

        var selectedVersion = versions.FirstOrDefault();
        foreach (var version in versions.Take(12))
        {
            AvailableInstanceVersions.Add(new InstanceVersionItemViewModel(
                version,
                IsSelected: version == selectedVersion));
        }

        SelectedNewInstanceVersion = AvailableInstanceVersions.FirstOrDefault();
        OnPropertyChanged(nameof(HasAvailableInstanceVersions));
    }

    private void RefreshAvailableInstanceVersionSelection(int selectedVersion)
    {
        for (var index = 0; index < AvailableInstanceVersions.Count; index++)
        {
            var option = AvailableInstanceVersions[index];
            AvailableInstanceVersions[index] = option with { IsSelected = option.Version == selectedVersion };
        }

        SelectedNewInstanceVersion = AvailableInstanceVersions
            .FirstOrDefault(option => option.Version == selectedVersion);
    }

    private async Task LoadNewsAsync()
    {
        if (_hasLoadedNews || IsNewsLoading)
            return;

        IsNewsLoading = true;
        NewsError = string.Empty;

        try
        {
            var news = (await _newsClient.GetNewsAsync(InitialNewsCount))
                .Take(InitialNewsCount)
                .ToList();

            foreach (var item in _allNews)
                item.Dispose();
            _allNews.Clear();
            _allNews.AddRange(news.Select(item =>
                new NewsItemViewModel(item, _uriLauncher, OpenNewsArticleAsync)));
            _canLoadMoreNews = news.Count == InitialNewsCount;
            _hasLoadedNews = true;
            PresentNews();

            RestartNewsImageLoading();
        }
        catch (Exception ex)
        {
            NewsError = ex.Message;
            _canLoadMoreNews = false;
            FeaturedNews = null;
            LatestNews.Clear();
            NotifyNewsStateChanged();
        }
        finally
        {
            IsNewsLoading = false;
            NotifyNewsStateChanged();
        }
    }

    private void PresentNews()
    {
        FeaturedNews = _allNews.FirstOrDefault();
        LatestNews.Clear();
        foreach (var item in _allNews.Skip(1))
            LatestNews.Add(item);

        NotifyNewsStateChanged();
        OnPropertyChanged(nameof(HasMoreNews));
        OnPropertyChanged(nameof(CanShowLoadMore));
    }

    [RelayCommand]
    private async Task LoadMoreNewsAsync()
    {
        if (!HasMoreNews)
            return;

        IsLoadingMoreNews = true;
        try
        {
            var requestedCount = Math.Min(_allNews.Count + NewsPageSize, MaximumNewsCount);
            var response = (await _newsClient.GetNewsAsync(requestedCount))
                .Take(requestedCount)
                .ToList();
            var knownUrls = _allNews
                .Select(item => item.Url)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = response
                .Where(item => knownUrls.Add(item.Url))
                .Select(item => new NewsItemViewModel(item, _uriLauncher, OpenNewsArticleAsync))
                .ToArray();

            _allNews.AddRange(added);
            _canLoadMoreNews = response.Count == requestedCount &&
                               requestedCount < MaximumNewsCount &&
                               added.Length > 0;
            PresentNews();
            _ = LoadNewsImagesAsync(added, _newsImagesCancellation.Token);
        }
        catch (Exception ex)
        {
            NewsError = ex.Message;
        }
        finally
        {
            IsLoadingMoreNews = false;
            OnPropertyChanged(nameof(HasMoreNews));
            OnPropertyChanged(nameof(CanShowLoadMore));
        }
    }

    private async Task OpenNewsArticleAsync(NewsItemViewModel item)
    {
        if (ReferenceEquals(SelectedNewsItem, item))
            return;

        var loadVersion = ++_articleLoadVersion;
        _newsImagesCancellation.Cancel();
        _articleImagesCancellation.Cancel();
        _articlePresentationCancellation.Cancel();
        IsNewsArticleScrolled = false;

        foreach (var newsItem in _allNews)
            newsItem.IsSelected = ReferenceEquals(newsItem, item);
        SelectedNewsItem = item;
        NewsArticleError = string.Empty;
        IsNewsArticleSkeletonVisible = false;
        IsNewsArticleBodyVisible = !IsCompactNewsLayout;

        if (_articleViewModelCache.TryGetValue(item.Url, out var cachedArticle))
        {
            // Clear before exposing a cached model so Avalonia never realizes the full
            // rich tree synchronously during the SelectedNewsArticle binding change.
            cachedArticle.ResetRenderedBlocks();
            SelectedNewsArticle = cachedArticle;
            IsNewsArticleLoading = false;
            NotifyNewsStateChanged();
            StartCompactArticleTransition();
            await RestartArticlePresentationAsync(cachedArticle);
            return;
        }

        SelectedNewsArticle = null;
        IsNewsArticleLoading = true;
        NotifyNewsStateChanged();
        StartCompactArticleTransition();
        _ = ShowArticleSkeletonAfterDelayAsync(loadVersion);

        try
        {
            var article = await _newsClient.GetNewsArticleAsync(item.Url);
            if (loadVersion != _articleLoadVersion)
                return;

            if (article is null)
            {
                NewsArticleError = _localizer["news.articleLoadFailed"];
                return;
            }

            var articleViewModel = await Task.Run(
                () => new NewsArticleViewModel(article, _uriLauncher));
            if (loadVersion != _articleLoadVersion)
            {
                articleViewModel.Dispose();
                return;
            }

            _articleViewModelCache[item.Url] = articleViewModel;
            IsNewsArticleSkeletonVisible = false;
            SelectedNewsArticle = articleViewModel;
            await RestartArticlePresentationAsync(articleViewModel);
        }
        catch (ArgumentException ex)
        {
            NewsArticleError = ex.Message;
        }
        catch (Exception)
        {
            NewsArticleError = _localizer["news.articleLoadFailed"];
        }
        finally
        {
            if (loadVersion == _articleLoadVersion)
            {
                IsNewsArticleLoading = false;
                IsNewsArticleSkeletonVisible = false;
                NotifyNewsStateChanged();
            }
        }
    }

    private void StartCompactArticleTransition()
    {
        if (!IsCompactNewsLayout)
            return;

        // The reader bindings are fully updated before Carousel starts measuring the
        // incoming page, preventing article realization from stalling the first frame.
        BeginCompactNewsTransition();
        CompactNewsPageIndex = 1;
    }

    [RelayCommand]
    private async Task CloseNewsArticleAsync()
    {
        var closeVersion = ++_articleLoadVersion;
        _articleImagesCancellation.Cancel();
        _articlePresentationCancellation.Cancel();
        IsNewsArticleScrolled = false;
        foreach (var newsItem in _allNews)
            newsItem.IsSelected = false;
        CompactNewsPageIndex = 0;

        if (IsCompactNewsLayout)
        {
            BeginCompactNewsTransition();
            await Task.Delay(CompactTransitionMilliseconds);

            if (closeVersion != _articleLoadVersion)
                return;
        }

        SelectedNewsArticle = null;
        SelectedNewsItem = null;
        NewsArticleError = string.Empty;
        IsNewsArticleLoading = false;
        IsNewsArticleSkeletonVisible = false;
        NotifyNewsStateChanged();
        RestartNewsImageLoading();
    }

    private async Task ShowArticleSkeletonAfterDelayAsync(int loadVersion)
    {
        await Task.Delay(ArticleSkeletonDelayMilliseconds).ConfigureAwait(false);
        Dispatcher.UIThread.Post(() =>
        {
            if (loadVersion == _articleLoadVersion &&
                IsNewsArticleLoading &&
                SelectedNewsArticle is null)
            {
                IsNewsArticleSkeletonVisible = true;
            }
        });
    }

    private void BeginCompactNewsTransition()
    {
        _compactNewsTransitionCancellation.Cancel();
        _compactNewsTransitionCancellation.Dispose();
        _compactNewsTransitionCancellation = new CancellationTokenSource();
        _compactNewsTransitionReadyAt =
            Environment.TickCount64 + CompactTransitionMilliseconds + 34;
        IsCompactNewsTransitionActive = true;
        _ = CompleteCompactNewsTransitionAsync(_compactNewsTransitionCancellation.Token);
    }

    private async Task CompleteCompactNewsTransitionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(CompactTransitionMilliseconds + 60, cancellationToken).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() => IsCompactNewsTransitionActive = false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void RestartNewsImageLoading()
    {
        _newsImagesCancellation.Cancel();
        _newsImagesCancellation.Dispose();
        _newsImagesCancellation = new CancellationTokenSource();
        _ = LoadNewsImagesAsync(_allNews.ToArray(), _newsImagesCancellation.Token);
    }

    private void RestartArticleImageLoading(NewsArticleViewModel article)
    {
        _articleImagesCancellation.Cancel();
        _articleImagesCancellation.Dispose();
        _articleImagesCancellation = new CancellationTokenSource();
        _ = article.LoadImagesAsync(_httpClient, _articleImagesCancellation.Token);
    }

    private async Task RestartArticlePresentationAsync(NewsArticleViewModel article)
    {
        _articlePresentationCancellation.Cancel();
        _articlePresentationCancellation.Dispose();
        _articlePresentationCancellation = new CancellationTokenSource();
        var cancellationToken = _articlePresentationCancellation.Token;
        var readyAt = IsCompactNewsLayout
            ? _compactNewsTransitionReadyAt
            : 0;

        try
        {
            var remainingDelay = readyAt - Environment.TickCount64;
            if (remainingDelay > 0)
                await Task.Delay((int)remainingDelay, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(
                () => RestartArticleImageLoading(article),
                DispatcherPriority.Background,
                cancellationToken);
            await PrepareArticleForDisplayAsync(
                    article,
                    cancellationToken,
                    readyAt > 0 ? RevealNewsArticleBodyAsync : null)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task PrepareArticleForDisplayAsync(
        NewsArticleViewModel article,
        CancellationToken cancellationToken,
        Func<Task>? firstBatchReady)
    {
        try
        {
            await article.PrepareForDisplayAsync(cancellationToken, firstBatchReady)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RevealNewsArticleBodyAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(
            () => IsNewsArticleBodyVisible = true,
            DispatcherPriority.Render);
    }

    private async Task LoadNewsImagesAsync(
        IReadOnlyCollection<NewsItemViewModel> items,
        CancellationToken cancellationToken)
    {
        using var concurrencyGate = new SemaphoreSlim(4, 4);

        try
        {
            await Task.WhenAll(items.Select(async item =>
            {
                await concurrencyGate.WaitAsync(cancellationToken);
                try
                {
                    await item.LoadImageAsync(_httpClient, cancellationToken);
                }
                finally
                {
                    concurrencyGate.Release();
                }
            }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void NotifyNewsStateChanged()
    {
        OnPropertyChanged(nameof(HasFeaturedNews));
        OnPropertyChanged(nameof(HasNewsError));
        OnPropertyChanged(nameof(IsNewsReady));
        OnPropertyChanged(nameof(IsNewsEmpty));
        OnPropertyChanged(nameof(IsNewsLandingVisible));
        OnPropertyChanged(nameof(IsNewsArticleVisible));
        OnPropertyChanged(nameof(IsNewsArticleStatusVisible));
        OnPropertyChanged(nameof(HasSelectedNewsItem));
        OnPropertyChanged(nameof(IsNewsFeedVisible));
        OnPropertyChanged(nameof(IsNewsArticleContext));
        OnPropertyChanged(nameof(HasNewsArticleError));
        OnPropertyChanged(nameof(IsNewsArticleEmpty));
        OnPropertyChanged(nameof(CompactNewsPageIndex));
    }

    private void UpdateSelectedInstancePresentation()
    {
        OnPropertyChanged(nameof(IsSelectedInstanceInstalled));
        OnPropertyChanged(nameof(IsInstalledModsEmpty));
        OnPropertyChanged(nameof(IsModCatalogEmpty));
        OnPropertyChanged(nameof(IsInstanceWorldsEmpty));

        if (_selectedInstance is null)
        {
            SelectedInstanceName = SelectInstanceLabel;
            SelectedInstanceMeta = _localizer["instances.noInstances"];
            SelectedInstanceState = _localizer["instances.status.unknown"];
            SelectedInstanceBranch = string.Empty;
            SelectedInstanceVersion = string.Empty;
            SelectedInstancePlayTime = FormatPlayTime(0);
            PrimaryActionText = SelectInstanceLabel;
            CanRunPrimaryAction = !IsBusy;
            NotifyPrimaryActionStateChanged();
            return;
        }

        SelectedInstanceName = _selectedInstance.Name;
        SelectedInstanceMeta = $"{FormatBranch(_selectedInstance.Branch)}  ·  {FormatVersion(_selectedInstance.Version)}";
        SelectedInstanceBranch = FormatBranch(_selectedInstance.Branch);
        SelectedInstanceVersion = FormatVersion(_selectedInstance.Version);
        SelectedInstancePlayTime = FormatPlayTime(GetSelectedInstancePlayTimeSeconds());
        SelectedInstanceState = _selectedInstance.IsInstalled
            ? _localizer["instances.status.ready"]
            : _localizer["instances.status.notInstalled"];
        PrimaryActionText = IsBusy
            ? CanCancelActivity
                ? _localizer["main.cancel"]
                : _localizer["common.loading"]
            : IsGameRunning
                ? _localizer["main.stop"]
                : _selectedInstance.IsInstalled
                    ? _localizer["main.play"]
                    : _localizer["main.download"];
        CanRunPrimaryAction = !IsBusy || CanCancelActivity;
        NotifyPrimaryActionStateChanged();
    }

    private void UpdateManagedInstancePresentation()
    {
        OnPropertyChanged(nameof(IsManagedInstanceInstalled));
        OnPropertyChanged(nameof(CanRunManagedInstanceAction));
        OnPropertyChanged(nameof(CanOpenManagedInstanceFolder));
        OnPropertyChanged(nameof(CanDeleteManagedInstance));
        OnPropertyChanged(nameof(ManagedInstanceActionLabel));
        OnPropertyChanged(nameof(ManagedInstanceDeleteHint));
        OnPropertyChanged(nameof(IsInstalledModsEmpty));
        OnPropertyChanged(nameof(IsModCatalogEmpty));
        OnPropertyChanged(nameof(IsInstanceWorldsEmpty));
        OnPropertyChanged(nameof(InstanceSectionTitle));

        if (_managedInstance is null)
        {
            ManagedInstanceName = string.Empty;
            ManagedInstanceState = _localizer["instances.status.unknown"];
            ManagedInstanceBranch = string.Empty;
            ManagedInstanceVersion = string.Empty;
            ManagedInstancePlayTime = FormatPlayTime(0);
            return;
        }

        ManagedInstanceName = _managedInstance.Name;
        ManagedInstanceBranch = FormatBranch(_managedInstance.Branch);
        ManagedInstanceVersion = FormatVersion(_managedInstance.Version);
        ManagedInstancePlayTime = FormatPlayTime(GetManagedInstancePlayTimeSeconds());
        ManagedInstanceState = _managedInstance.IsInstalled
            ? _localizer["instances.status.ready"]
            : _localizer["instances.status.notInstalled"];
    }

    private void NotifyPrimaryActionStateChanged()
    {
        OnPropertyChanged(nameof(IsPrimarySelectAction));
        OnPropertyChanged(nameof(IsPrimaryStopAction));
        OnPropertyChanged(nameof(IsPrimaryDownloadAction));
        OnPropertyChanged(nameof(IsPrimaryPlayAction));
        OnPropertyChanged(nameof(IsPrimaryPendingAction));
    }

    private SettingsViewModel CreateSettingsViewModel()
        => new(
            _settingsStore,
            _uriLauncher,
            _localizer,
            _filePicker,
            _gitHubClient,
            _mirrorCatalog,
            _mirrorDiscovery,
            _versionCatalog);

    private void ApplyLanguage(string language)
    {
        Settings.RefreshLocalization();
        AccountType = _isOfficialProfile
            ? _localizer["desktopSettings.accountHytale"]
            : _localizer["desktopSettings.accountOffline"];

        foreach (var item in _allNews)
            item.RefreshCulture();
        foreach (var article in _articleViewModelCache.Values)
            article.RefreshCulture();

        CurrentPageTitle = CurrentPage switch
        {
            InstancesPage => InstancesLabel,
            NewsPage => NewsLabel,
            ProfilesPage => ProfilesLabel,
            SettingsPage => SettingsLabel,
            _ => DashboardLabel
        };
        if (!IsInstanceOverviewSection)
            DisplayedInstanceSectionTitle = InstanceSectionTitle;
        UpdateSelectedInstancePresentation();
        UpdateManagedInstancePresentation();
        OnPropertyChanged(string.Empty);
    }

    private string FormatVersion(int version)
        => version <= 0 ? _localizer["common.latest"] : $"v{version}";

    private string FormatBranch(string branch)
        => branch.Contains("pre", StringComparison.OrdinalIgnoreCase)
            ? _localizer["common.preRelease"]
            : _localizer["common.release"];

    private long GetSelectedInstancePlayTimeSeconds()
    {
        if (_selectedInstance is null)
            return 0;

        var instancePath = _instances.GetInstancePathById(_selectedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return 0;

        return Math.Max(0, _instances.GetInstanceMeta(instancePath)?.PlayTimeSeconds ?? 0);
    }

    private long GetManagedInstancePlayTimeSeconds()
    {
        if (_managedInstance is null)
            return 0;

        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return 0;

        return Math.Max(0, _instances.GetInstanceMeta(instancePath)?.PlayTimeSeconds ?? 0);
    }

    private string FormatPlayTime(long seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        var totalHours = (long)duration.TotalHours;
        return _localizer.Format("instances.info.playtimeValue", totalHours, duration.Minutes);
    }

    private void OnDownloadProgressChanged(ProgressUpdateMessage update)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var normalizedProgress = update.Progress <= 1
                ? update.Progress * 100
                : update.Progress;

            ActivityProgress = Math.Clamp(normalizedProgress, 0, 100);
            ActivityProgressText = $"{ActivityProgress:0}%";
            ActivityTitle = _localizer[update.MessageKey];
            ActivityDetail = update.State;
            IsActivityVisible = true;
            IsBusy = ActivityProgress < 100;
            CanCancelActivity = IsBusy;
            UpdateSelectedInstancePresentation();
        });
    }

    private void OnGameStateChanged(string state, int exitCode)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (state == "started")
                _gameSessionStartedAtUtc ??= DateTime.UtcNow;
            else if (state == "stopped")
                RecordGameSessionPlayTime();

            IsGameRunning = state is "started" or "running";
            if (state is "started" or "running" or "stopped")
                IsActivityVisible = false;

            UpdateSelectedInstancePresentation();
        });
    }

    private void RecordGameSessionPlayTime()
    {
        if (_gameSessionStartedAtUtc is not { } startedAt ||
            string.IsNullOrWhiteSpace(_gameSessionInstanceId))
            return;

        _gameSessionStartedAtUtc = null;
        var instanceId = _gameSessionInstanceId;
        _gameSessionInstanceId = null;
        var instancePath = _instances.GetInstancePathById(instanceId);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        var meta = _instances.GetInstanceMeta(instancePath);
        if (meta is null)
            return;

        var elapsedSeconds = Math.Max(0, (long)(DateTime.UtcNow - startedAt).TotalSeconds);
        meta.PlayTimeSeconds += elapsedSeconds;
        meta.LastPlayedAt = DateTime.UtcNow;
        _instances.SaveInstanceMeta(instancePath, meta);
    }

    private void OnErrorOccurred(string type, string message, string? technical)
        => Dispatcher.UIThread.Post(() => ShowError(technical ?? message));

    private void OnBackgroundChanged(string? mode)
        => Dispatcher.UIThread.Post(() => ReplaceDashboardBackground(mode));

    private void ReplaceDashboardBackground(string? mode)
    {
        var replacement = LoadDashboardBackground(mode);
        var previous = DashboardBackground;
        DashboardBackground = replacement;
        previous?.Dispose();
    }

    private Bitmap LoadDashboardBackground(string? mode)
    {
        var available = _settingsStore.AvailableBackgrounds;
        var selected = mode;
        if (string.IsNullOrWhiteSpace(selected) ||
            string.Equals(selected, "auto", StringComparison.OrdinalIgnoreCase) ||
            !available.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            selected = available.Count > 0
                ? available[Random.Shared.Next(available.Count)]
                : "bg_26.jpg";
        }

        var uri = new Uri($"avares://HyPrism.Desktop/Assets/Backgrounds/{selected}");
        using var stream = AssetLoader.Open(uri);
        return new Bitmap(stream);
    }

    private void ShowError(string message)
    {
        ActivityTitle = _localizer["error.title"];
        ActivityDetail = message;
        ActivityProgress = 0;
        ActivityProgressText = string.Empty;
        IsActivityVisible = true;
        IsBusy = false;
        CanCancelActivity = false;
        UpdateSelectedInstancePresentation();
    }

    private void NotifyPageStateChanged()
    {
        OnPropertyChanged(nameof(IsDashboard));
        OnPropertyChanged(nameof(IsInstances));
        OnPropertyChanged(nameof(IsNews));
        OnPropertyChanged(nameof(IsProfiles));
        OnPropertyChanged(nameof(IsSettings));
        OnPropertyChanged(nameof(IsPlaceholderPage));
        OnPropertyChanged(nameof(IsGlobalActivityVisible));
        OnPropertyChanged(nameof(IsNewsLandingVisible));
        OnPropertyChanged(nameof(IsNewsArticleVisible));
        OnPropertyChanged(nameof(IsNewsArticleStatusVisible));
        OnPropertyChanged(nameof(IsNewsFeedVisible));
        OnPropertyChanged(nameof(IsNewsArticleContext));
        OnPropertyChanged(nameof(IsNewsArticleEmpty));
        OnPropertyChanged(nameof(CompactNewsPageIndex));
    }

    partial void OnIsActivityVisibleChanged(bool value)
        => OnPropertyChanged(nameof(IsGlobalActivityVisible));

    partial void OnInstalledModsSearchQueryChanged(string value)
        => FilterInstalledMods();

    public void Dispose()
    {
        _newsImagesCancellation.Cancel();
        _newsImagesCancellation.Dispose();
        _articleImagesCancellation.Cancel();
        _articleImagesCancellation.Dispose();
        _articlePresentationCancellation.Cancel();
        _articlePresentationCancellation.Dispose();
        _compactNewsTransitionCancellation.Cancel();
        _compactNewsTransitionCancellation.Dispose();
        _instanceVersionsCancellation?.Cancel();
        _instanceVersionsCancellation?.Dispose();
        SelectedNewsArticle = null;
        foreach (var article in _articleViewModelCache.Values)
            article.Dispose();
        _articleViewModelCache.Clear();
        foreach (var item in _allNews)
            item.Dispose();
        _progress.DownloadProgressChanged -= OnDownloadProgressChanged;
        _progress.GameStateChanged -= OnGameStateChanged;
        _progress.ErrorOccurred -= OnErrorOccurred;
        _settingsStore.BackgroundChanged -= OnBackgroundChanged;
        _localizer.LanguageChanged -= ApplyLanguage;
        Settings.Dispose();
        DashboardBackground?.Dispose();
        DashboardBackground = null;
    }
}
