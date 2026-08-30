// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPrism.Desktop.Features.About;
using HyPrism.Desktop.Features.Dashboard;
using HyPrism.Desktop.Features.Instances;
using HyPrism.Desktop.Features.News;
using HyPrism.Desktop.Features.Profiles;
using HyPrism.Desktop.Features.Settings;
using HyPrism.Desktop.Controls;
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
using HyPrism.Core.Infrastructure;

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
    private const int MaximumCachedNewsArticles = 2;
    private const int CompactTransitionMilliseconds = 320;
    private const int ArticleSkeletonDelayMilliseconds = 180;
    private const int ArticleBodySkeletonFadeMilliseconds = 180;
    private const int ModCatalogPreviewFilesSkeletonMinMilliseconds = 220;
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
    private readonly IGameConsoleService? _gameConsole;
    private readonly HttpClient _httpClient;
    private readonly RemoteImageCache? _remoteImageCache;
    private readonly StringLocalizer _localizer;
    private InstanceInfo? _selectedInstance;
    private InstanceInfo? _managedInstance;
    private bool _suppressInstancesChanged;
    private readonly List<NewsItemViewModel> _allNews = [];
    private readonly Dictionary<string, NewsArticleViewModel> _articleViewModelCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _busyInstanceCounts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableRangeCollection<InstanceItemViewModel> _allInstances = [];
    private readonly ObservableRangeCollection<InstanceVersionItemViewModel> _availableInstanceVersions = [];
    private readonly ObservableRangeCollection<InstanceModItemViewModel> _installedMods = [];
    private readonly ObservableRangeCollection<InstanceModItemViewModel> _visibleInstalledMods = [];
    private readonly ObservableRangeCollection<ModCatalogItemViewModel> _modCatalogItems = [];
    private readonly ObservableRangeCollection<ModCatalogFileItemViewModel> _modCatalogPreviewFiles = [];
    private readonly ObservableRangeCollection<InstanceWorldItemViewModel> _instanceWorlds = [];
    private readonly ObservableRangeCollection<NewsItemViewModel> _latestNews = [];
    private ProgressUpdateMessage? _pendingProgressUpdate;
    private int _progressUpdateScheduled;
    private int _backgroundLoadVersion;
    private bool _isDisposed;
    private CancellationTokenSource _newsImagesCancellation = new();
    private CancellationTokenSource _articleImagesCancellation = new();
    private CancellationTokenSource _articlePresentationCancellation = new();
    private CancellationTokenSource _compactNewsTransitionCancellation = new();
    private CancellationTokenSource? _instanceVersionsCancellation;
    private readonly DispatcherTimer _managedInstanceActionTimer;
    private DateTime? _managedInstanceActionStartedAtUtc;
    private DateTime? _managedInstanceGameStartedAtUtc;
    private string? _managedInstanceActionInstanceId;
    private long _managedInstanceActionGeneration;
    private readonly HashSet<long> _completedManagedActivityGenerations = [];
    private bool _managedInstanceActionStartedWithInstall;
    private bool _isManagedInstanceCancellationArmed;
    private string? _modsLoadedForInstanceId;
    private string? _worldsLoadedForInstanceId;
    private readonly DispatcherTimer _consoleFlushTimer;
    private readonly object _pendingConsoleLock = new();
    private readonly List<GameConsoleLine> _pendingConsoleLines = [];
    private readonly ObservableRangeCollection<ConsoleLineViewModel> _consoleLines = [];
    private readonly Dictionary<string, InstalledMod> _modUpdatesById = new(StringComparer.Ordinal);
    private string? _consoleLoadedForInstanceId;
    private bool _modCatalogFiltersLoaded;
    private bool _suppressCatalogReload;
    private List<ModCategory> _loadedModCategories = [];
    private int _modCatalogPage;
    private CancellationTokenSource _modIconsCancellation = new();
    private CancellationTokenSource _modPreviewImageCancellation = new();
    private CancellationTokenSource _modPreviewImageTransitionCancellation = new();
    private CancellationTokenSource _modPreviewRevealCancellation = new();
    private int _modCatalogPreviewVersion;
    private readonly List<Bitmap?> _modCatalogPreviewBitmaps = [];
    private readonly List<bool> _modCatalogPreviewBitmapSlotsResolved = [];
    private readonly object _modCatalogPreviewBitmapsLock = new();
    private bool _isModCatalogPreviewImageFadingOut;
    private string? _modCatalogGameVersion;
    private const int ModCatalogPageSize = 24;
    private const int MaxConsoleLines = 3000;
    private bool _hasLoadedNews;
    private bool _canLoadMoreNews = true;
    private bool _isOfficialProfile;
    private int _articleLoadVersion;
    private long _compactNewsTransitionReadyAt;
    private SettingsViewModel _settings;
    private readonly ProfilesViewModel _profiles;

    [ObservableProperty]
    private bool _isStartupLoading;

    [ObservableProperty]
    private string _startupLoadingStatus = string.Empty;

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
    [NotifyPropertyChangedFor(nameof(NewsArticleDisplayTitle))]
    [NotifyPropertyChangedFor(nameof(NewsArticleDisplayMetadata))]
    private NewsItemViewModel? _selectedNewsItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewsArticleVisible))]
    [NotifyPropertyChangedFor(nameof(IsNewsLandingVisible))]
    [NotifyPropertyChangedFor(nameof(IsNewsArticleBodyPreparing))]
    [NotifyPropertyChangedFor(nameof(NewsArticleDisplayTitle))]
    [NotifyPropertyChangedFor(nameof(NewsArticleDisplayMetadata))]
    private NewsArticleViewModel? _selectedNewsArticle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewsArticleBodyPreparing))]
    private bool _isNewsArticleBodyVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewsArticleBodyPreparing))]
    private bool _isNewsArticleBodySkeletonVisible;

    [ObservableProperty]
    private bool _isNewsArticleBodySkeletonFadingOut;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewsLandingVisible))]
    [NotifyPropertyChangedFor(nameof(IsNewsArticleStatusVisible))]
    [NotifyPropertyChangedFor(nameof(NewsArticleDisplayMetadata))]
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedModCountText))]
    private int _selectedModCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModUpdates))]
    [NotifyPropertyChangedFor(nameof(ModUpdateCountText))]
    [NotifyPropertyChangedFor(nameof(InstanceModsUpdatesAvailableText))]
    private int _modUpdateCount;

    [ObservableProperty]
    private bool _isCheckingModUpdates;

    [ObservableProperty]
    private bool _isApplyingModUpdates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConsoleEmpty))]
    [NotifyPropertyChangedFor(nameof(HasConsoleLines))]
    [NotifyPropertyChangedFor(nameof(ConsoleLineCountText))]
    private string _consoleSearchQuery = string.Empty;

    [ObservableProperty]
    private bool _isConsoleAutoScroll = true;

    [ObservableProperty]
    private bool _isConsoleWrap;

    [ObservableProperty]
    private int _consoleRevision;

    [ObservableProperty]
    private InstanceListOptionViewModel? _selectedModCatalogCategory;

    [ObservableProperty]
    private InstanceListOptionViewModel? _selectedModCatalogSort;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMoreModCatalog))]
    private bool _isLoadingMoreModCatalog;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMoreModCatalog))]
    private bool _hasMoreModCatalog;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallModCatalogPreview))]
    [NotifyPropertyChangedFor(nameof(HasMultipleModCatalogPreviewScreenshots))]
    [NotifyPropertyChangedFor(nameof(CanShowPreviousModCatalogScreenshot))]
    [NotifyPropertyChangedFor(nameof(CanShowNextModCatalogScreenshot))]
    [NotifyPropertyChangedFor(nameof(IsModCatalogPreviewMounted))]
    private ModCatalogItemViewModel? _selectedModCatalogPreview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModCatalogPreview))]
    private bool _isModCatalogPreviewOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallModCatalogPreview))]
    private ModCatalogFileItemViewModel? _selectedModCatalogPreviewFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModCatalogPreviewImage))]
    private Bitmap? _modCatalogPreviewImage;

    [ObservableProperty]
    private bool _isModCatalogPreviewLoading;

    [ObservableProperty]
    private bool _isModCatalogPreviewFilesSkeletonVisible;

    [ObservableProperty]
    private bool _isModCatalogPreviewFilesSkeletonFadingOut;

    [ObservableProperty]
    private bool _isModCatalogPreviewFilesContentVisible;

    [ObservableProperty]
    private bool _isModCatalogPreviewImageLoading;

    [ObservableProperty]
    private bool _isModCatalogPreviewImageVisible;

    [ObservableProperty]
    private bool _isModCatalogPreviewImageTransitioning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallSelectedCatalogMods))]
    private bool _isInstallingSelectedCatalogMods;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowPreviousModCatalogScreenshot))]
    [NotifyPropertyChangedFor(nameof(CanShowNextModCatalogScreenshot))]
    private int _modCatalogPreviewScreenshotIndex;

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
        IModManager? modManager = null,
        IHytaleAuthenticator? authenticator = null,
        RemoteImageCache? remoteImageCache = null,
        IGameConsoleService? gameConsole = null)
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
        _gameConsole = gameConsole;
        _httpClient = httpClient;
        _remoteImageCache = remoteImageCache;
        _localizer = localizer;
        _localizer.LanguageChanged += ApplyLanguage;
        _settingsStore.BackgroundChanged += OnBackgroundChanged;
        _settings = CreateSettingsViewModel();
        _profiles = new ProfilesViewModel(
            profiles,
            profileRepository,
            _uriLauncher,
            _localizer,
            authenticator,
            _instances);
        _profiles.ActiveProfileChanged += OnActiveProfileChanged;
        _ = ReplaceDashboardBackgroundAsync(_settingsStore.BackgroundMode);

        UserName = profiles.GetNick();
        UserInitial = string.IsNullOrWhiteSpace(UserName)
            ? "H"
            : UserName[..1].ToUpperInvariant();
        _isOfficialProfile = profileRepository.GetSelectedProfile()?.IsOfficial == true;
        AccountType = _isOfficialProfile
            ? _localizer["desktopSettings.accountHytale"]
            : _localizer["desktopSettings.accountOffline"];

        _progress.DownloadProgressChanged += OnDownloadProgressChanged;
        _progress.OperationErrorOccurred += OnOperationErrorOccurred;
        _gameProcess.GameProcessStarted += OnGameProcessStarted;
        _gameProcess.GameProcessExited += OnGameProcessExited;
        _gameLaunchCoordinator.LaunchFailed += OnLaunchFailed;
        _instances.InstancesChanged += OnInstancesChanged;
        IsGameRunning = _gameProcess.IsGameRunning();

        _managedInstanceActionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _managedInstanceActionTimer.Tick += OnManagedInstanceActionTimerTick;

        _consoleFlushTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _consoleFlushTimer.Tick += OnConsoleFlushTimerTick;
        if (_gameConsole is not null)
            _gameConsole.LineReceived += OnConsoleLineReceived;
        BuildModCatalogSortOptions();

        CurrentPageTitle = DashboardLabel;
        RefreshInstances();
        RefreshManagedInstanceContent();
        if (IsGameRunning)
            _managedInstanceActionTimer.Start();
    }

    public ObservableCollection<InstanceItemViewModel> AllInstances => _allInstances;
    public ObservableCollection<InstanceVersionItemViewModel> AvailableInstanceVersions => _availableInstanceVersions;
    public ObservableCollection<InstanceModItemViewModel> InstalledMods => _installedMods;
    public ObservableCollection<InstanceModItemViewModel> VisibleInstalledMods => _visibleInstalledMods;
    public ObservableCollection<ModCatalogItemViewModel> ModCatalogItems => _modCatalogItems;
    public ObservableCollection<ModCatalogFileItemViewModel> ModCatalogPreviewFiles => _modCatalogPreviewFiles;
    public ObservableCollection<InstanceWorldItemViewModel> InstanceWorlds => _instanceWorlds;
    public ObservableCollection<ConsoleLineViewModel> ConsoleLines => _consoleLines;
    public ObservableCollection<InstanceListOptionViewModel> ModCatalogCategories { get; } = [];
    public ObservableCollection<InstanceListOptionViewModel> ModCatalogSortOptions { get; } = [];
    public ObservableCollection<NewsItemViewModel> LatestNews => _latestNews;
    public SettingsViewModel Settings
    {
        get => _settings;
        private set => SetProperty(ref _settings, value);
    }
    public ProfilesViewModel Profiles => _profiles;

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
    public string StartupLoadingTitle => _localizer["startup.loading.title"];
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
    public string InstanceModsCheckUpdatesLabel => _localizer["instances.mods.checkUpdates"];
    public string InstanceModsUpdateAllLabel => _localizer["instances.mods.updateAll"];
    public string InstanceModsAddLabel => _localizer["instances.mods.add"];
    public string InstanceModsDropImportLabel => _localizer["instances.mods.dropToImport"];
    public string InstanceModsSelectAllLabel => _localizer["instances.mods.selectAll"];
    public string InstanceModsClearSelectionLabel => _localizer["common.clear"];
    public string InstanceModsEnableSelectedLabel => _localizer["instances.mods.enableSelected"];
    public string InstanceModsDisableSelectedLabel => _localizer["instances.mods.disableSelected"];
    public string InstanceModsDeleteSelectedLabel => _localizer["instances.mods.deleteSelected"];
    public string InstanceModsDeleteTooltip => _localizer["common.delete"];
    public string InstanceModsDeleteLabel => _localizer["common.delete"];
    public string InstanceModsDeleteTitle => _localizer["instances.mods.deleteTitle"];
    public string InstanceModsDeleteHint => _localizer["instances.mods.deleteHint"];
    public string InstanceModsOpenPageTooltip => _localizer["instances.mods.openPage"];
    public string InstanceModsToggleTooltip => _localizer["instances.mods.toggle"];
    public string InstanceBrowseCategoryLabel => _localizer["instances.browse.category"];
    public string InstanceBrowseSortLabel => _localizer["instances.browse.sort"];
    public string InstanceBrowseLoadingMoreLabel => _localizer["instances.browse.loadingMore"];
    public string ModCatalogPreviewAuthorLabel => _localizer["modManager.author"];
    public string ModCatalogPreviewDownloadsLabel => _localizer["modManager.downloads"];
    public string ModCatalogPreviewNoFilesLabel => _localizer["modManager.noFilesAvailable"];
    public string ModCatalogPreviewCloseLabel => _localizer["common.close"];
    public string ModCatalogOpenCurseForgeLabel => _localizer["modManager.openCurseforge"];
    public string ModCatalogFileTypeColumn => _localizer["settings.downloads.columnType"];
    public string ModCatalogFileNameColumn => _localizer["modManager.name"];
    public string ModCatalogFileGameVersionsColumn => _localizer["modManager.gameVersions"];
    public string ModCatalogInstallSelectedLabel =>
        $"{_localizer["modManager.installSelected"]} ({SelectedCatalogModCount})";
    public string ModCatalogGameVersionLabel => string.IsNullOrWhiteSpace(_modCatalogGameVersion)
        ? _localizer["instances.mods.compatibility.versionUnknown"]
        : _localizer.Format("instances.mods.compatibility.gameVersion", _modCatalogGameVersion);
    public string ConsoleAutoScrollLabel => _localizer["instances.console.autoScroll"];
    public string ConsoleClearLabel => _localizer["instances.console.clear"];
    public string ConsoleSearchHint => _localizer["instances.console.search"];
    public string ConsoleWrapLabel => _localizer["instances.console.wrap"];
    public bool HasModSelection => SelectedModCount > 0;
    public bool HasModUpdates => ModUpdateCount > 0;
    public bool IsConsoleRunning => IsManagedInstanceRunning;
    public bool HasConsoleLines => ConsoleLines.Count > 0;
    public bool IsConsoleEmpty => ConsoleLines.Count == 0;
    public bool CanLoadMoreModCatalog => HasMoreModCatalog && !IsLoadingMoreModCatalog && !IsModCatalogLoading;
    public bool HasModCatalogPreview => IsModCatalogPreviewOpen;
    public bool IsModCatalogPreviewMounted => SelectedModCatalogPreview is not null;
    public bool HasModCatalogPreviewImage => ModCatalogPreviewImage is not null;
    public bool HasModCatalogPreviewFiles => ModCatalogPreviewFiles.Count > 0;
    public bool HasMultipleModCatalogPreviewScreenshots =>
        SelectedModCatalogPreview is { ScreenshotUrls.Count: > 1 };
    public bool CanShowPreviousModCatalogScreenshot =>
        ModCatalogPreviewScreenshotIndex > 0;
    public bool CanShowNextModCatalogScreenshot =>
        SelectedModCatalogPreview is { } item &&
        ModCatalogPreviewScreenshotIndex < item.ScreenshotUrls.Count - 1;
    public bool CanInstallModCatalogPreview =>
        SelectedModCatalogPreview is { IsInstalling: false } &&
        SelectedModCatalogPreviewFile is { CanInstall: true };
    public int SelectedCatalogModCount => ModCatalogItems.Count(item => item.IsSelected);
    public bool HasSelectedCatalogMods => SelectedCatalogModCount > 0;
    public bool CanInstallSelectedCatalogMods => HasSelectedCatalogMods && !IsInstallingSelectedCatalogMods;
    public string SelectedModCountText =>
        _localizer.Format("instances.mods.selectedCount", SelectedModCount);
    public string ModUpdateCountText =>
        ModUpdateCount.ToString(System.Globalization.CultureInfo.CurrentCulture);
    public string InstanceModsUpdatesAvailableText =>
        _localizer.Format("instances.mods.updatesAvailable", ModUpdateCount);
    public string InstanceModsFooterText =>
        _localizer.Format("instances.mods.countInstalled", InstalledMods.Count);
    public string ConsoleStatusText =>
        IsConsoleRunning
            ? _localizer["instances.console.running"]
            : _localizer["instances.console.offline"];
    public string ConsoleLineCountText =>
        _localizer.Format("instances.console.lineCount", ConsoleLines.Count);
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
    public string ManagedInstanceActionCancelLabel => _localizer["instances.actions.cancel"];
    public string ManagedInstanceActionStatusText => IsManagedInstanceActionRunning
        ? _localizer["instances.actions.running"]
        : _managedInstanceActionStartedWithInstall
            ? ActivityTitle
            : _localizer["instances.actions.launching"];
    public string ManagedInstanceActionMetricText => _managedInstanceActionStartedWithInstall &&
                                                     !IsManagedInstanceActionRunning
        ? ActivityProgressText
        : FormatManagedInstanceActionElapsedTime();
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
    public bool IsPlaceholderPage => !IsDashboard && !IsInstances && !IsNews && !IsProfiles && !IsSettings;
    public bool HasInstances => AllInstances.Count > 0;
    public bool HasSelectedInstance => _selectedInstance is not null;
    public bool HasManagedInstance => _managedInstance is not null;
    public bool HasAvailableInstanceVersions => AvailableInstanceVersions.Count > 0;
    public bool HasInstanceCreationError => !string.IsNullOrWhiteSpace(InstanceCreationError);
    public bool HasInstanceContentError => !string.IsNullOrWhiteSpace(InstanceContentError);
    public bool IsSelectedInstanceInstalled => _selectedInstance?.IsInstalled == true;
    public bool IsManagedInstanceInstalled => _managedInstance?.IsInstalled == true;
    public bool IsManagedInstanceActionActive => IsManagedInstanceRunning ||
        (_managedInstance is not null &&
         string.Equals(
             _managedInstance.Id,
             _managedInstanceActionInstanceId,
             StringComparison.OrdinalIgnoreCase));
    public bool IsManagedInstanceActionRunning => IsManagedInstanceRunning;
    public bool ShouldSpinManagedInstanceAction =>
        IsManagedInstanceActionActive && !IsManagedInstanceActionRunning;
    private bool IsManagedInstanceRunning => _managedInstance is not null
        && _gameProcess.IsInstanceRunning(_managedInstance.Id);
    private bool IsSelectedInstanceRunning => _selectedInstance is not null
        && _gameProcess.IsInstanceRunning(_selectedInstance.Id);
    public bool IsManagedInstanceCancellationArmed =>
        IsManagedInstanceActionActive && _isManagedInstanceCancellationArmed;
    public bool CanRunManagedInstanceAction =>
        _managedInstance is not null &&
        ((!IsInstanceBusy(_managedInstance.Id) && !IsManagedInstanceRunning) || IsManagedInstanceActionActive);
    public bool CanOpenManagedInstanceFolder => _managedInstance is not null;
    public bool CanDeleteManagedInstance =>
        _managedInstance is not null && !IsInstanceBusy(_managedInstance.Id) && !IsManagedInstanceRunning;
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
    public bool IsPrimarySelectAction => _selectedInstance is null;
    public bool IsPrimaryStopAction => IsSelectedInstanceRunning || (IsBusy && CanCancelActivity);
    public bool IsPrimaryDownloadAction =>
        _selectedInstance is { IsInstalled: false }
        && !IsInstanceBusy(_selectedInstance.Id)
        && !IsSelectedInstanceRunning;
    public bool IsPrimaryPlayAction =>
        _selectedInstance is { IsInstalled: true }
        && !IsInstanceBusy(_selectedInstance.Id)
        && !IsSelectedInstanceRunning;
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
    public string NewsArticleDisplayTitle =>
        SelectedNewsItem?.Title ?? SelectedNewsArticle?.Title ?? string.Empty;
    public string NewsArticleDisplayMetadata =>
        SelectedNewsArticle is not null && !IsNewsArticleLoading
            ? SelectedNewsArticle.Metadata
            : string.Join(
                "  ·  ",
                new[] { SelectedNewsItem?.Author, SelectedNewsItem?.Date }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
    public bool IsNewsArticleBodyPreparing =>
        SelectedNewsArticle is not null && IsNewsArticleBodySkeletonVisible;
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
        {
            if (_hasLoadedNews)
                RestartNewsImageLoading();
            else
                _ = LoadNewsAsync();
        }
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
        ResetInstanceCreatorState();
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
            var branch = NewInstanceBranch;
            var instance = _instances.CreateInstanceMeta(
                branch,
                version,
                $"{FormatBranch(branch)} {FormatVersion(version)}");
            _managedInstance = _instances.FindInstanceById(instance.Id);
            IsInstanceCreatorOpen = false;
            ResetInstanceCreatorState();
            InstanceSection = string.Empty;
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
        UpdateManagedInstanceListSelection(instance.Id);
        InstanceSection = string.Empty;
        UpdateManagedInstancePresentation();
        RefreshManagedInstanceContent();
    }

    private void UpdateManagedInstanceListSelection(string managedInstanceId)
    {
        for (var index = 0; index < AllInstances.Count; index++)
        {
            var item = AllInstances[index];
            var isManaged = string.Equals(item.Id, managedInstanceId, StringComparison.Ordinal);
            if (item.IsManaged != isManaged)
                AllInstances[index] = item with { IsManaged = isManaged };
        }
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
        else if (section == "browse")
        {
            _ = EnsureModCatalogFiltersAsync();
            if (ModCatalogItems.Count == 0)
                _ = SearchModCatalogAsync();
        }
        else if (section == "worlds" && !IsInstanceWorldsLoading &&
                 !string.Equals(_worldsLoadedForInstanceId, _managedInstance?.Id, StringComparison.Ordinal))
            _ = LoadInstanceWorldsAsync();
        else if (section == "console")
        {
            PrepareConsoleForCurrentInstance();
            _consoleFlushTimer.Start();
        }
    }

    [RelayCommand]
    private void CloseInstanceSection()
    {
        InstanceSection = string.Empty;
        InstanceContentError = string.Empty;
        ResetModCatalogPreview();
        _consoleFlushTimer.Stop();
    }

    [RelayCommand]
    private Task RefreshInstanceModsAsync()
        => LoadInstalledModsAsync();

    [RelayCommand]
    private Task SearchModCatalogAsync()
        => LoadModCatalogAsync(ModCatalogSearchQuery, false);

    [RelayCommand]
    private Task LoadMoreModCatalogAsync()
        => CanLoadMoreModCatalog
            ? LoadModCatalogAsync(ModCatalogSearchQuery, true)
            : Task.CompletedTask;

    [RelayCommand]
    private void ToggleModCatalogSelection(ModCatalogItemViewModel? item)
    {
        if (item is null || !item.CanSelect)
            return;

        item.IsSelected = !item.IsSelected;
        NotifyCatalogSelectionChanged();
    }

    [RelayCommand]
    private async Task InstallSelectedCatalogModsAsync()
    {
        if (!CanInstallSelectedCatalogMods || _modManager is null ||
            _managedInstance?.IsInstalled != true)
        {
            return;
        }

        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        var selected = ModCatalogItems.Where(item => item.IsSelected && item.CanSelect).ToArray();
        IsInstallingSelectedCatalogMods = true;
        InstanceContentError = string.Empty;
        var failed = false;
        try
        {
            foreach (var item in selected)
            {
                item.IsInstalling = true;
                try
                {
                    if (!await _modManager.InstallModFileToInstanceAsync(
                            item.Id,
                            item.RecommendedFileId,
                            instancePath))
                    {
                        failed = true;
                        continue;
                    }

                    item.IsInstalled = true;
                    item.InstalledFileId = item.RecommendedFileId;
                    item.IsSelected = false;
                }
                catch
                {
                    failed = true;
                }
                finally
                {
                    item.IsInstalling = false;
                }
            }

            await LoadInstalledModsAsync();
            if (failed)
                InstanceContentError = _localizer["modManager.installFailed"];
        }
        finally
        {
            IsInstallingSelectedCatalogMods = false;
            NotifyCatalogSelectionChanged();
        }
    }

    [RelayCommand]
    private async Task SelectModCatalogPreviewAsync(ModCatalogItemViewModel? item)
    {
        if (item is null || _modManager is null)
            return;

        if (ReferenceEquals(SelectedModCatalogPreview, item))
        {
            IsModCatalogPreviewOpen = true;
            return;
        }

        var previewVersion = ++_modCatalogPreviewVersion;
        SelectedModCatalogPreview = item;
        IsModCatalogPreviewOpen = true;
        SelectedModCatalogPreviewFile = null;
        _modCatalogPreviewFiles.Clear();
        OnPropertyChanged(nameof(HasModCatalogPreviewFiles));
        OnPropertyChanged(nameof(HasMultipleModCatalogPreviewScreenshots));
        ModCatalogPreviewScreenshotIndex = 0;
        IsModCatalogPreviewLoading = true;
        IsModCatalogPreviewFilesSkeletonVisible = true;
        IsModCatalogPreviewFilesSkeletonFadingOut = false;
        IsModCatalogPreviewFilesContentVisible = false;
        BeginModCatalogPreviewImagePreload(item, previewVersion);

        try
        {
            var result = await _modManager.GetModFilesAsync(item.Id, 0, 10);
            if (previewVersion != _modCatalogPreviewVersion ||
                !ReferenceEquals(SelectedModCatalogPreview, item))
            {
                return;
            }

            var files = result.Files.Select(file =>
            {
                var compatibility = ModCompatibilityEvaluator.Evaluate(
                    _modCatalogGameVersion,
                    file.GameVersions);
                return new ModCatalogFileItemViewModel(
                    file,
                    GetModReleaseLabel(file.ReleaseType),
                    string.Equals(file.Id, item.InstalledFileId, StringComparison.Ordinal),
                    compatibility,
                    GetModCompatibilityLabel(compatibility));
            });
            _modCatalogPreviewFiles.ReplaceRange(files);
            SelectedModCatalogPreviewFile = _modCatalogPreviewFiles.FirstOrDefault(file =>
                file.IsInstalled) ??
                _modCatalogPreviewFiles.FirstOrDefault(file =>
                    string.Equals(file.Id, item.RecommendedFileId, StringComparison.Ordinal)) ??
                _modCatalogPreviewFiles.FirstOrDefault(file => file.CanInstall) ??
                _modCatalogPreviewFiles.FirstOrDefault();
            if (SelectedModCatalogPreviewFile is not null)
                SelectedModCatalogPreviewFile.IsSelected = true;
            OnPropertyChanged(nameof(HasModCatalogPreviewFiles));
            OnPropertyChanged(nameof(CanInstallModCatalogPreview));
        }
        catch (Exception ex)
        {
            if (previewVersion == _modCatalogPreviewVersion)
                InstanceContentError = ex.Message;
        }
        finally
        {
            if (previewVersion == _modCatalogPreviewVersion)
            {
                IsModCatalogPreviewLoading = false;
                _ = RevealModCatalogPreviewFilesAsync(previewVersion);
            }
        }
    }

    [RelayCommand]
    private void CloseModCatalogPreview()
        => IsModCatalogPreviewOpen = false;

    internal void CompleteModCatalogPreviewClose()
    {
        if (!IsModCatalogPreviewOpen)
            ResetModCatalogPreview();
    }

    [RelayCommand]
    private void SelectModCatalogPreviewFile(ModCatalogFileItemViewModel? file)
    {
        if (file is not { CanSelect: true })
            return;

        foreach (var previewFile in ModCatalogPreviewFiles)
            previewFile.IsSelected = ReferenceEquals(previewFile, file);
        SelectedModCatalogPreviewFile = file;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ShowPreviousModCatalogScreenshotAsync()
        => MoveModCatalogPreviewScreenshotAsync(-1);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ShowNextModCatalogScreenshotAsync()
        => MoveModCatalogPreviewScreenshotAsync(1);

    [RelayCommand]
    private async Task InstallModCatalogPreviewAsync()
    {
        var item = SelectedModCatalogPreview;
        var file = SelectedModCatalogPreviewFile;
        if (item is null || file is null || item.IsInstalling || !file.CanInstall ||
            _modManager is null || _managedInstance?.IsInstalled != true)
        {
            return;
        }

        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        item.IsInstalling = true;
        OnPropertyChanged(nameof(CanInstallModCatalogPreview));
        InstanceContentError = string.Empty;
        try
        {
            if (!await _modManager.InstallModFileToInstanceAsync(item.Id, file.Id, instancePath))
            {
                InstanceContentError = _localizer["instances.mods.installFailed"];
                return;
            }

            item.IsInstalled = true;
            item.InstalledFileId = file.Id;
            foreach (var previewFile in ModCatalogPreviewFiles)
                previewFile.IsInstalled = ReferenceEquals(previewFile, file);
            await LoadInstalledModsAsync();
        }
        catch (Exception ex)
        {
            InstanceContentError = ex.Message;
        }
        finally
        {
            item.IsInstalling = false;
            OnPropertyChanged(nameof(CanInstallModCatalogPreview));
        }
    }

    [RelayCommand]
    private Task RefreshInstanceWorldsAsync()
        => LoadInstanceWorldsAsync();

    #region Installed mod management

    [RelayCommand]
    private async Task ToggleModAsync(InstanceModItemViewModel? item)
    {
        if (item is null || item.IsBusy ||
            _modManager is null || _managedInstance?.IsInstalled != true)
        {
            return;
        }

        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        item.IsBusy = true;
        InstanceContentError = string.Empty;
        try
        {
            var target = !item.IsEnabled;
            var changed = await Task.Run(
                () => _modManager.SetModEnabledAsync(instancePath, item.Id, target));
            if (!changed)
            {
                InstanceContentError = _localizer["instances.mods.toggleFailed"];
                return;
            }

            item.IsEnabled = target;
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteModAsync(InstanceModItemViewModel? item)
    {
        if (item is null || item.IsBusy ||
            _modManager is null || _managedInstance?.IsInstalled != true)
        {
            return;
        }

        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        item.IsBusy = true;
        InstanceContentError = string.Empty;
        try
        {
            var removed = await Task.Run(
                () => _modManager.RemoveInstalledModAsync(instancePath, item.Id));
            if (!removed)
                InstanceContentError = _localizer["instances.mods.deleteFailed"];
        }
        finally
        {
            item.IsBusy = false;
        }

        await LoadInstalledModsAsync();
    }

    [RelayCommand]
    private void SelectAllInstalledMods()
    {
        foreach (var mod in VisibleInstalledMods)
            mod.IsSelected = true;
    }

    [RelayCommand]
    private void ClearInstalledModsSelection()
    {
        foreach (var mod in InstalledMods)
            mod.IsSelected = false;
    }

    [RelayCommand]
    private Task EnableSelectedModsAsync()
        => SetModsEnabledForSelectionAsync(true);

    [RelayCommand]
    private Task DisableSelectedModsAsync()
        => SetModsEnabledForSelectionAsync(false);

    private async Task SetModsEnabledForSelectionAsync(bool enabled)
    {
        var selected = InstalledMods.Where(mod => mod.IsSelected).ToList();
        if (selected.Count == 0 ||
            _modManager is null || _managedInstance?.IsInstalled != true)
        {
            return;
        }

        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        InstanceContentError = string.Empty;
        foreach (var item in selected)
        {
            if (item.IsBusy)
                continue;

            item.IsBusy = true;
            try
            {
                var changed = await Task.Run(
                    () => _modManager.SetModEnabledAsync(instancePath, item.Id, enabled));
                if (changed)
                    item.IsEnabled = enabled;
                else
                    InstanceContentError = _localizer["instances.mods.toggleFailed"];
            }
            finally
            {
                item.IsBusy = false;
            }
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedModsAsync()
    {
        var selected = InstalledMods.Where(mod => mod.IsSelected).ToList();
        if (selected.Count == 0 ||
            _modManager is null || _managedInstance?.IsInstalled != true)
        {
            return;
        }

        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        InstanceContentError = string.Empty;
        foreach (var item in selected)
        {
            item.IsBusy = true;
            var removed = await Task.Run(
                () => _modManager.RemoveInstalledModAsync(instancePath, item.Id));
            if (!removed)
                InstanceContentError = _localizer["instances.mods.deleteFailed"];
        }

        await LoadInstalledModsAsync();
    }

    [RelayCommand]
    private async Task CheckModUpdatesAsync()
    {
        if (_modManager is null || IsCheckingModUpdates ||
            _managedInstance?.IsInstalled != true)
        {
            return;
        }

        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        IsCheckingModUpdates = true;
        InstanceContentError = string.Empty;
        try
        {
            var updates = await Task.Run(
                () => _modManager.CheckInstanceModUpdatesAsync(instancePath));
            _modUpdatesById.Clear();
            foreach (var update in updates)
                _modUpdatesById[update.Id] = update;

            foreach (var item in InstalledMods)
            {
                item.UpdateVersion =
                    _modUpdatesById.TryGetValue(item.Id, out var update) &&
                    !string.IsNullOrWhiteSpace(update.LatestVersion)
                        ? update.LatestVersion
                        : _localizer["instances.mods.updateAvailableShort"];
            }

            ModUpdateCount = _modUpdatesById.Count;
        }
        finally
        {
            IsCheckingModUpdates = false;
        }
    }

    [RelayCommand]
    private async Task UpdateAllModsWithUpdatesAsync()
    {
        if (_modManager is null || IsApplyingModUpdates || !HasModUpdates ||
            _managedInstance?.IsInstalled != true)
        {
            return;
        }

        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        IsApplyingModUpdates = true;
        InstanceContentError = string.Empty;
        try
        {
            foreach (var (modId, update) in _modUpdatesById.ToList())
            {
                var installed = await _modManager.InstallModFileToInstanceAsync(
                    update.CurseForgeId,
                    update.LatestFileId,
                    instancePath);
                if (installed)
                    _modUpdatesById.Remove(modId);
                else
                    InstanceContentError = _localizer["instances.mods.installFailed"];
            }

            ModUpdateCount = _modUpdatesById.Count;
        }
        finally
        {
            IsApplyingModUpdates = false;
        }

        await LoadInstalledModsAsync();
    }

    [RelayCommand]
    private async Task ImportModsAsync()
    {
        if (_filePicker is null)
            return;

        var files = await _filePicker.BrowseModFilesAsync();
        await ImportModFilesAsync(files);
    }

    public async Task ImportModFilesAsync(IReadOnlyList<string>? filePaths)
    {
        if (_modManager is null || _managedInstance?.IsInstalled != true)
            return;

        var paths = (filePaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path) &&
                           (path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                            path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                            path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase) ||
                            path.EndsWith(".zip.disabled", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (paths.Count == 0)
            return;

        var instancePath = _instances.GetInstancePathById(_managedInstance.Id);
        if (string.IsNullOrWhiteSpace(instancePath))
            return;

        InstanceContentError = string.Empty;
        var imported = false;
        foreach (var sourcePath in paths)
        {
            try
            {
                if (await Task.Run(() => _modManager.InstallLocalModFile(sourcePath, instancePath)))
                    imported = true;
            }
            catch (Exception ex)
            {
                InstanceContentError = ex.Message;
            }
        }

        if (imported)
            await LoadInstalledModsAsync();
    }

    [RelayCommand]
    private Task OpenInstalledModPageAsync(InstanceModItemViewModel? item)
        => item is null
            ? Task.CompletedTask
            : OpenExternalAsync(item.CurseForgeUrl);

    [RelayCommand]
    private Task OpenCatalogModPageAsync(ModCatalogItemViewModel? item)
        => item is null
            ? Task.CompletedTask
            : OpenExternalAsync(item.CurseForgeUrl);

    private async Task OpenExternalAsync(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https")
        {
            await _uriLauncher.LaunchAsync(uri);
        }
    }

    private void UnsubscribeInstalledModItems()
    {
        foreach (var item in InstalledMods)
            item.PropertyChanged -= OnInstalledModItemPropertyChanged;
    }

    private void OnInstalledModItemPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is InstanceModItemViewModel &&
            args.PropertyName == nameof(InstanceModItemViewModel.IsSelected))
        {
            RecalculateInstalledModsSelection();
        }
    }

    private void RecalculateInstalledModsSelection()
    {
        SelectedModCount = InstalledMods.Count(mod => mod.IsSelected);
    }

    private void RestartModIconFetch()
    {
        _modIconsCancellation.Cancel();
        _modIconsCancellation.Dispose();
        _modIconsCancellation = new CancellationTokenSource();
    }

    private void FetchInstalledModIcons(IEnumerable<InstanceModItemViewModel> items)
    {
        if (_remoteImageCache is null)
            return;

        var token = _modIconsCancellation.Token;
        var targets = items
            .Where(item => !string.IsNullOrWhiteSpace(item.IconUrl) && item.Icon is null)
            .ToList();
        if (targets.Count == 0)
            return;

        _ = Task.Run(async () =>
        {
            foreach (var item in targets)
            {
                if (token.IsCancellationRequested)
                    return;

                try
                {
                    var bitmap = await RemoteNewsBitmap.LoadAsync(
                        item.IconUrl,
                        96,
                        _httpClient,
                        token,
                        _remoteImageCache);
                    if (bitmap is not null && !token.IsCancellationRequested)
                        Dispatcher.UIThread.Post(() => item.Icon = bitmap);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                }
            }
        }, token);
    }

    private void FetchCatalogModIcons(IEnumerable<ModCatalogItemViewModel> items)
    {
        if (_remoteImageCache is null)
            return;

        var token = _modIconsCancellation.Token;
        var targets = items
            .Where(item => !string.IsNullOrWhiteSpace(item.IconUrl) && item.Icon is null)
            .ToList();
        if (targets.Count == 0)
            return;

        _ = Task.Run(async () =>
        {
            foreach (var item in targets)
            {
                if (token.IsCancellationRequested)
                    return;

                try
                {
                    var bitmap = await RemoteNewsBitmap.LoadAsync(
                        item.IconUrl,
                        96,
                        _httpClient,
                        token,
                        _remoteImageCache);
                    if (bitmap is not null && !token.IsCancellationRequested)
                        Dispatcher.UIThread.Post(() => item.Icon = bitmap);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                }
            }
        }, token);
    }

    private async Task MoveModCatalogPreviewScreenshotAsync(int offset)
    {
        if (IsModCatalogPreviewImageTransitioning ||
            SelectedModCatalogPreview is not { ScreenshotUrls.Count: > 1 } item)
        {
            return;
        }

        var targetIndex = Math.Clamp(
            ModCatalogPreviewScreenshotIndex + offset,
            0,
            item.ScreenshotUrls.Count - 1);
        if (targetIndex == ModCatalogPreviewScreenshotIndex)
            return;

        _modPreviewImageTransitionCancellation.Cancel();
        _modPreviewImageTransitionCancellation.Dispose();
        _modPreviewImageTransitionCancellation = new CancellationTokenSource();
        var cancellationToken = _modPreviewImageTransitionCancellation.Token;
        IsModCatalogPreviewImageTransitioning = true;
        _isModCatalogPreviewImageFadingOut = true;
        IsModCatalogPreviewImageVisible = false;

        try
        {
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _isModCatalogPreviewImageFadingOut = false;
                ModCatalogPreviewScreenshotIndex = targetIndex;
                ApplyModCatalogPreviewScreenshot(_modCatalogPreviewVersion);
            }, DispatcherPriority.Render, cancellationToken);
            await Task.Delay(180, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                await Dispatcher.UIThread.InvokeAsync(
                    () => IsModCatalogPreviewImageTransitioning = false,
                    DispatcherPriority.Render);
            }
        }
    }

    private async Task RevealModCatalogPreviewFilesAsync(int previewVersion)
    {
        _modPreviewRevealCancellation.Cancel();
        _modPreviewRevealCancellation.Dispose();
        _modPreviewRevealCancellation = new CancellationTokenSource();
        var token = _modPreviewRevealCancellation.Token;
        try
        {
            await Task.Delay(ModCatalogPreviewFilesSkeletonMinMilliseconds, token)
                .ConfigureAwait(false);
            if (previewVersion != _modCatalogPreviewVersion)
                return;

            await Dispatcher.UIThread.InvokeAsync(
                () => IsModCatalogPreviewFilesSkeletonFadingOut = true,
                DispatcherPriority.Render);
            await Task.Delay(ArticleBodySkeletonFadeMilliseconds, token)
                .ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (previewVersion != _modCatalogPreviewVersion)
                    return;

                IsModCatalogPreviewFilesSkeletonVisible = false;
                IsModCatalogPreviewFilesSkeletonFadingOut = false;
            }, DispatcherPriority.Render);

            await Task.Delay(16, token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(
                () => IsModCatalogPreviewFilesContentVisible = true,
                DispatcherPriority.Render);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private void BeginModCatalogPreviewImagePreload(
        ModCatalogItemViewModel item,
        int previewVersion)
    {
        _modPreviewImageCancellation.Cancel();
        _modPreviewImageCancellation.Dispose();
        _modPreviewImageCancellation = new CancellationTokenSource();
        var token = _modPreviewImageCancellation.Token;

        DisposeModCatalogPreviewBitmaps();

        IReadOnlyList<string> urls = item.ScreenshotUrls.Count > 0
            ? item.ScreenshotUrls
            : string.IsNullOrWhiteSpace(item.IconUrl)
                ? []
                : [item.IconUrl];

        if (urls.Count == 0)
        {
            ModCatalogPreviewImage = null;
            IsModCatalogPreviewImageVisible = false;
            IsModCatalogPreviewImageLoading = false;
            return;
        }

        lock (_modCatalogPreviewBitmapsLock)
        {
            for (var index = 0; index < urls.Count; index++)
            {
                _modCatalogPreviewBitmaps.Add(null);
                _modCatalogPreviewBitmapSlotsResolved.Add(false);
            }
        }

        IsModCatalogPreviewImageLoading = true;
        _ = Task.Run(async () =>
        {
            using var gate = new SemaphoreSlim(3, 3);
            var loads = urls.Select(async (url, index) =>
            {
                await gate.WaitAsync(token);
                try
                {
                    var bitmap = await RemoteNewsBitmap.LoadAsync(
                        url,
                        720,
                        _httpClient,
                        token,
                        _remoteImageCache);
                    var stored = false;
                    lock (_modCatalogPreviewBitmapsLock)
                    {
                        if (!token.IsCancellationRequested &&
                            previewVersion == _modCatalogPreviewVersion &&
                            index < _modCatalogPreviewBitmaps.Count &&
                            !_modCatalogPreviewBitmapSlotsResolved[index])
                        {
                            _modCatalogPreviewBitmaps[index] = bitmap;
                            _modCatalogPreviewBitmapSlotsResolved[index] = true;
                            stored = true;
                        }
                    }

                    if (!stored)
                    {
                        bitmap?.Dispose();
                        return;
                    }

                    Dispatcher.UIThread.Post(
                        () => ApplyModCatalogPreviewScreenshot(previewVersion),
                        DispatcherPriority.Background);
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    gate.Release();
                }
            });

            try
            {
                await Task.WhenAll(loads);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void ApplyModCatalogPreviewScreenshot(int previewVersion)
    {
        if (previewVersion != _modCatalogPreviewVersion ||
            SelectedModCatalogPreview is null)
        {
            return;
        }

        Bitmap? bitmap = null;
        var resolved = false;
        lock (_modCatalogPreviewBitmapsLock)
        {
            if (ModCatalogPreviewScreenshotIndex < _modCatalogPreviewBitmaps.Count)
            {
                bitmap = _modCatalogPreviewBitmaps[ModCatalogPreviewScreenshotIndex];
                resolved = _modCatalogPreviewBitmapSlotsResolved[ModCatalogPreviewScreenshotIndex];
            }
        }

        IsModCatalogPreviewImageLoading = !resolved;
        ModCatalogPreviewImage = resolved
            ? bitmap ?? GetModCatalogPreviewFallbackBitmap()
            : null;
        IsModCatalogPreviewImageVisible = !_isModCatalogPreviewImageFadingOut &&
                                          ModCatalogPreviewImage is not null;
    }

    private Bitmap? GetModCatalogPreviewFallbackBitmap()
    {
        lock (_modCatalogPreviewBitmapsLock)
        {
            for (var offset = 1; offset < _modCatalogPreviewBitmaps.Count; offset++)
            {
                var candidate =
                    _modCatalogPreviewBitmaps[
                        (ModCatalogPreviewScreenshotIndex + offset) % _modCatalogPreviewBitmaps.Count];
                if (candidate is not null)
                    return candidate;
            }
        }

        return SelectedModCatalogPreview?.Icon;
    }

    private void DisposeModCatalogPreviewBitmaps()
    {
        lock (_modCatalogPreviewBitmapsLock)
        {
            foreach (var bitmap in _modCatalogPreviewBitmaps)
                bitmap?.Dispose();
            _modCatalogPreviewBitmaps.Clear();
            _modCatalogPreviewBitmapSlotsResolved.Clear();
        }

        ModCatalogPreviewImage = null;
        IsModCatalogPreviewImageVisible = false;
    }

    private void ResetModCatalogPreview()
    {
        _modCatalogPreviewVersion++;
        _modPreviewImageCancellation.Cancel();
        _modPreviewImageTransitionCancellation.Cancel();
        _modPreviewRevealCancellation.Cancel();
        IsModCatalogPreviewOpen = false;
        SelectedModCatalogPreview = null;
        SelectedModCatalogPreviewFile = null;
        _modCatalogPreviewFiles.Clear();
        IsModCatalogPreviewLoading = false;
        IsModCatalogPreviewImageLoading = false;
        _isModCatalogPreviewImageFadingOut = false;
        IsModCatalogPreviewImageVisible = false;
        IsModCatalogPreviewImageTransitioning = false;
        IsModCatalogPreviewFilesSkeletonVisible = false;
        IsModCatalogPreviewFilesSkeletonFadingOut = false;
        IsModCatalogPreviewFilesContentVisible = false;
        ModCatalogPreviewScreenshotIndex = 0;
        DisposeModCatalogPreviewBitmaps();
        OnPropertyChanged(nameof(HasModCatalogPreviewFiles));
        OnPropertyChanged(nameof(HasMultipleModCatalogPreviewScreenshots));
        OnPropertyChanged(nameof(CanInstallModCatalogPreview));
    }

    private string GetModReleaseLabel(int releaseType) => releaseType switch
    {
        2 => _localizer["modManager.releaseType.beta"],
        3 => _localizer["modManager.releaseType.alpha"],
        _ => _localizer["modManager.releaseType.release"]
    };

    private string GetModCompatibilityLabel(ModCompatibilityStatus compatibility) => compatibility switch
    {
        ModCompatibilityStatus.Compatible => _localizer["instances.mods.compatibility.compatible"],
        ModCompatibilityStatus.Incompatible => _localizer["instances.mods.compatibility.incompatible"],
        _ => _localizer["instances.mods.compatibility.unknown"]
    };

    private void NotifyCatalogSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCatalogModCount));
        OnPropertyChanged(nameof(HasSelectedCatalogMods));
        OnPropertyChanged(nameof(CanInstallSelectedCatalogMods));
        OnPropertyChanged(nameof(ModCatalogInstallSelectedLabel));
    }

    #endregion

    #region Mod catalog filters

    private async Task EnsureModCatalogFiltersAsync()
    {
        if (_modManager is null || _modCatalogFiltersLoaded ||
            ModCatalogCategories.Count > 0)
        {
            return;
        }

        _modCatalogFiltersLoaded = true;
        try
        {
            var categories = await _modManager.GetModCategoriesAsync();
            if (categories.Count == 0)
            {
                _modCatalogFiltersLoaded = false;
                return;
            }

            _loadedModCategories = categories;
            RebuildModCatalogCategories();
        }
        catch
        {
            _modCatalogFiltersLoaded = false;
        }
    }

    private void RebuildModCatalogCategories()
    {
        var selectedValue = SelectedModCatalogCategory?.Value;
        _suppressCatalogReload = true;
        ModCatalogCategories.Clear();
        ModCatalogCategories.Add(
            new InstanceListOptionViewModel("all", _localizer["instances.browse.categoryAll"]));
        foreach (var category in _loadedModCategories)
        {
            ModCatalogCategories.Add(new InstanceListOptionViewModel(
                category.Id.ToString(System.Globalization.CultureInfo.CurrentCulture),
                category.Name));
        }

        SelectedModCatalogCategory =
            ModCatalogCategories.FirstOrDefault(option =>
                string.Equals(option.Value, selectedValue, StringComparison.Ordinal)) ??
            ModCatalogCategories[0];
        _suppressCatalogReload = false;
    }

    private void BuildModCatalogSortOptions()
    {
        var selectedValue = SelectedModCatalogSort?.Value ?? "2";
        _suppressCatalogReload = true;
        ModCatalogSortOptions.Clear();
        ModCatalogSortOptions.Add(new InstanceListOptionViewModel("1", _localizer["instances.browse.sortRelevancy"]));
        ModCatalogSortOptions.Add(new InstanceListOptionViewModel("2", _localizer["instances.browse.sortPopularity"]));
        ModCatalogSortOptions.Add(new InstanceListOptionViewModel("3", _localizer["instances.browse.sortLatestUpdate"]));
        ModCatalogSortOptions.Add(new InstanceListOptionViewModel("11", _localizer["instances.browse.sortCreationDate"]));
        ModCatalogSortOptions.Add(new InstanceListOptionViewModel("6", _localizer["instances.browse.sortTotalDownloads"]));
        SelectedModCatalogSort =
            ModCatalogSortOptions.FirstOrDefault(option => option.Value == selectedValue) ??
            ModCatalogSortOptions[1];
        _suppressCatalogReload = false;
    }

    partial void OnSelectedModCatalogCategoryChanged(InstanceListOptionViewModel? value)
    {
        if (!_suppressCatalogReload)
            _ = SearchModCatalogAsync();
    }

    partial void OnSelectedModCatalogSortChanged(InstanceListOptionViewModel? value)
    {
        if (!_suppressCatalogReload)
            _ = SearchModCatalogAsync();
    }

    #endregion

    #region Game console

    partial void OnConsoleSearchQueryChanged(string value)
        => RebuildConsoleLines();

    private void OnConsoleLineReceived(object? sender, GameConsoleLineEventArgs e)
    {
        lock (_pendingConsoleLock)
        {
            _pendingConsoleLines.Add(e.Line);
            if (_pendingConsoleLines.Count > MaxConsoleLines * 2)
            {
                _pendingConsoleLines.RemoveRange(0, _pendingConsoleLines.Count - MaxConsoleLines);
            }
        }
    }

    private void OnConsoleFlushTimerTick(object? sender, EventArgs args)
        => FlushConsoleLines();

    private void FlushConsoleLines()
    {
        List<GameConsoleLine> pending;
        lock (_pendingConsoleLock)
        {
            if (_pendingConsoleLines.Count == 0)
                return;

            pending = [.. _pendingConsoleLines];
            _pendingConsoleLines.Clear();
        }

        var instanceId = _managedInstance?.Id;
        if (string.IsNullOrWhiteSpace(instanceId))
            return;

        var filter = ConsoleSearchQuery.Trim();
        foreach (var line in pending.Where(line =>
                     string.Equals(line.InstanceId, instanceId, StringComparison.Ordinal) &&
                     MatchesConsoleFilter(line, filter)))
        {
            _consoleLines.Add(ToConsoleLineViewModel(line));
        }

        while (_consoleLines.Count > MaxConsoleLines)
            _consoleLines.RemoveAt(0);

        ConsoleRevision++;
        NotifyConsoleStateChanged();
    }

    private void RebuildConsoleLines()
    {
        lock (_pendingConsoleLock)
            _pendingConsoleLines.Clear();

        var instanceId = _managedInstance?.Id;
        if (string.IsNullOrWhiteSpace(instanceId) || _gameConsole is null)
        {
            _consoleLines.ReplaceRange([]);
            ConsoleRevision++;
            NotifyConsoleStateChanged();
            return;
        }

        var filter = ConsoleSearchQuery.Trim();
        var lines = _gameConsole
            .GetLines(instanceId)
            .Where(line => MatchesConsoleFilter(line, filter))
            .ToList();
        var skip = Math.Max(0, lines.Count - MaxConsoleLines);
        _consoleLines.ReplaceRange(lines.Skip(skip).Select(ToConsoleLineViewModel));
        ConsoleRevision++;
        NotifyConsoleStateChanged();
    }

    private void PrepareConsoleForCurrentInstance()
    {
        _consoleLoadedForInstanceId = _managedInstance?.Id;
        RebuildConsoleLines();
    }

    [RelayCommand]
    private void ClearConsole()
    {
        if (_managedInstance is { } instance)
            _gameConsole?.Clear(instance.Id);

        RebuildConsoleLines();
    }

    private static bool MatchesConsoleFilter(GameConsoleLine line, string filter)
        => string.IsNullOrEmpty(filter) ||
           line.Text.Contains(filter, StringComparison.CurrentCultureIgnoreCase);

    private static ConsoleLineViewModel ToConsoleLineViewModel(GameConsoleLine line)
        => new(
            line.Level,
            line.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
            line.Text);

    private void NotifyConsoleStateChanged()
    {
        OnPropertyChanged(nameof(HasConsoleLines));
        OnPropertyChanged(nameof(IsConsoleEmpty));
        OnPropertyChanged(nameof(ConsoleLineCountText));
        OnPropertyChanged(nameof(ConsoleStatusText));
        OnPropertyChanged(nameof(IsConsoleRunning));
    }

    #endregion

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RunManagedInstanceAsync()
    {
        if (_managedInstance is not { } instance || !CanRunManagedInstanceAction)
            return;

        if (IsManagedInstanceActionActive)
        {
            if (!IsManagedInstanceCancellationArmed)
                return;

            if (IsManagedInstanceRunning)
                _gameProcess.ExitGame(instance.Id);
            else
                CancelActivity();

            return;
        }

        _managedInstanceActionInstanceId = instance.Id;
        _managedInstanceActionStartedAtUtc = DateTime.UtcNow;
        _managedInstanceGameStartedAtUtc = null;
        _managedInstanceActionStartedWithInstall = !instance.IsInstalled;
        var actionGeneration = ++_managedInstanceActionGeneration;
        _isManagedInstanceCancellationArmed = false;
        BeginInstanceActivity(instance.Id);
        CanCancelActivity = true;
        IsActivityVisible = true;
        ActivityProgress = 0;
        ActivityProgressText = "0%";
        ActivityTitle = _localizer["common.loading"];
        ActivityDetail = instance.Name;
        NotifyManagedInstanceActionStateChanged();
        _managedInstanceActionTimer.Start();

        try
        {
            if (instance.IsInstalled)
            {
                await Task.Run(() => _gameLaunchCoordinator.LaunchAsync(
                    instance.Id,
                    authorizationUriPresenter: _uriLauncher.LaunchAsync));
            }
            else
            {
                var result = await Task.Run(() => _installationWorkflow.DownloadAndLaunchInstanceAsync(
                    instance.Id,
                    _uriLauncher.LaunchAsync));
                if (!result.Success && !result.Cancelled && !string.IsNullOrWhiteSpace(result.Error))
                    ShowError(result.Error);
            }
        }
        finally
        {
            if (actionGeneration == _managedInstanceActionGeneration)
            {
                CanCancelActivity = false;
                RefreshInstances();
                if (!IsManagedInstanceRunning)
                    EndManagedInstanceAction();
                else
                    NotifyManagedInstanceActionStateChanged();
            }

            if (!_completedManagedActivityGenerations.Remove(actionGeneration))
                EndInstanceActivity(instance.Id);
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
        RefreshManagedInstanceContent();
    }

    [RelayCommand]
    private async Task PrimaryActionAsync()
    {
        if (_selectedInstance is not { } instance)
        {
            Navigate(InstancesPage);
            return;
        }

        if (_gameProcess.IsInstanceRunning(instance.Id))
        {
            _gameProcess.ExitGame(instance.Id);
            return;
        }

        if (IsInstanceBusy(instance.Id))
        {
            if (CanCancelActivity)
                CancelActivity();

            return;
        }

        BeginInstanceActivity(instance.Id);
        CanCancelActivity = !instance.IsInstalled;
        IsActivityVisible = true;
        ActivityProgress = 0;
        ActivityProgressText = "0%";
        ActivityTitle = _localizer["common.loading"];
        ActivityDetail = instance.Name;
        UpdateSelectedInstancePresentation();

        try
        {
            if (instance.IsInstalled)
            {
                await Task.Run(() => _gameLaunchCoordinator.LaunchAsync(
                    instance.Id,
                    authorizationUriPresenter: _uriLauncher.LaunchAsync));
            }
            else
            {
                _instances.SetSelectedInstance(instance.Id);
                var result = await Task.Run(() =>
                    _installationWorkflow.DownloadAndLaunchAsync(_uriLauncher.LaunchAsync));

                if (!result.Success && !result.Cancelled && !string.IsNullOrWhiteSpace(result.Error))
                    ShowError(result.Error);
            }
        }
        finally
        {
            CanCancelActivity = false;
            RefreshInstances();
            EndInstanceActivity(instance.Id);
        }
    }

    [RelayCommand]
    private void CancelActivity()
    {
        var instanceId = _managedInstanceActionInstanceId ?? _selectedInstance?.Id;
        if (!string.IsNullOrWhiteSpace(instanceId))
            _installationWorkflow.CancelDownload(instanceId);
    }

    private bool IsInstanceBusy(string instanceId)
        => !string.IsNullOrWhiteSpace(instanceId)
        && _busyInstanceCounts.TryGetValue(instanceId, out var count)
        && count > 0;

    private void BeginInstanceActivity(string instanceId)
    {
        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            _busyInstanceCounts.TryGetValue(instanceId, out var currentCount);
            _busyInstanceCounts[instanceId] = currentCount + 1;
        }
        IsBusy = _busyInstanceCounts.Count > 0;
        UpdateSelectedInstancePresentation();
        NotifyManagedInstanceActionStateChanged();
    }

    private void EndInstanceActivity(string instanceId)
    {
        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            if (_busyInstanceCounts.TryGetValue(instanceId, out var currentCount))
            {
                if (currentCount <= 1)
                    _busyInstanceCounts.Remove(instanceId);
                else
                    _busyInstanceCounts[instanceId] = currentCount - 1;
            }
        }
        IsBusy = _busyInstanceCounts.Count > 0;
        UpdateSelectedInstancePresentation();
        NotifyManagedInstanceActionStateChanged();
    }

    private void OnInstancesChanged()
    {
        // Raised synchronously from repository mutations; skip when the change was
        // triggered by our own resync, which rebuilds right after it returns
        if (_suppressInstancesChanged)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                RebuildInstancesFromCache();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        });
    }

    private void RefreshInstances()
    {
        try
        {
            _suppressInstancesChanged = true;
            try
            {
                _instances.SyncInstancesWithConfig();
            }
            finally
            {
                _suppressInstancesChanged = false;
            }

            RebuildInstancesFromCache();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void RebuildInstancesFromCache()
    {
        var items = _instances.GetCachedInstances();
        var selectedInstanceId = _instances.GetSelectedInstance()?.Id;
        var requestedManagedInstanceId = _managedInstance?.Id;
        var managedInstance = items.FirstOrDefault(instance =>
                string.Equals(instance.Id, requestedManagedInstanceId, StringComparison.Ordinal))
            ?? items.FirstOrDefault();
        var managedInstanceId = managedInstance?.Id;

        var presentedInstances = items
            .Select(instance =>
            {
                RefreshInstanceInstalledState(instance);

                return new InstanceItemViewModel(
                    instance.Id,
                    instance.Name,
                    FormatVersion(instance.Version),
                    FormatBranch(instance.Branch),
                    instance.IsInstalled,
                    string.Equals(instance.Id, managedInstanceId, StringComparison.Ordinal));
            })
            .ToList();

        _allInstances.ReplaceRange(presentedInstances);

        _selectedInstance = items.FirstOrDefault(instance =>
            string.Equals(instance.Id, selectedInstanceId, StringComparison.Ordinal));
        _managedInstance = managedInstance;

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
        ResetModCatalogPreview();
        InstanceWorlds.Clear();
        _modsLoadedForInstanceId = null;
        _worldsLoadedForInstanceId = null;
        _modUpdatesById.Clear();
        ModUpdateCount = 0;
        SelectedModCount = 0;
        InstanceContentError = string.Empty;
        RestartModIconFetch();
        RebuildConsoleLines();
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

            UnsubscribeInstalledModItems();
            _installedMods.ReplaceRange(mods
                .OrderBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(mod =>
                {
                    var item = new InstanceModItemViewModel(
                        mod.Id,
                        mod.Name,
                        string.IsNullOrWhiteSpace(mod.Version) ? _localizer["common.unknown"] : mod.Version,
                        string.IsNullOrWhiteSpace(mod.Author) ? _localizer["common.unknown"] : mod.Author,
                        mod.Enabled,
                        mod.IconUrl,
                        mod.CurseForgeId,
                        mod.ReleaseType);
                    if (_modUpdatesById.TryGetValue(mod.Id, out var update))
                        item.UpdateVersion = update.LatestVersion;
                    item.PropertyChanged += OnInstalledModItemPropertyChanged;
                    return item;
                }));

            FilterInstalledMods();
            RefreshCatalogInstalledState(mods);
            FetchInstalledModIcons(_installedMods);
            RecalculateInstalledModsSelection();
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

    private async Task LoadModCatalogAsync(string query, bool append)
    {
        if (_modManager is null || _managedInstance?.IsInstalled != true)
            return;

        var instanceId = _managedInstance.Id;
        if (append)
            IsLoadingMoreModCatalog = true;
        else
        {
            IsModCatalogLoading = true;
            ResetModCatalogPreview();
        }
        InstanceContentError = string.Empty;
        try
        {
            var page = append ? _modCatalogPage + 1 : 0;
            var categories = SelectedModCatalogCategory is { Value: var categoryValue } &&
                             !string.IsNullOrWhiteSpace(categoryValue) &&
                             categoryValue != "all"
                ? new[] { categoryValue }
                : [];
            var sortField = int.TryParse(SelectedModCatalogSort?.Value, out var parsedSort)
                ? parsedSort
                : 2;
            var result = await _modManager.SearchModsAsync(
                query.Trim(),
                page,
                ModCatalogPageSize,
                categories,
                sortField,
                1);
            if (!string.Equals(_managedInstance?.Id, instanceId, StringComparison.Ordinal))
                return;

            var instancePath = _instances.GetInstancePathById(instanceId);
            var installed = string.IsNullOrWhiteSpace(instancePath)
                ? []
                : _modManager.GetInstanceInstalledMods(instancePath);
            _modCatalogGameVersion = string.IsNullOrWhiteSpace(instancePath)
                ? null
                : ModCompatibilityEvaluator.DetectInstanceGameVersion(instancePath);
            OnPropertyChanged(nameof(ModCatalogGameVersionLabel));
            var items = result.Mods.Select(mod =>
            {
                var installedMod = FindInstalledCatalogMod(mod.Id, installed);
                var recommendedFile = ModCompatibilityEvaluator.SelectRecommendedFile(
                    mod.LatestFiles,
                    _modCatalogGameVersion);
                var compatibility = recommendedFile is not null
                    ? ModCompatibilityEvaluator.Evaluate(
                        _modCatalogGameVersion,
                        recommendedFile.GameVersions)
                    : mod.LatestFiles.Count > 0
                        ? ModCompatibilityStatus.Incompatible
                        : ModCompatibilityStatus.Unknown;
                return new ModCatalogItemViewModel(
                    mod.Id,
                    mod.Name,
                    string.IsNullOrWhiteSpace(mod.Author) ? _localizer["common.unknown"] : mod.Author,
                    mod.Summary,
                    mod.LatestFileId,
                    mod.Slug,
                    mod.IconUrl,
                    mod.DownloadCount,
                    recommendedFile?.ReleaseType ?? 1,
                    screenshotUrls: mod.Screenshots
                        .Select(screenshot => screenshot.Url ?? screenshot.ThumbnailUrl ?? string.Empty)
                        .Where(url => !string.IsNullOrWhiteSpace(url))
                        .ToList(),
                    installedFileId: installedMod?.FileId ?? string.Empty,
                    recommendedFileId: recommendedFile?.Id ??
                        (mod.LatestFiles.Count == 0 ? mod.LatestFileId : string.Empty),
                    compatibility: compatibility,
                    compatibilityLabel: GetModCompatibilityLabel(compatibility))
                {
                    IsInstalled = installedMod is not null
                };
            }).ToList();

            if (append)
                _modCatalogItems.AddRange(items);
            else
                _modCatalogItems.ReplaceRange(items);

            _modCatalogPage = page;
            HasMoreModCatalog = _modCatalogItems.Count < result.TotalCount && items.Count > 0;
            FetchCatalogModIcons(items);
            NotifyInstanceContentCollectionsChanged();
            NotifyCatalogSelectionChanged();
        }
        catch (Exception ex)
        {
            InstanceContentError = ex.Message;
        }
        finally
        {
            IsModCatalogLoading = false;
            IsLoadingMoreModCatalog = false;
        }
    }

    private void FilterInstalledMods()
    {
        var query = InstalledModsSearchQuery.Trim();
        _visibleInstalledMods.ReplaceRange(InstalledMods.Where(mod =>
            string.IsNullOrWhiteSpace(query) ||
            mod.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            mod.Author.Contains(query, StringComparison.CurrentCultureIgnoreCase)));

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

            _instanceWorlds.ReplaceRange(worlds);
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
        {
            var installedMod = FindInstalledCatalogMod(item.Id, installedMods);
            item.IsInstalled = installedMod is not null;
            item.InstalledFileId = installedMod?.FileId ?? string.Empty;
            if (item.IsInstalled)
                item.IsSelected = false;
        }

        NotifyCatalogSelectionChanged();
    }

    private static InstalledMod? FindInstalledCatalogMod(
        string catalogId,
        IEnumerable<InstalledMod> installedMods)
        => installedMods.FirstOrDefault(mod =>
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
        OnPropertyChanged(nameof(InstanceModsFooterText));
        OnPropertyChanged(nameof(HasModSelection));
        OnPropertyChanged(nameof(SelectedModCountText));
    }

    private async Task LoadInstanceVersionsAsync(string branch)
    {
        CancelInstanceVersionLoading();

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

    private void ResetInstanceCreatorState()
    {
        CancelInstanceVersionLoading();
        NewInstanceBranch = "release";
        SelectedNewInstanceVersion = null;
        AvailableInstanceVersions.Clear();
        IsInstanceVersionsLoading = false;
        InstanceCreationError = string.Empty;
        OnPropertyChanged(nameof(HasAvailableInstanceVersions));
    }

    private void CancelInstanceVersionLoading()
    {
        _instanceVersionsCancellation?.Cancel();
        _instanceVersionsCancellation?.Dispose();
        _instanceVersionsCancellation = null;
    }

    private void ApplyAvailableInstanceVersions(IReadOnlyList<int> versions)
    {
        SelectedNewInstanceVersion = null;
        var selectedVersion = versions.FirstOrDefault();
        _availableInstanceVersions.ReplaceRange(versions
            .Take(12)
            .Select(version => new InstanceVersionItemViewModel(
                version,
                IsSelected: version == selectedVersion)));

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

    /// <summary>
    /// Activates the startup overlay before the window is presented
    /// </summary>
    public void BeginStartupLoading()
    {
        StartupLoadingStatus = _localizer["startup.loading.content"];
        IsStartupLoading = true;
    }

    /// <summary>
    /// Preloads dynamic launcher content while the startup overlay is visible
    /// </summary>
    /// <param name="cancellationToken">Cancellation requested when the desktop exits</param>
    public async Task PreloadStartupDataAsync(CancellationToken cancellationToken)
    {
        StartupLoadingStatus = _localizer["startup.loading.content"];
        await Task.WhenAll(
            LoadNewsAsync(waitForImages: true, cancellationToken: cancellationToken),
            Settings.PreloadAboutDataAsync(cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        StartupLoadingStatus = _localizer["startup.loading.ready"];
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
    }

    /// <summary>
    /// Starts the transition from the startup overlay to the launcher shell
    /// </summary>
    public void CompleteStartupLoading()
        => IsStartupLoading = false;

    private async Task LoadNewsAsync(
        bool waitForImages = false,
        CancellationToken cancellationToken = default)
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
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var item in _allNews)
                item.Dispose();
            _allNews.Clear();
            _allNews.AddRange(news.Select(item =>
                new NewsItemViewModel(item, _uriLauncher, OpenNewsArticleAsync)));
            _canLoadMoreNews = news.Count == InitialNewsCount;
            _hasLoadedNews = true;
            PresentNews();

            if (waitForImages)
            {
                _newsImagesCancellation.Cancel();
                _newsImagesCancellation.Dispose();
                _newsImagesCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                await LoadNewsImagesAsync(_allNews.ToArray(), _newsImagesCancellation.Token);
            }
            else
            {
                RestartNewsImageLoading();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
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
        _latestNews.ReplaceRange(_allNews.Skip(1));

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

        var previousArticle = SelectedNewsArticle;
        var loadVersion = ++_articleLoadVersion;
        _newsImagesCancellation.Cancel();
        _articleImagesCancellation.Cancel();
        _articlePresentationCancellation.Cancel();
        BeginArticleBodyPreparation();
        previousArticle?.ResetRenderedBlocks();
        previousArticle?.ReleaseImages();
        IsNewsArticleScrolled = false;

        foreach (var newsItem in _allNews)
            newsItem.IsSelected = ReferenceEquals(newsItem, item);
        SelectedNewsItem = item;
        NewsArticleError = string.Empty;
        IsNewsArticleSkeletonVisible = false;
        IsNewsArticleLoading = true;

        if (_articleViewModelCache.TryGetValue(item.Url, out var cachedArticle))
        {
            // Clear before exposing a cached model so Avalonia never realizes the full
            // rich tree synchronously during the SelectedNewsArticle binding change.
            cachedArticle.ResetRenderedBlocks();
            SelectedNewsArticle = cachedArticle;
            NotifyNewsStateChanged();
            StartCompactArticleTransition();
            await RestartArticlePresentationAsync(cachedArticle);
            if (loadVersion == _articleLoadVersion)
            {
                IsNewsArticleLoading = false;
                NotifyNewsStateChanged();
            }
            return;
        }

        if (IsCompactNewsLayout || previousArticle is null)
            SelectedNewsArticle = null;
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
                ShowNewsArticleError(_localizer["news.articleLoadFailed"]);
                return;
            }

            var articleViewModel = await Task.Run(
                () => new NewsArticleViewModel(article, _uriLauncher));
            if (loadVersion != _articleLoadVersion)
            {
                articleViewModel.Dispose();
                return;
            }

            IsNewsArticleSkeletonVisible = false;
            SelectedNewsArticle = articleViewModel;
            CacheArticleViewModel(item.Url, articleViewModel);
            await RestartArticlePresentationAsync(articleViewModel);
        }
        catch (ArgumentException ex)
        {
            ShowNewsArticleError(ex.Message);
        }
        catch (Exception)
        {
            ShowNewsArticleError(_localizer["news.articleLoadFailed"]);
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

        SelectedNewsArticle?.ResetRenderedBlocks();
        SelectedNewsArticle?.ReleaseImages();
        IsNewsArticleBodySkeletonVisible = false;
        IsNewsArticleBodySkeletonFadingOut = false;
        IsNewsArticleBodyVisible = false;
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
        _ = article.LoadImagesAsync(
            _httpClient,
            _articleImagesCancellation.Token,
            _remoteImageCache);
    }

    private void CacheArticleViewModel(string url, NewsArticleViewModel article)
    {
        if (_articleViewModelCache.TryGetValue(url, out var replaced) &&
            !ReferenceEquals(replaced, article))
        {
            replaced.Dispose();
        }

        _articleViewModelCache[url] = article;
        while (_articleViewModelCache.Count > MaximumCachedNewsArticles)
        {
            var candidate = _articleViewModelCache.FirstOrDefault(pair =>
                !ReferenceEquals(pair.Value, SelectedNewsArticle));
            if (string.IsNullOrEmpty(candidate.Key))
                return;

            _articleViewModelCache.Remove(candidate.Key);
            candidate.Value.Dispose();
        }
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
            await PrepareArticleForDisplayAsync(
                    article,
                    cancellationToken,
                    () => RevealNewsArticleBodyAsync(cancellationToken))
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(
                () => RestartArticleImageLoading(article),
                DispatcherPriority.Background,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task PrepareArticleForDisplayAsync(
        NewsArticleViewModel article,
        CancellationToken cancellationToken,
        Func<Task>? contentReady)
    {
        try
        {
            await article.PrepareForDisplayAsync(cancellationToken, contentReady)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void BeginArticleBodyPreparation()
    {
        IsNewsArticleBodySkeletonFadingOut = false;
        IsNewsArticleBodySkeletonVisible = true;
        IsNewsArticleBodyVisible = false;
    }

    private void ShowNewsArticleError(string message)
    {
        IsNewsArticleBodySkeletonVisible = false;
        IsNewsArticleBodySkeletonFadingOut = false;
        IsNewsArticleBodyVisible = false;
        SelectedNewsArticle = null;
        NewsArticleError = message;
    }

    private async Task RevealNewsArticleBodyAsync(CancellationToken cancellationToken)
    {
        await Dispatcher.UIThread.InvokeAsync(
            () => IsNewsArticleBodySkeletonFadingOut = true,
            DispatcherPriority.Render);
        await Task.Delay(ArticleBodySkeletonFadeMilliseconds, cancellationToken)
            .ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsNewsArticleBodySkeletonVisible = false;
            IsNewsArticleBodySkeletonFadingOut = false;
        }, DispatcherPriority.Render, cancellationToken);

        await Task.Delay(16, cancellationToken).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(
            () => IsNewsArticleBodyVisible = true,
            DispatcherPriority.Render,
            cancellationToken);
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
                    await item.LoadImageAsync(
                        _httpClient,
                        cancellationToken,
                        _remoteImageCache);
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
        var selectedInstanceIsBusy = IsInstanceBusy(_selectedInstance.Id);
        PrimaryActionText = selectedInstanceIsBusy
            ? CanCancelActivity
                ? _localizer["main.cancel"]
                : _localizer["common.loading"]
            : IsSelectedInstanceRunning
                ? _localizer["main.stop"]
                : _selectedInstance.IsInstalled
                    ? _localizer["main.play"]
                    : _localizer["main.download"];
        CanRunPrimaryAction = !selectedInstanceIsBusy || CanCancelActivity;
        NotifyPrimaryActionStateChanged();
    }

    private void UpdateManagedInstancePresentation()
    {
        OnPropertyChanged(nameof(IsManagedInstanceInstalled));
        OnPropertyChanged(nameof(CanRunManagedInstanceAction));
        OnPropertyChanged(nameof(CanOpenManagedInstanceFolder));
        OnPropertyChanged(nameof(CanDeleteManagedInstance));
        OnPropertyChanged(nameof(ManagedInstanceActionLabel));
        NotifyManagedInstanceActionStateChanged();
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
        Profiles.RefreshLocalization();
        AccountType = _isOfficialProfile
            ? _localizer["desktopSettings.accountHytale"]
            : _localizer["desktopSettings.accountOffline"];

        foreach (var item in _allNews)
            item.RefreshCulture();
        foreach (var article in _articleViewModelCache.Values)
            article.RefreshCulture();

        if (_loadedModCategories.Count > 0)
            RebuildModCatalogCategories();
        BuildModCatalogSortOptions();
        NotifyConsoleStateChanged();

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

    private string FormatManagedInstanceActionElapsedTime()
    {
        var startedAt = IsManagedInstanceActionRunning
            ? _managedInstanceGameStartedAtUtc ?? _gameProcess.GetRunningProcesses()
                .FirstOrDefault(process => string.Equals(
                    process.InstanceId,
                    _managedInstance?.Id,
                    StringComparison.OrdinalIgnoreCase))
                ?.ProcessStartedAtUtc
            : _managedInstanceActionStartedAtUtc;
        if (startedAt is null)
            return "0:00";

        var duration = DateTime.UtcNow - startedAt.Value;
        var totalHours = (long)duration.TotalHours;
        return totalHours > 0
            ? $"{totalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";
    }

    private void OnManagedInstanceActionTimerTick(object? sender, EventArgs args)
        => OnPropertyChanged(nameof(ManagedInstanceActionMetricText));

    private void NotifyManagedInstanceActionStateChanged()
    {
        OnPropertyChanged(nameof(IsManagedInstanceActionActive));
        OnPropertyChanged(nameof(IsManagedInstanceActionRunning));
        OnPropertyChanged(nameof(ShouldSpinManagedInstanceAction));
        OnPropertyChanged(nameof(IsManagedInstanceCancellationArmed));
        OnPropertyChanged(nameof(CanRunManagedInstanceAction));
        OnPropertyChanged(nameof(CanDeleteManagedInstance));
        OnPropertyChanged(nameof(ManagedInstanceActionStatusText));
        OnPropertyChanged(nameof(ManagedInstanceActionMetricText));
    }

    private void EndManagedInstanceAction()
    {
        _managedInstanceActionTimer.Stop();
        _managedInstanceActionStartedAtUtc = null;
        _managedInstanceGameStartedAtUtc = null;
        _managedInstanceActionInstanceId = null;
        _managedInstanceActionStartedWithInstall = false;
        _isManagedInstanceCancellationArmed = false;
        NotifyManagedInstanceActionStateChanged();
    }

    public void ArmManagedInstanceCancellation()
    {
        if (!IsManagedInstanceActionActive || _isManagedInstanceCancellationArmed)
            return;

        _isManagedInstanceCancellationArmed = true;
        OnPropertyChanged(nameof(IsManagedInstanceCancellationArmed));
    }

    private void OnDownloadProgressChanged(ProgressUpdateMessage update)
    {
        Interlocked.Exchange(ref _pendingProgressUpdate, update);
        SchedulePendingProgressUpdate();
    }

    private void SchedulePendingProgressUpdate()
    {
        if (Interlocked.CompareExchange(ref _progressUpdateScheduled, 1, 0) != 0)
            return;

        Dispatcher.UIThread.Post(ApplyPendingProgressUpdate, DispatcherPriority.Background);
    }

    private void ApplyPendingProgressUpdate()
    {
        var update = Interlocked.Exchange(ref _pendingProgressUpdate, null);
        if (update is not null)
        {
            var activeInstanceId = _managedInstanceActionInstanceId ?? _selectedInstance?.Id;
            if (IsBusy && (update.InstanceId is null ||
                           string.Equals(update.InstanceId, activeInstanceId, StringComparison.OrdinalIgnoreCase)))
            {
                ActivityProgress = Math.Clamp(update.Progress, 0, 100);
                ActivityProgressText = $"{ActivityProgress:0}%";
                ActivityTitle = update.Args is { Length: > 0 }
                    ? _localizer.Format(update.MessageKey, update.Args)
                    : _localizer[update.MessageKey];
                ActivityDetail = update.State;
                IsActivityVisible = true;
            }
        }

        Interlocked.Exchange(ref _progressUpdateScheduled, 0);
        if (Volatile.Read(ref _pendingProgressUpdate) is not null)
            SchedulePendingProgressUpdate();
    }

    private void OnGameProcessStarted(object? sender, GameProcessStartedEventArgs e)
    {
        var process = e.Process;
        Dispatcher.UIThread.Post(() =>
        {
            if (string.Equals(
                _managedInstanceActionInstanceId,
                process.InstanceId,
                StringComparison.OrdinalIgnoreCase))
            {
                _managedInstanceGameStartedAtUtc ??= DateTime.UtcNow;
            }

            IsGameRunning = _gameProcess.IsGameRunning();
            IsActivityVisible = false;

            UpdateSelectedInstancePresentation();
            NotifyManagedInstanceActionStateChanged();
            NotifyConsoleStateChanged();
        });
    }

    private void OnGameProcessExited(object? sender, GameProcessExitedEventArgs e)
    {
        var process = e.Process;
        Dispatcher.UIThread.Post(() =>
        {
            var endsManagedAction = IsManagedInstanceActionActive &&
                string.Equals(
                    _managedInstanceActionInstanceId,
                    process.InstanceId,
                    StringComparison.OrdinalIgnoreCase);
            if (endsManagedAction)
                _completedManagedActivityGenerations.Add(_managedInstanceActionGeneration);
            CanCancelActivity = false;
            EndInstanceActivity(process.InstanceId);

            IsGameRunning = _gameProcess.IsGameRunning();
            IsActivityVisible = false;

            UpdateSelectedInstancePresentation();
            if (endsManagedAction)
                EndManagedInstanceAction();
            else
                NotifyManagedInstanceActionStateChanged();
            NotifyConsoleStateChanged();
        });
    }

    private void OnLaunchFailed(object? sender, LaunchFailedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var instanceId = e.InstanceId
                ?? _managedInstanceActionInstanceId
                ?? _selectedInstance?.Id;
            var endsManagedAction = IsManagedInstanceActionActive &&
                string.Equals(
                    _managedInstanceActionInstanceId,
                    instanceId,
                    StringComparison.OrdinalIgnoreCase);
            if (endsManagedAction)
                _completedManagedActivityGenerations.Add(_managedInstanceActionGeneration);
            CanCancelActivity = false;
            if (!string.IsNullOrWhiteSpace(instanceId))
                EndInstanceActivity(instanceId);

            IsActivityVisible = false;

            UpdateSelectedInstancePresentation();
            if (endsManagedAction)
                EndManagedInstanceAction();
            else
                NotifyManagedInstanceActionStateChanged();
        });
    }

    private void OnOperationErrorOccurred(OperationErrorMessage error)
    {
        var activeInstanceId = _managedInstanceActionInstanceId ?? _selectedInstance?.Id;
        if (error.InstanceId is not null &&
            !string.Equals(error.InstanceId, activeInstanceId, StringComparison.OrdinalIgnoreCase))
            return;

        Dispatcher.UIThread.Post(() => ShowError(error.Technical ?? error.Message));
    }

    private void OnBackgroundChanged(string? mode)
        => _ = ReplaceDashboardBackgroundAsync(mode);

    private async Task ReplaceDashboardBackgroundAsync(string? mode)
    {
        var loadVersion = Interlocked.Increment(ref _backgroundLoadVersion);
        var backgroundUri = ResolveDashboardBackgroundUri(mode);
        Bitmap replacement;
        try
        {
            replacement = await Task.Run(() => LoadDashboardBackground(backgroundUri))
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Logger.Warning("Background", $"Failed to load dashboard background: {exception.Message}");
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_isDisposed || loadVersion != Volatile.Read(ref _backgroundLoadVersion))
            {
                replacement.Dispose();
                return;
            }

            var previous = DashboardBackground;
            DashboardBackground = replacement;
            previous?.Dispose();
        }, DispatcherPriority.Background);
    }

    private Uri ResolveDashboardBackgroundUri(string? mode)
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

        return new Uri($"avares://HyPrism.Desktop/Assets/Backgrounds/{selected}");
    }

    private static Bitmap LoadDashboardBackground(Uri uri)
    {
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
        CanCancelActivity = false;
        UpdateSelectedInstancePresentation();
    }

    private void OnActiveProfileChanged(object? sender, ActiveProfileChangedEventArgs e)
    {
        UserName = e.Name;
        UserInitial = string.IsNullOrWhiteSpace(UserName)
            ? "H"
            : UserName[..1].ToUpperInvariant();
        _isOfficialProfile = e.IsOfficial;
        AccountType = _isOfficialProfile
            ? _localizer["desktopSettings.accountHytale"]
            : _localizer["desktopSettings.accountOffline"];
    }

    private void NotifyPageStateChanged()
    {
        OnPropertyChanged(nameof(IsDashboard));
        OnPropertyChanged(nameof(IsInstances));
        OnPropertyChanged(nameof(IsNews));
        OnPropertyChanged(nameof(IsProfiles));
        OnPropertyChanged(nameof(IsSettings));
        OnPropertyChanged(nameof(IsPlaceholderPage));
        OnPropertyChanged(nameof(IsNewsLandingVisible));
        OnPropertyChanged(nameof(IsNewsArticleVisible));
        OnPropertyChanged(nameof(IsNewsArticleStatusVisible));
        OnPropertyChanged(nameof(IsNewsFeedVisible));
        OnPropertyChanged(nameof(IsNewsArticleContext));
        OnPropertyChanged(nameof(IsNewsArticleEmpty));
        OnPropertyChanged(nameof(CompactNewsPageIndex));
    }

    partial void OnIsBusyChanged(bool value)
        => NotifyManagedInstanceActionStateChanged();

    partial void OnIsGameRunningChanged(bool value)
        => NotifyManagedInstanceActionStateChanged();

    partial void OnActivityTitleChanged(string value)
        => OnPropertyChanged(nameof(ManagedInstanceActionStatusText));

    partial void OnActivityProgressTextChanged(string value)
        => OnPropertyChanged(nameof(ManagedInstanceActionMetricText));

    partial void OnInstalledModsSearchQueryChanged(string value)
        => FilterInstalledMods();

    public void Dispose()
    {
        _isDisposed = true;
        Interlocked.Increment(ref _backgroundLoadVersion);
        _managedInstanceActionTimer.Stop();
        _managedInstanceActionTimer.Tick -= OnManagedInstanceActionTimerTick;
        _consoleFlushTimer.Stop();
        _consoleFlushTimer.Tick -= OnConsoleFlushTimerTick;
        if (_gameConsole is not null)
            _gameConsole.LineReceived -= OnConsoleLineReceived;
        UnsubscribeInstalledModItems();
        RestartModIconFetch();
        _modIconsCancellation.Dispose();
        _modPreviewImageCancellation.Cancel();
        _modPreviewImageCancellation.Dispose();
        _modPreviewImageTransitionCancellation.Cancel();
        _modPreviewImageTransitionCancellation.Dispose();
        _modPreviewRevealCancellation.Cancel();
        _modPreviewRevealCancellation.Dispose();
        DisposeModCatalogPreviewBitmaps();
        _newsImagesCancellation.Cancel();
        _newsImagesCancellation.Dispose();
        _articleImagesCancellation.Cancel();
        _articleImagesCancellation.Dispose();
        _articlePresentationCancellation.Cancel();
        _articlePresentationCancellation.Dispose();
        _compactNewsTransitionCancellation.Cancel();
        _compactNewsTransitionCancellation.Dispose();
        CancelInstanceVersionLoading();
        SelectedNewsArticle = null;
        foreach (var article in _articleViewModelCache.Values)
            article.Dispose();
        _articleViewModelCache.Clear();
        foreach (var item in _allNews)
            item.Dispose();
        Interlocked.Exchange(ref _pendingProgressUpdate, null);
        _progress.DownloadProgressChanged -= OnDownloadProgressChanged;
        _progress.OperationErrorOccurred -= OnOperationErrorOccurred;
        _gameProcess.GameProcessStarted -= OnGameProcessStarted;
        _gameProcess.GameProcessExited -= OnGameProcessExited;
        _gameLaunchCoordinator.LaunchFailed -= OnLaunchFailed;
        _instances.InstancesChanged -= OnInstancesChanged;
        _settingsStore.BackgroundChanged -= OnBackgroundChanged;
        _localizer.LanguageChanged -= ApplyLanguage;
        _profiles.ActiveProfileChanged -= OnActiveProfileChanged;
        _profiles.Dispose();
        Settings.Dispose();
        DashboardBackground?.Dispose();
        DashboardBackground = null;
    }
}
