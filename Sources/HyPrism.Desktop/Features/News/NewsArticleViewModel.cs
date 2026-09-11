// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Web;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPrism.Desktop.Controls;
using HyPrism.Desktop.Localization;
using HyPrism.Desktop.Platform;

namespace HyPrism.Desktop.Features.News;

public sealed class NewsArticleViewModel : ObservableObject, IDisposable
{
    private const int RenderBatchSize = 4;
    private static readonly ConcurrentDictionary<string, Lazy<Task<string?>>> YouTubeTitleCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim YouTubeMetadataGate = new(4, 4);
    private readonly IExternalUriLauncher _uriLauncher;
    private readonly StringLocalizer _localizer;
    private readonly string _publishedAt;
    private readonly ObservableRangeCollection<NewsArticleBlockViewModel> _renderedBlocks = [];
    private string _date = string.Empty;
    private string _metadata = string.Empty;

    public NewsArticleViewModel(
        NewsArticleResponse article,
        IExternalUriLauncher uriLauncher,
        StringLocalizer? localizer = null)
    {
        _uriLauncher = uriLauncher;
        _localizer = localizer ?? new StringLocalizer("en-US");
        Title = article.Title;
        Excerpt = article.Excerpt;
        Url = article.Url;
        Author = article.Author;
        _publishedAt = article.PublishedAt;
        Categories = string.Join("  ·  ", article.Categories);
        OpenLinkCommand = new AsyncRelayCommand<string?>(OpenLinkAsync);
        Blocks = NewsArticleBlockViewModel.Create(article.Content, OpenLinkCommand, _localizer);
        OpenOriginalCommand = new AsyncRelayCommand(OpenOriginalAsync);
        RefreshCulture();
    }

    public string Title { get; }
    public string Excerpt { get; }
    public string Url { get; }
    public string Author { get; }
    public string Date
    {
        get => _date;
        private set => SetProperty(ref _date, value);
    }
    public string Categories { get; }
    public string Metadata
    {
        get => _metadata;
        private set => SetProperty(ref _metadata, value);
    }
    public IReadOnlyList<NewsArticleBlockViewModel> Blocks { get; }
    public ObservableCollection<NewsArticleBlockViewModel> RenderedBlocks => _renderedBlocks;
    public IRelayCommand OpenOriginalCommand { get; }
    public IRelayCommand<string?> OpenLinkCommand { get; }
    public bool HasExcerpt => !string.IsNullOrWhiteSpace(Excerpt);
    public bool HasAuthor => !string.IsNullOrWhiteSpace(Author);
    public bool HasCategories => !string.IsNullOrWhiteSpace(Categories);
    public bool HasContent => Blocks.Count > 0;

