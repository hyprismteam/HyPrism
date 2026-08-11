// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPrism.Desktop.Platform;

namespace HyPrism.Desktop.Features.News;

public sealed class NewsArticleViewModel : ObservableObject, IDisposable
{
    private readonly IExternalUriLauncher _uriLauncher;
    private readonly string _publishedAt;
    private string _date = string.Empty;
    private string _metadata = string.Empty;

    public NewsArticleViewModel(NewsArticleResponse article, IExternalUriLauncher uriLauncher)
    {
        _uriLauncher = uriLauncher;
        Title = article.Title;
        Excerpt = article.Excerpt;
        Url = article.Url;
        Author = article.Author;
        _publishedAt = article.PublishedAt;
        Categories = string.Join("  ·  ", article.Categories);
        OpenLinkCommand = new AsyncRelayCommand<string?>(OpenLinkAsync);
        Blocks = NewsArticleBlockViewModel.Create(article.Content, OpenLinkCommand);
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
    public ObservableCollection<NewsArticleBlockViewModel> RenderedBlocks { get; } = [];
    public IRelayCommand OpenOriginalCommand { get; }
    public IRelayCommand<string?> OpenLinkCommand { get; }
    public bool HasExcerpt => !string.IsNullOrWhiteSpace(Excerpt);
    public bool HasAuthor => !string.IsNullOrWhiteSpace(Author);
    public bool HasCategories => !string.IsNullOrWhiteSpace(Categories);
    public bool HasContent => Blocks.Count > 0;

    public void ResetRenderedBlocks()
        => RenderedBlocks.Clear();

    public async Task PrepareForDisplayAsync(
        CancellationToken cancellationToken,
        Func<Task>? firstBatchReady = null)
    {
        const int BatchSize = 3;

        await Dispatcher.UIThread.InvokeAsync(() => RenderedBlocks.Clear());
        if (Blocks.Count == 0)
        {
            if (firstBatchReady is not null)
                await firstBatchReady().ConfigureAwait(false);
            return;
        }

        for (var offset = 0; offset < Blocks.Count; offset += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = Blocks.Skip(offset).Take(BatchSize).ToArray();
            await Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    foreach (var block in batch)
                        RenderedBlocks.Add(block);
                },
                DispatcherPriority.Background);

            if (offset == 0 && firstBatchReady is not null)
                await firstBatchReady().ConfigureAwait(false);

            // Let layout, input and animations complete a real frame between rich
            // groups. A one-millisecond yield can enqueue the next group before the
            // compositor has presented anything on long patch-note articles.
            if (offset + BatchSize < Blocks.Count)
                await Task.Delay(16, cancellationToken).ConfigureAwait(false);
        }
    }

    public void RefreshCulture()
    {
        Date = NewsItemViewModel.FormatDate(_publishedAt, _publishedAt);
        Metadata = string.Join(
            "  ·  ",
            new[] { Author, Categories, Date }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    public async Task LoadImagesAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        using var concurrencyGate = new SemaphoreSlim(3, 3);
        try
        {
            await Task.WhenAll(Blocks.Select(async block =>
            {
                if (!block.HasRemoteImages)
                    return;

                await concurrencyGate.WaitAsync(cancellationToken);
                try
                {
                    await block.LoadImageAsync(httpClient, cancellationToken);
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
    private NewsArticleBlockViewModel(NewsContentNode node, ICommand? linkCommand)
    {
        Kind = node.Kind;
        LinkCommand = linkCommand;

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
        ImageUrl = node.ImageUrl;
        AltText = node.AltText ?? string.Empty;
        HeadingLevel = node.Level ?? 2;
        PlainText = ExtractText(node);
        if (IsList)
            ListItems = CreateListItems(node, linkCommand);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    [NotifyPropertyChangedFor(nameof(HasNoImage))]
    private Bitmap? _image;

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
    public string? ImageUrl { get; }
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
    public bool IsQuote => Kind == "blockquote";
    public bool IsDetails => Kind == "details";
    public bool IsList => Kind is "unordered-list" or "ordered-list";
    public bool IsCode => Kind is "code-block" or "inline-code";
    public bool IsDivider => Kind == "divider";
    public bool IsCaption => Kind == "caption";
    public bool HasImage => Image is not null;
    public bool HasNoImage => Image is null;
    public bool HasRemoteImages =>
        IsImage || InlineImages.Count > 0 || DetailsBlocks.Any(block => block.HasRemoteImages);

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
        ICommand? linkCommand = null)
    {
        var blocks = new List<NewsArticleBlockViewModel>();
        foreach (var node in nodes)
            AddBlock(node, blocks, linkCommand);
        return blocks;
    }

    private static void AddBlock(
        NewsContentNode node,
        ICollection<NewsArticleBlockViewModel> blocks,
        ICommand? linkCommand)
    {
        if (node.Kind == "paragraph" && node.Children.Any(child => child.Kind == "image"))
        {
            var inlineNodes = new List<NewsContentNode>();
            foreach (var child in node.Children)
            {
                if (child.Kind != "image")
                {
                    inlineNodes.Add(child);
                    continue;
                }

                AddInlineParagraph(inlineNodes, blocks, linkCommand);
                inlineNodes.Clear();
                AddBlock(child, blocks, linkCommand);
            }

            AddInlineParagraph(inlineNodes, blocks, linkCommand);
            return;
        }

        var containsBlockChildren = node.Kind == "container" && node.Children.Any(child =>
            child.Kind is "paragraph" or "heading" or "image" or "blockquote" or
                "unordered-list" or "ordered-list" or "figure" or "table" or "divider");
        if (containsBlockChildren ||
            node.Kind is "figure" or "table" or "table-row" or "table-cell" or "table-header")
        {
            foreach (var child in node.Children)
                AddBlock(child, blocks, linkCommand);
            return;
        }

        if (node.Kind == "text" && string.IsNullOrWhiteSpace(node.Text))
            return;

        blocks.Add(new NewsArticleBlockViewModel(node, linkCommand));
    }

    private static void AddInlineParagraph(
        IReadOnlyCollection<NewsContentNode> inlineNodes,
        ICollection<NewsArticleBlockViewModel> blocks,
        ICommand? linkCommand)
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
        }, linkCommand));
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

    public async Task LoadImageAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        var inlineTasks = InlineImages.Select(image =>
            image.LoadAsync(httpClient, cancellationToken));

        if (!IsImage)
        {
            await Task.WhenAll(inlineTasks.Concat(
                DetailsBlocks.Select(block => block.LoadImageAsync(httpClient, cancellationToken))));
            return;
        }

        if (Image is not null)
        {
            await Task.WhenAll(inlineTasks);
            return;
        }

        var blockTask = RemoteNewsBitmap.LoadAsync(ImageUrl, 1400, httpClient, cancellationToken);
        await Task.WhenAll(inlineTasks.Append(LoadBlockImageAsync(blockTask, cancellationToken)));
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

        await Dispatcher.UIThread.InvokeAsync(() => Image = bitmap);
    }

    partial void OnImageChanging(Bitmap? oldValue, Bitmap? newValue)
        => oldValue?.Dispose();

    public void Dispose()
    {
        Image = null;
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

    public async Task LoadAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        if (Image is not null)
            return;

        var bitmap = await RemoteNewsBitmap.LoadAsync(
            Url,
            IsSticker ? 128 : 48,
            httpClient,
            cancellationToken).ConfigureAwait(false);
        if (bitmap is null)
            return;

        if (cancellationToken.IsCancellationRequested)
        {
            bitmap.Dispose();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => Image = bitmap);
    }

    partial void OnImageChanging(Bitmap? oldValue, Bitmap? newValue)
        => oldValue?.Dispose();

    public void Dispose()
        => Image = null;
}

internal static class RemoteNewsBitmap
{
    public static async Task<Bitmap?> LoadAsync(
        string? url,
        int decodeWidth,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var imageUri) ||
            imageUri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        try
        {
            using var response = await httpClient.GetAsync(
                    imageUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            var imageBytes = buffer.ToArray();

            // Decoding large covers is CPU-bound and can otherwise resume on the
            // Avalonia dispatcher, freezing an in-progress page transition.
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var imageStream = new MemoryStream(imageBytes, writable: false);
                return Bitmap.DecodeToWidth(
                    imageStream,
                    decodeWidth,
                    BitmapInterpolationMode.HighQuality);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception)
        {
            // Article text remains readable when a remote image is unavailable.
            return null;
        }
    }
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
