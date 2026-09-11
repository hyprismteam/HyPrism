// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPrism.Desktop.Controls;
using HyPrism.Desktop.Localization;
using HyPrism.Desktop.Platform;

namespace HyPrism.Desktop.Features.News;

public sealed partial class NewsViewModel : ObservableObject, IDisposable
{
    private const int InitialNewsCount = 12;
    private const int NewsPageSize = 8;
    private const int MaximumNewsCount = 30;
    private const int MaximumCachedNewsArticles = 2;
    private const int CompactArticleAnimationMilliseconds = 300;
    private const int CompactTransitionMilliseconds = 320;
    private const int ArticleSkeletonDelayMilliseconds = 180;
    private const int ArticleBodySkeletonFadeMilliseconds = 180;

    private readonly IHytaleNewsClient _newsClient;
    private readonly IExternalUriLauncher _uriLauncher;
    private readonly HttpClient _httpClient;
    private readonly RemoteImageCache? _remoteImageCache;
    private readonly StringLocalizer _localizer;
    private readonly List<NewsItemViewModel> _allNews = [];
    private readonly Dictionary<string, NewsArticleViewModel> _articleViewModelCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableRangeCollection<NewsItemViewModel> _latestNews = [];
    private CancellationTokenSource _newsImagesCancellation = new();
    private CancellationTokenSource _articleImagesCancellation = new();
    private CancellationTokenSource _articlePresentationCancellation = new();
    private CancellationTokenSource _compactNewsTransitionCancellation = new();
    private bool _hasLoadedNews;
    private bool _canLoadMoreNews = true;
    private int _articleLoadVersion;
    private long _compactNewsTransitionReadyAt;

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
    private bool _isCompactNewsArticleClosing;

    [ObservableProperty]
    private bool _isNewsArticleScrolled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMoreNews))]
    [NotifyPropertyChangedFor(nameof(CanShowLoadMore))]
    private bool _isLoadingMoreNews;

    [ObservableProperty]
    private int _compactNewsPageIndex;

    public NewsViewModel(
        IHytaleNewsClient newsClient,
        IExternalUriLauncher uriLauncher,
        HttpClient httpClient,
        StringLocalizer localizer,
        RemoteImageCache? remoteImageCache = null)
    {
        _newsClient = newsClient;
        _uriLauncher = uriLauncher;
        _httpClient = httpClient;
        _localizer = localizer;
        _remoteImageCache = remoteImageCache;
    }

    public ObservableCollection<NewsItemViewModel> LatestNews => _latestNews;
    public bool HasLoadedNews => _hasLoadedNews;
    public bool IsNews => true;

    public string NewsLoadingLabel => _localizer["news.loading"];
    public string NewsEmptyLabel => _localizer["news.noNewsFound"];
    public string BackLabel => _localizer["common.back"];
    public string OpenOriginalLabel => _localizer["news.readMore"];
    public string ArticleLoadingLabel => _localizer["news.articleLoading"];
    public string SelectArticleLabel => _localizer["news.selectArticle"];
    public string LoadMoreLabel => _localizer["news.loadMore"];

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

    public void RefreshLocalization()
    {
        foreach (var item in _allNews)
            item.RefreshCulture();
        foreach (var article in _articleViewModelCache.Values)
            article.RefreshCulture();

        NotifyNewsStateChanged();
        OnPropertyChanged(string.Empty);
    }

    public async Task LoadAsync(
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
                RestartImageLoading();
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
        IsCompactNewsArticleClosing = false;
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
                () => new NewsArticleViewModel(article, _uriLauncher, _localizer));
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

        // Keep article realization behind the compact detail transition so the first
        // frame only has to animate the already laid out reader shell
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
        IsCompactNewsArticleClosing = IsCompactNewsLayout;
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
        IsCompactNewsArticleClosing = false;
        NotifyNewsStateChanged();
        RestartImageLoading();
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
            Environment.TickCount64 + CompactArticleAnimationMilliseconds;
        IsCompactNewsTransitionActive = true;
        _ = CompleteCompactNewsTransitionAsync(_compactNewsTransitionCancellation.Token);
    }

    private async Task CompleteCompactNewsTransitionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(CompactTransitionMilliseconds, cancellationToken).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() => IsCompactNewsTransitionActive = false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public void RestartImageLoading()
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
    }
}