    internal static async Task<string?> LoadYouTubeTitleAsync(
        HttpClient httpClient,
        string? watchUrl,
        CancellationToken cancellationToken)
    {
        if (!TryGetYouTubeVideoId(watchUrl, out var videoId))
            return null;

        var pending = YouTubeTitleCache.GetOrAdd(
            videoId,
            id => new Lazy<Task<string?>>(
                () => FetchYouTubeTitleAsync(httpClient, id),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return await pending.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> FetchYouTubeTitleAsync(HttpClient httpClient, string videoId)
    {
        await YouTubeMetadataGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var videoUrl = $"https://www.youtube.com/watch?v={videoId}";
            var endpoint =
                $"https://www.youtube.com/oembed?url={Uri.EscapeDataString(videoUrl)}&format=json";
            var json = await httpClient.GetStringAsync(endpoint).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("title", out var title) &&
                   title.ValueKind == JsonValueKind.String
                ? title.GetString()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            YouTubeMetadataGate.Release();
        }
    }

    private static bool TryGetYouTubeVideoId(string? value, out string videoId)
    {
        videoId = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.Equals("www.youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate = HttpUtility.ParseQueryString(uri.Query)["v"];
        if (candidate is not { Length: 11 } ||
            !candidate.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
        {
            return false;
        }

        videoId = candidate;
        return true;
    }

    public void ResetRenderedBlocks()
        => RenderedBlocks.Clear();

    public async Task PrepareForDisplayAsync(
        CancellationToken cancellationToken,
        Func<Task>? contentReady = null)
    {
        await Dispatcher.UIThread.InvokeAsync(() => _renderedBlocks.Clear());
        if (Blocks.Count == 0)
        {
            if (contentReady is not null)
                await contentReady().ConfigureAwait(false);
            return;
        }

        for (var offset = 0; offset < Blocks.Count; offset += RenderBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = Blocks.Skip(offset).Take(RenderBatchSize).ToArray();
            await Dispatcher.UIThread.InvokeAsync(
                () => _renderedBlocks.AddRange(batch),
                DispatcherPriority.Background,
                cancellationToken);

            if (offset + RenderBatchSize < Blocks.Count)
                await Task.Delay(16, cancellationToken).ConfigureAwait(false);
        }

        // Wait behind the rich-text jobs queued by the final group before the
        // document becomes interactive and exposes its stable scroll extent
        await Dispatcher.UIThread.InvokeAsync(
            static () => { },
            DispatcherPriority.Background,
            cancellationToken);

        if (contentReady is not null)
            await contentReady().ConfigureAwait(false);
    }

    public void ReleaseImages()
    {
        foreach (var block in Blocks)
            block.ReleaseImages();
    }

    public void RefreshCulture()
    {
        Date = NewsItemViewModel.FormatDate(_publishedAt, _publishedAt);
        Metadata = string.Join(
            "  ·  ",
            new[] { Author, Categories, Date }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        foreach (var block in Blocks)
            block.RefreshCulture(_localizer);
    }

    public async Task LoadImagesAsync(
        HttpClient httpClient,
        CancellationToken cancellationToken,
        RemoteImageCache? imageCache = null)
    {
        using var concurrencyGate = new SemaphoreSlim(1, 1);
        try
        {
            await Task.WhenAll(Blocks.Select(async block =>
            {
                if (!block.HasRemoteImages)
                    return;

                await concurrencyGate.WaitAsync(cancellationToken);
                try
                {
                    await block.LoadImageAsync(httpClient, cancellationToken, imageCache);
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

    private Task OpenOriginalAsync()
        => LaunchExternalAsync(Url);

    private Task OpenLinkAsync(string? url)
        => LaunchExternalAsync(url);

    private Task<bool> LaunchExternalAsync(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
           (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? _uriLauncher.LaunchAsync(uri)
            : Task.FromResult(false);

    public void Dispose()
    {
        RenderedBlocks.Clear();
        foreach (var block in Blocks)
            block.Dispose();
    }
}

public sealed partial class NewsArticleBlockViewModel : ObservableObject, IDisposable
{
    private NewsArticleBlockViewModel(
        NewsContentNode node,
        ICommand? linkCommand,
        StringLocalizer localizer)
    {
        Kind = node.Kind;
        LinkCommand = linkCommand;
        RefreshCulture(localizer);

        if (IsDetails)
        {
            var summary = node.Children.FirstOrDefault(child => child.Kind == "summary");
            DetailsSummaryNodes = summary?.Children.Count > 0
                ? summary.Children
                : summary is null
                    ? []
                    : [summary];
            DetailsBlocks = Create(
                node.Children.Where(child => child.Kind != "summary"),
                linkCommand);
            Nodes = DetailsSummaryNodes;
        }
        else
        {
            Nodes = node.Children.Count > 0 ? node.Children : [node];
        }

        InlineImages = FindInlineImages(Nodes)
            .Where(image => !string.IsNullOrWhiteSpace(image.ImageUrl))
            .GroupBy(image => image.ImageUrl, StringComparer.OrdinalIgnoreCase)
            .Select(group => new NewsInlineImageViewModel(group.First()))
            .ToList();
        if (IsDetails)
        {
            var summaryTextNodes = DetailsSummaryNodes
                .Where(child => child.Kind != "inline-image")
                .ToList();
            if (summaryTextNodes.LastOrDefault() is { Kind: "text", Text: { } trailingText })
            {
                summaryTextNodes[^1] = new NewsContentNode
                {
                    Kind = "text",
                    Text = trailingText.TrimEnd()
                };
            }

            DetailsSummaryTextNodes = summaryTextNodes;
            DetailsSummaryImages = InlineImages;
        }
        ConfigureStickerParagraph();
        Url = node.Url;
        ImageUrl = node.ImageUrl;
        AltText = node.AltText ?? string.Empty;
        HeadingLevel = node.Level ?? 2;
        PlainText = IsCode ? ExtractText(node) : string.Empty;
        if (IsList)
            ListItems = CreateListItems(node, linkCommand);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    [NotifyPropertyChangedFor(nameof(HasNoImage))]
    private Bitmap? _image;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVideoTitle))]
    private string _videoTitle = "YouTube";

    [ObservableProperty]
    private string _watchOnYouTubeLabel = string.Empty;

    [ObservableProperty]
    private string _openInBrowserLabel = string.Empty;

    public string Kind { get; }
    public IReadOnlyList<NewsContentNode> Nodes { get; }
    public IReadOnlyList<NewsInlineImageViewModel> InlineImages { get; }
    public NewsInlineImageViewModel? StickerImage { get; private set; }
    public IReadOnlyList<NewsContentNode> StickerLeadNodes { get; private set; } = [];
    public IReadOnlyList<NewsContentNode> StickerBodyNodes { get; private set; } = [];
    public IReadOnlyList<NewsArticleListItemViewModel> ListItems { get; private set; } = [];
    public IReadOnlyList<NewsContentNode> DetailsSummaryNodes { get; private set; } = [];
    public IReadOnlyList<NewsContentNode> DetailsSummaryTextNodes { get; private set; } = [];
    public IReadOnlyList<NewsInlineImageViewModel> DetailsSummaryImages { get; private set; } = [];
    public IReadOnlyList<NewsArticleBlockViewModel> DetailsBlocks { get; private set; } = [];
    public string? Url { get; }
    public string? ImageUrl { get; }
    public bool HasVideoTitle => !string.IsNullOrWhiteSpace(VideoTitle);
    public void RefreshCulture(StringLocalizer localizer)
    {
        WatchOnYouTubeLabel = localizer["news.watchOnYouTube"];
        OpenInBrowserLabel = localizer["news.openInBrowser"];
    }
    public string AltText { get; }
    public string PlainText { get; }
    public int HeadingLevel { get; }
    public ICommand? LinkCommand { get; }
    public double HeadingSize => HeadingLevel switch
    {
        1 => 32,
        2 => 27,
        3 => 23,
        _ => 20
    };

    public bool IsParagraph =>
        Kind is ("paragraph" or "container" or "text") && !IsStickerParagraph;
    public bool IsStickerParagraph => StickerImage is not null;
    public bool IsHeading => Kind == "heading";
    public bool IsImage => Kind == "image";
    public bool IsYouTube => Kind == "youtube";
    public bool IsQuote => Kind == "blockquote";
    public bool IsDetails => Kind == "details";
    public bool IsList => Kind is "unordered-list" or "ordered-list";
    public bool IsCode => Kind is "code-block" or "inline-code";
    public bool IsDivider => Kind == "divider";
    public bool IsCaption => Kind == "caption";
    public bool HasImage => Image is not null;
    public bool HasNoImage => Image is null;
    public bool HasRemoteImages =>
        IsImage || IsYouTube || InlineImages.Count > 0 || DetailsBlocks.Any(block => block.HasRemoteImages);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailsCollapsed))]
    [NotifyPropertyChangedFor(nameof(VisibleDetailsBlocks))]
    private bool _isDetailsExpanded;

    public bool IsDetailsCollapsed => !IsDetailsExpanded;
    public IReadOnlyList<NewsArticleBlockViewModel> VisibleDetailsBlocks =>
        IsDetailsExpanded ? DetailsBlocks : [];

    [RelayCommand]
    private void ToggleDetails()
        => IsDetailsExpanded = !IsDetailsExpanded;

    public static IReadOnlyList<NewsArticleBlockViewModel> Create(
        IEnumerable<NewsContentNode> nodes,
        ICommand? linkCommand = null,
        StringLocalizer? localizer = null)
    {
        localizer ??= new StringLocalizer("en-US");
        var blocks = new List<NewsArticleBlockViewModel>();
        foreach (var node in nodes)
            AddBlock(node, blocks, linkCommand, localizer);
        return blocks;
    }

    private static void AddBlock(
        NewsContentNode node,
        ICollection<NewsArticleBlockViewModel> blocks,
        ICommand? linkCommand,
        StringLocalizer localizer)
    {
        if (node.Kind == "paragraph" && node.Children.Any(child =>
                child.Kind is "image" or "youtube"))
        {
            var inlineNodes = new List<NewsContentNode>();
            foreach (var child in node.Children)
            {
                if (child.Kind is not ("image" or "youtube"))
                {
                    inlineNodes.Add(child);
                    continue;
                }

                AddInlineParagraph(inlineNodes, blocks, linkCommand, localizer);
                inlineNodes.Clear();
                AddBlock(child, blocks, linkCommand, localizer);
            }

            AddInlineParagraph(inlineNodes, blocks, linkCommand, localizer);
            return;
        }

        var containsBlockChildren = node.Kind == "container" && node.Children.Any(child =>
            child.Kind is "paragraph" or "heading" or "image" or "blockquote" or
                "unordered-list" or "ordered-list" or "figure" or "table" or "youtube" or "divider");
        if (containsBlockChildren ||
            node.Kind is "figure" or "table" or "table-row" or "table-cell" or "table-header")
        {
            foreach (var child in node.Children)
                AddBlock(child, blocks, linkCommand, localizer);
            return;
        }

        if (node.Kind == "text" && string.IsNullOrWhiteSpace(node.Text))
            return;

        blocks.Add(new NewsArticleBlockViewModel(node, linkCommand, localizer));
    }

    private static void AddInlineParagraph(
        IReadOnlyCollection<NewsContentNode> inlineNodes,
        ICollection<NewsArticleBlockViewModel> blocks,
        ICommand? linkCommand,
        StringLocalizer localizer)
    {
        if (inlineNodes.Count == 0 || inlineNodes.All(node =>
                node.Kind == "text" && string.IsNullOrWhiteSpace(node.Text)))
        {
            return;
        }

        blocks.Add(new NewsArticleBlockViewModel(new NewsContentNode
        {
            Kind = "paragraph",
            Children = inlineNodes.ToList()
        }, linkCommand, localizer));
    }

    private static string ExtractText(NewsContentNode node)
        => !string.IsNullOrEmpty(node.Text)
            ? node.Text
            : string.Concat(node.Children.Select(ExtractText));

    private static IEnumerable<NewsContentNode> FindInlineImages(
        IEnumerable<NewsContentNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Kind == "inline-image")
                yield return node;

            foreach (var child in FindInlineImages(node.Children))
                yield return child;
        }
    }

    private static IReadOnlyList<NewsArticleListItemViewModel> CreateListItems(
        NewsContentNode list,
        ICommand? linkCommand)
    {
        var ordered = list.Kind == "ordered-list";
        return list.Children
            .Where(child => child.Kind == "list-item")
            .Select((item, index) =>
            {
                var nestedLists = item.Children
                    .Where(child => child.Kind is "unordered-list" or "ordered-list")
                    .ToList();
                var inlineNodes = item.Children
                    .Where(child => child.Kind is not ("unordered-list" or "ordered-list"))
                    .ToList();
                var children = nestedLists
                    .SelectMany(nested => CreateListItems(nested, linkCommand))
                    .ToList();

                return new NewsArticleListItemViewModel(
                    ordered ? $"{index + 1}." : "•",
                    inlineNodes,
                    children,
                    linkCommand);
            })
            .ToList();
    }

    private void ConfigureStickerParagraph()
    {
        if (Kind != "paragraph")
            return;

        var stickerIndex = -1;
        for (var index = 0; index < Nodes.Count; index++)
        {
            var node = Nodes[index];
            if (node.Kind != "inline-image" ||
                !string.Equals(
                    node.ImagePresentation,
                    "sticker",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            stickerIndex = index;
            break;
        }

        if (stickerIndex < 0)
            return;

        StickerImage = InlineImages.FirstOrDefault(image => image.IsSticker &&
            string.Equals(
                image.Url,
                Nodes[stickerIndex].ImageUrl,
                StringComparison.OrdinalIgnoreCase));
        if (StickerImage is null)
            return;

        var lineBreakIndex = -1;
        for (var index = stickerIndex + 1; index < Nodes.Count; index++)
        {
            if (Nodes[index].Kind == "line-break")
            {
                lineBreakIndex = index;
                break;
            }
        }

        var leadEnd = lineBreakIndex >= 0 ? lineBreakIndex : Nodes.Count;
        StickerLeadNodes = Nodes
            .Skip(stickerIndex + 1)
            .Take(leadEnd - stickerIndex - 1)
            .ToList();
        StickerBodyNodes = lineBreakIndex >= 0
            ? Nodes.Skip(lineBreakIndex + 1).ToList()
            : [];
    }

    public async Task LoadImageAsync(
        HttpClient httpClient,
        CancellationToken cancellationToken,
        RemoteImageCache? imageCache = null)
    {
        var inlineTasks = InlineImages.Select(image =>
            image.LoadAsync(httpClient, cancellationToken, imageCache));
        var videoTitleTask = IsYouTube
            ? NewsArticleViewModel.LoadYouTubeTitleAsync(httpClient, Url, cancellationToken)
            : null;

        if (!IsImage && !IsYouTube)
        {
            await Task.WhenAll(inlineTasks.Concat(
                DetailsBlocks.Select(block => block.LoadImageAsync(
                    httpClient,
                    cancellationToken,
                    imageCache))));
            return;
        }

        if (Image is not null)
        {
            await Task.WhenAll(inlineTasks.Append(videoTitleTask ?? Task.FromResult<string?>(null)));
            await ApplyVideoTitleAsync(videoTitleTask);
            return;
        }

        var blockTask = RemoteBitmapLoader.LoadAsync(
            ImageUrl,
            960,
            httpClient,
            cancellationToken,
            imageCache);
        await Task.WhenAll(
            inlineTasks
                .Append(LoadBlockImageAsync(blockTask, cancellationToken))
                .Append(videoTitleTask ?? Task.FromResult<string?>(null)));
        await ApplyVideoTitleAsync(videoTitleTask);
    }

    private async Task ApplyVideoTitleAsync(Task<string?>? videoTitleTask)
    {
        if (videoTitleTask is null)
            return;

        var title = await videoTitleTask.ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(title))
            return;

        await Dispatcher.UIThread.InvokeAsync(() => VideoTitle = title);
    }

    private async Task LoadBlockImageAsync(
        Task<Bitmap?> bitmapTask,
        CancellationToken cancellationToken)
    {
        var bitmap = await bitmapTask.ConfigureAwait(false);
        if (bitmap is null)
            return;

        if (cancellationToken.IsCancellationRequested)
        {
            bitmap.Dispose();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(
            () => Image = bitmap,
            DispatcherPriority.Background);
    }

    partial void OnImageChanging(Bitmap? oldValue, Bitmap? newValue)
        => oldValue?.Dispose();

    public void ReleaseImages()
    {
        Image = null;
        foreach (var inlineImage in InlineImages)
            inlineImage.ReleaseImage();
        foreach (var detailBlock in DetailsBlocks)
            detailBlock.ReleaseImages();
    }

    public void Dispose()
    {
        ReleaseImages();
        foreach (var inlineImage in InlineImages)
            inlineImage.Dispose();
        foreach (var detailBlock in DetailsBlocks)
            detailBlock.Dispose();
    }
}

public sealed partial class NewsInlineImageViewModel : ObservableObject, IDisposable
{
    public NewsInlineImageViewModel(NewsContentNode node)
    {
        Url = node.ImageUrl ?? string.Empty;
        AltText = node.AltText ?? string.Empty;
        IsSticker = string.Equals(
            node.ImagePresentation,
            "sticker",
            StringComparison.OrdinalIgnoreCase);
    }

    [ObservableProperty]
    private Bitmap? _image;

    public string Url { get; }
    public string AltText { get; }
    public bool IsSticker { get; }
    public double DisplaySize => IsSticker ? 64 : 24;

    public async Task LoadAsync(
        HttpClient httpClient,
        CancellationToken cancellationToken,
        RemoteImageCache? imageCache = null)
    {
        if (Image is not null)
            return;

        var bitmap = await RemoteBitmapLoader.LoadAsync(
            Url,
            IsSticker ? 128 : 48,
            httpClient,
            cancellationToken,
            imageCache).ConfigureAwait(false);
        if (bitmap is null)
            return;

        if (cancellationToken.IsCancellationRequested)
        {
            bitmap.Dispose();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(
            () => Image = bitmap,
            DispatcherPriority.Background);
    }

    partial void OnImageChanging(Bitmap? oldValue, Bitmap? newValue)
        => oldValue?.Dispose();

    public void ReleaseImage()
        => Image = null;

    public void Dispose()
        => ReleaseImage();
}

public sealed record NewsArticleListItemViewModel(
    string Marker,
    IReadOnlyList<NewsContentNode> Nodes,
    IReadOnlyList<NewsArticleListItemViewModel> Children,
    ICommand? LinkCommand)
{
    public bool IsBullet => Marker == "•";
    public bool IsOrdered => !IsBullet;
}
