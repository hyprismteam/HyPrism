// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPrism.Desktop.Localization;
using HyPrism.Models;
using HyPrism.Services.Core.App;
using HyPrism.Services.Core.Integration;
using HyPrism.Services.Core.Platform;
using HyPrism.Services.Game;
using HyPrism.Services.Game.Instance;
using HyPrism.Services.Game.Launch;
using HyPrism.Services.User;

namespace HyPrism.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private const string DashboardPage = "dashboard";
    private const string InstancesPage = "instances";
    private const string ModsPage = "mods";
    private const string NewsPage = "news";
    private const string ProfilesPage = "profiles";
    private const string SettingsPage = "settings";
    private const int InitialNewsCount = 12;
    private const int NewsPageSize = 8;
    private const int MaximumNewsCount = 30;
    private const int CompactTransitionMilliseconds = 320;
    private const int ArticleSkeletonDelayMilliseconds = 180;

    private readonly IInstanceService _instanceService;
    private readonly IGameLaunchCoordinator _gameLaunchCoordinator;
    private readonly IGameSessionService _gameSessionService;
    private readonly IGameProcessService _gameProcessService;
    private readonly IProgressNotificationService _progressService;
    private readonly ISettingsService _settingsService;
    private readonly INewsService _newsService;
    private readonly IBrowserService _browserService;
    private readonly IFileDialogService? _fileDialogService;
    private readonly IGitHubService? _gitHubService;
    private readonly HttpClient _httpClient;
    private readonly LocalizationService _localizer;
    private InstanceInfo? _selectedInstance;
    private readonly List<NewsItemViewModel> _allNews = [];
    private readonly Dictionary<string, NewsArticleViewModel> _articleViewModelCache =
        new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource _newsImagesCancellation = new();
    private CancellationTokenSource _articleImagesCancellation = new();
    private CancellationTokenSource _articlePresentationCancellation = new();
    private CancellationTokenSource _compactNewsTransitionCancellation = new();
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
    private Bitmap? _dashboardBackground;

    [ObservableProperty]
    private string _primaryActionText = string.Empty;

    [ObservableProperty]
    private bool _canRunPrimaryAction;

    [ObservableProperty]
    private bool _canCancelActivity;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
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
    [NotifyPropertyChangedFor(nameof(IsDashboardQuickStripVisible))]
    private bool _isCompactDashboardLayout;

    public MainWindowViewModel(
        IInstanceService instanceService,
        IProfileService profileService,
        IProfileManagementService profileManagementService,
        IGameLaunchCoordinator gameLaunchCoordinator,
        IGameSessionService gameSessionService,
        IGameProcessService gameProcessService,
        IProgressNotificationService progressService,
        ISettingsService settingsService,
        INewsService newsService,
        IBrowserService browserService,
        HttpClient httpClient,
        LocalizationService localizer,
        IFileDialogService? fileDialogService = null,
        IGitHubService? gitHubService = null)
    {
        _instanceService = instanceService;
        _gameLaunchCoordinator = gameLaunchCoordinator;
        _gameSessionService = gameSessionService;
        _gameProcessService = gameProcessService;
        _progressService = progressService;
        _settingsService = settingsService;
        _newsService = newsService;
        _browserService = browserService;
        _fileDialogService = fileDialogService;
        _gitHubService = gitHubService;
        _httpClient = httpClient;
        _localizer = localizer;
        _localizer.LanguageChanged += ApplyLanguage;
        _settingsService.OnBackgroundChanged += OnBackgroundChanged;
        _settings = CreateSettingsViewModel();
        DashboardBackground = LoadDashboardBackground(_settingsService.GetBackgroundMode());

        UserName = profileService.GetNick();
        UserInitial = string.IsNullOrWhiteSpace(UserName)
            ? "H"
            : UserName[..1].ToUpperInvariant();
        _isOfficialProfile = profileManagementService.GetSelectedProfile()?.IsOfficial == true;
        AccountType = _isOfficialProfile
            ? _localizer["desktopSettings.accountHytale"]
            : _localizer["desktopSettings.accountOffline"];

        _progressService.DownloadProgressChanged += OnDownloadProgressChanged;
        _progressService.GameStateChanged += OnGameStateChanged;
        _progressService.ErrorOccurred += OnErrorOccurred;

        CurrentPageTitle = DashboardLabel;
        RefreshInstances();
    }

    public ObservableCollection<InstanceItemViewModel> Instances { get; } = [];
    public ObservableCollection<NewsItemViewModel> LatestNews { get; } = [];
    public SettingsViewModel Settings
    {
        get => _settings;
        private set => SetProperty(ref _settings, value);
    }

    public string DashboardLabel => _localizer["dock.dashboard"];
    public string InstancesLabel => _localizer["dock.instances"];
    public string ModsLabel => _localizer["dock.mods"];
    public string NewsLabel => _localizer["dock.news"];
    public string ProfilesLabel => _localizer["dock.profiles"];
    public string SettingsLabel => _localizer["dock.settings"];
    public string SelectInstanceLabel => _localizer["main.selectInstance"];
    public string VersionLabel => _localizer["common.version"];
    public string BranchLabel => _localizer["common.branch"];
    public string InstancesSectionLabel => _localizer["instances.title"];
    public string AddInstanceLabel => _localizer["instances.addInstance"];
    public string NewsLoadingLabel => _localizer["news.loading"];
    public string NewsEmptyLabel => _localizer["news.noNewsFound"];
    public string BackLabel => _localizer["common.back"];
    public string OpenOriginalLabel => _localizer["news.readMore"];
    public string ArticleLoadingLabel => _localizer["news.articleLoading"];
    public string SelectArticleLabel => _localizer["news.selectArticle"];
    public string LoadMoreLabel => _localizer["news.loadMore"];

    public bool IsDashboard => CurrentPage == DashboardPage;
    public bool IsInstances => CurrentPage == InstancesPage;
    public bool IsMods => CurrentPage == ModsPage;
    public bool IsNews => CurrentPage == NewsPage;
    public bool IsProfiles => CurrentPage == ProfilesPage;
    public bool IsSettings => CurrentPage == SettingsPage;
    public bool IsPlaceholderPage => !IsDashboard && !IsNews && !IsSettings;
    public bool HasDashboardInstances => Instances.Count > 0;
    public bool IsDashboardQuickStripVisible =>
        HasDashboardInstances && !IsCompactDashboardLayout;
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

    [RelayCommand]
    private void Navigate(string? page)
    {
        CurrentPage = page switch
        {
            InstancesPage => InstancesPage,
            ModsPage => ModsPage,
            NewsPage => NewsPage,
            ProfilesPage => ProfilesPage,
            SettingsPage => SettingsPage,
            _ => DashboardPage
        };

        CurrentPageTitle = CurrentPage switch
        {
            InstancesPage => InstancesLabel,
            ModsPage => ModsLabel,
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
    private void OpenInstances()
        => Navigate(InstancesPage);

    [RelayCommand]
    private void SelectDashboardInstance(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId) || IsBusy || IsGameRunning ||
            string.Equals(_selectedInstance?.Id, instanceId, StringComparison.Ordinal))
        {
            return;
        }

        var instance = _instanceService.GetCachedInstances()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Id, instanceId, StringComparison.Ordinal));
        if (instance is null)
            return;

        _instanceService.SetSelectedInstance(instance.Id);
        _selectedInstance = instance;
        RefreshInstances();
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
            _gameProcessService.ExitGame();
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
        UpdateSelectedInstancePresentation();

        try
        {
            if (_selectedInstance.IsInstalled)
            {
                await _gameLaunchCoordinator.LaunchAsync(_selectedInstance.Id);
            }
            else
            {
                _instanceService.SetSelectedInstance(_selectedInstance.Id);
                var result = await _gameSessionService.DownloadAndLaunchAsync(
                    () => _settingsService.GetLaunchAfterDownload());

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
        => _gameSessionService.CancelDownload();

    private void RefreshInstances()
    {
        try
        {
            _instanceService.SyncInstancesWithConfig();
            var items = _instanceService.GetCachedInstances();
            _selectedInstance = _instanceService.GetSelectedInstance()
                ?? items.FirstOrDefault(instance => instance.IsInstalled)
                ?? items.FirstOrDefault();

            if (_selectedInstance is not null && _instanceService.GetSelectedInstance() is null)
                _instanceService.SetSelectedInstance(_selectedInstance.Id);

            Instances.Clear();
            foreach (var instance in items
                         .OrderByDescending(instance => instance.Id == _selectedInstance?.Id)
                         .Take(3))
            {
                var path = _instanceService.GetInstancePathById(instance.Id);
                var installed = !string.IsNullOrWhiteSpace(path) && _instanceService.IsClientPresent(path);
                instance.IsInstalled = installed;

                Instances.Add(new InstanceItemViewModel(
                    instance.Id,
                    instance.Name,
                    FormatVersion(instance.Version),
                    FormatBranch(instance.Branch),
                    installed,
                    instance.Id == _selectedInstance?.Id));
            }

            OnPropertyChanged(nameof(HasDashboardInstances));
            OnPropertyChanged(nameof(IsDashboardQuickStripVisible));

            if (_selectedInstance is not null)
            {
                var path = _instanceService.GetInstancePathById(_selectedInstance.Id);
                _selectedInstance.IsInstalled =
                    !string.IsNullOrWhiteSpace(path) && _instanceService.IsClientPresent(path);
            }

            UpdateSelectedInstancePresentation();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async Task LoadNewsAsync()
    {
        if (_hasLoadedNews || IsNewsLoading)
            return;

        IsNewsLoading = true;
        NewsError = string.Empty;

        try
        {
            var news = (await _newsService.GetNewsAsync(InitialNewsCount, NewsSource.Hytale))
                .Where(item => string.Equals(item.Source, "hytale", StringComparison.OrdinalIgnoreCase))
                .Take(InitialNewsCount)
                .ToList();

            foreach (var item in _allNews)
                item.Dispose();
            _allNews.Clear();
            _allNews.AddRange(news.Select(item =>
                new NewsItemViewModel(item, _browserService, OpenNewsArticleAsync)));
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
            var response = (await _newsService.GetNewsAsync(requestedCount, NewsSource.Hytale))
                .Where(item => string.Equals(item.Source, "hytale", StringComparison.OrdinalIgnoreCase))
                .Take(requestedCount)
                .ToList();
            var knownUrls = _allNews
                .Select(item => item.Url)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = response
                .Where(item => knownUrls.Add(item.Url))
                .Select(item => new NewsItemViewModel(item, _browserService, OpenNewsArticleAsync))
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
            var article = await _newsService.GetNewsArticleAsync(item.Url);
            if (loadVersion != _articleLoadVersion)
                return;

            if (article is null)
            {
                NewsArticleError = _localizer["news.articleLoadFailed"];
                return;
            }

            var articleViewModel = await Task.Run(
                () => new NewsArticleViewModel(article, _browserService));
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
        if (_selectedInstance is null)
        {
            SelectedInstanceName = SelectInstanceLabel;
            SelectedInstanceMeta = _localizer["instances.noInstances"];
            SelectedInstanceState = _localizer["instances.status.unknown"];
            PrimaryActionText = SelectInstanceLabel;
            CanRunPrimaryAction = !IsBusy;
            NotifyPrimaryActionStateChanged();
            return;
        }

        SelectedInstanceName = _selectedInstance.Name;
        SelectedInstanceMeta = $"{FormatBranch(_selectedInstance.Branch)}  ·  {FormatVersion(_selectedInstance.Version)}";
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

    private void NotifyPrimaryActionStateChanged()
    {
        OnPropertyChanged(nameof(IsPrimarySelectAction));
        OnPropertyChanged(nameof(IsPrimaryStopAction));
        OnPropertyChanged(nameof(IsPrimaryDownloadAction));
        OnPropertyChanged(nameof(IsPrimaryPlayAction));
        OnPropertyChanged(nameof(IsPrimaryPendingAction));
    }

    private SettingsViewModel CreateSettingsViewModel()
        => new(_settingsService, _browserService, _localizer, _fileDialogService, _gitHubService);

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
            ModsPage => ModsLabel,
            NewsPage => NewsLabel,
            ProfilesPage => ProfilesLabel,
            SettingsPage => SettingsLabel,
            _ => DashboardLabel
        };
        UpdateSelectedInstancePresentation();
        OnPropertyChanged(string.Empty);
    }

    private string FormatVersion(int version)
        => version <= 0 ? _localizer["common.latest"] : $"v{version}";

    private string FormatBranch(string branch)
        => branch.Contains("pre", StringComparison.OrdinalIgnoreCase)
            ? _localizer["common.preRelease"]
            : _localizer["common.release"];

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
            IsGameRunning = state is "started" or "running";
            if (state is "started" or "running" or "stopped")
                IsActivityVisible = false;

            UpdateSelectedInstancePresentation();
        });
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
        var available = _settingsService.GetAvailableBackgrounds() ?? [];
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
        OnPropertyChanged(nameof(IsMods));
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
        SelectedNewsArticle = null;
        foreach (var article in _articleViewModelCache.Values)
            article.Dispose();
        _articleViewModelCache.Clear();
        foreach (var item in _allNews)
            item.Dispose();
        _progressService.DownloadProgressChanged -= OnDownloadProgressChanged;
        _progressService.GameStateChanged -= OnGameStateChanged;
        _progressService.ErrorOccurred -= OnErrorOccurred;
        _settingsService.OnBackgroundChanged -= OnBackgroundChanged;
        _localizer.LanguageChanged -= ApplyLanguage;
        Settings.Dispose();
        DashboardBackground?.Dispose();
        DashboardBackground = null;
    }
}
