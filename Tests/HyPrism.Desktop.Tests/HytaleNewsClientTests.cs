// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using HyPrism.Core;
using HyPrism.Desktop.Features.News;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class HytaleNewsClientTests
{
    private const string ArticleUrl =
        "https://hytale.com/news/2026/7/community-spotlight-worldgen-v2";

    [Fact]
    public async Task GetNewsAsync_ParsesSemanticHytaleArticleCards()
    {
        using var handler = new FixtureHttpHandler();
        using var client = new HttpClient(handler);
        var service = new HytaleNewsClient(client);

        var items = await service.GetNewsAsync(1);

        var item = Assert.Single(items);
        Assert.Equal("Community Spotlight: WorldGen V2", item.Title);
        Assert.Equal("A look at what builders created with the new world generation tools.", item.Excerpt);
        Assert.Equal(ArticleUrl, item.Url.TrimEnd('/'));
        Assert.Equal("2026-07-29", item.PublishedAt);
        Assert.Equal("Hytale Team", item.Author);
        Assert.Equal("https://cdn.hytale.com/community-cover.png", item.ImageUrl);
        Assert.Equal(["Community News"], item.Categories);
    }

    [Fact]
    public async Task GetNewsArticleAsync_ReturnsSanitizedFormattingTreeAndCachesIt()
    {
        using var handler = new FixtureHttpHandler();
        using var client = new HttpClient(handler);
        var service = new HytaleNewsClient(client);

        var article = await service.GetNewsArticleAsync(ArticleUrl);
        var cachedArticle = await service.GetNewsArticleAsync(ArticleUrl);

        Assert.NotNull(article);
        Assert.Same(article, cachedArticle);
        Assert.Equal("Community Spotlight: WorldGen V2", article.Title);
        Assert.Equal("A complete community spotlight.", article.Excerpt);
        Assert.Equal("2026-07-29", article.PublishedAt);
        Assert.Equal("Hytale Team", article.Author);
        Assert.Equal("https://cdn.hytale.com/community-cover.png", article.ImageUrl);
        Assert.Equal(["Community News"], article.Categories);

        var paragraph = Assert.Single(article.Content, node => node.Kind == "paragraph");
        Assert.Contains(paragraph.Children, node => node.Kind == "bold");
        Assert.Contains(paragraph.Children, node =>
            node.Kind == "link" && node.Url == "https://hytale.com/news");
        Assert.Contains(paragraph.Children, node => node.Kind == "line-break");
        Assert.Contains(paragraph.Children, node =>
            node.Kind == "inline-image" &&
            node.ImagePresentation == "sticker" &&
            node.ImageUrl == "https://cdn.hytale.com/emotes/kweeb-wave.png");
        Assert.Contains(article.Content, node => node.Kind == "heading" && node.Level == 3);
        Assert.Contains(article.Content, node =>
            node.Kind == "image" && node.ImageUrl == "https://cdn.hytale.com/article-image.png");
        var quote = Assert.Single(article.Content, node => node.Kind == "blockquote");
        Assert.Contains(
            quote.Children.SelectMany(node => node.Children),
            node => node.Kind == "inline-image" &&
                    node.ImagePresentation == "emote" &&
                    node.ImageUrl == "https://cdn.hytale.com/emotes/hypixel-heart.png");
        var list = Assert.Single(article.Content, node => node.Kind == "unordered-list");
        var parentListItem = Assert.Single(list.Children, item =>
            item.Kind == "list-item" &&
            item.Children.Any(child => child.Kind == "unordered-list"));
        var nestedList = Assert.Single(parentListItem.Children, child =>
            child.Kind == "unordered-list");
        var linkedListItem = Assert.Single(nestedList.Children, item =>
            item.Children.Any(child => child.Kind == "link"));
        var listLead = Assert.Single(linkedListItem.Children, child => child.Kind == "text");
        Assert.EndsWith(" ", listLead.Text);
        Assert.Contains(linkedListItem.Children, child =>
            child.Kind == "link" && child.Url == "https://hytale.com/news");
        var wrappedListItem = Assert.Single(list.Children, item =>
            item.Children.Any(child => child.Kind == "paragraph"));
        Assert.DoesNotContain(wrappedListItem.Children, child =>
            child.Kind == "text" && string.IsNullOrWhiteSpace(child.Text));

        var details = Assert.Single(article.Content, node => node.Kind == "details");
        var summary = Assert.Single(details.Children, node => node.Kind == "summary");
        Assert.Contains(summary.Children, node =>
            node.Kind == "inline-image" &&
            node.ImagePresentation == "emote" &&
            node.ImageUrl == "https://cdn.hytale.com/emotes/hypixel-this-is-fine.png");
        Assert.Contains(details.Children, node => node.Kind == "paragraph");
        Assert.DoesNotContain(article.Content, node => node.Text?.Contains("shouldNeverReachTheClient") == true);
        Assert.Equal(1, handler.ArticleRequests);
    }

    [Theory]
    [InlineData("https://example.com/news/2026/7/article")]
    [InlineData("http://hytale.com/news/2026/7/article")]
    [InlineData("https://hytale.com/account")]
    [InlineData("/news/2026/7/article")]
    public async Task GetNewsArticleAsync_RejectsUrlsOutsideOfficialNews(string url)
    {
        using var handler = new FixtureHttpHandler();
        using var client = new HttpClient(handler);
        var service = new HytaleNewsClient(client);

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetNewsArticleAsync(url));

        Assert.Equal(0, handler.TotalRequests);
    }

    [Fact]
    public async Task GetNewsArticleAsync_DoesNotSerializeDifferentArticleDownloads()
    {
        using var handler = new ConcurrentArticleHandler();
        using var client = new HttpClient(handler);
        var service = new HytaleNewsClient(client);

        var first = service.GetNewsArticleAsync(ArticleUrl);
        var second = service.GetNewsArticleAsync(
            "https://hytale.com/news/2026/7/community-spotlight-worldgen-v3");

        var articles = await Task.WhenAll(first, second).WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        Assert.All(articles, Assert.NotNull);
        Assert.Equal(2, handler.ArticleRequests);
    }

    [Fact]
    public async Task NewsAndArticlesSurviveServiceRestartInFileCache()
    {
        var appDirectory = Path.Combine(
            Path.GetTempPath(),
            $"hyprism-news-cache-{Guid.NewGuid():N}");

        try
        {
            using (var handler = new FixtureHttpHandler())
            using (var client = new HttpClient(handler))
            {
                var service = new HytaleNewsClient(client, new AppPathConfiguration(appDirectory));
                Assert.NotEmpty(await service.GetNewsAsync(12));
                Assert.NotNull(await service.GetNewsArticleAsync(ArticleUrl));
            }

            var newsCacheDirectory = Path.Combine(appDirectory, "Cache", "News");
            Assert.True(File.Exists(Path.Combine(newsCacheDirectory, "feed.json")));
            Assert.Single(Directory.EnumerateFiles(newsCacheDirectory, "article-*.json"));

            using var offlineHandler = new FailingHttpHandler();
            using var offlineClient = new HttpClient(offlineHandler);
            var restartedService = new HytaleNewsClient(
                offlineClient,
                new AppPathConfiguration(appDirectory));

            Assert.NotEmpty(await restartedService.GetNewsAsync(12));
            Assert.NotNull(await restartedService.GetNewsArticleAsync(ArticleUrl));
            Assert.Equal(0, offlineHandler.Requests);
        }
        finally
        {
            if (Directory.Exists(appDirectory))
                Directory.Delete(appDirectory, recursive: true);
        }
    }

    private sealed class FixtureHttpHandler : HttpMessageHandler
    {
        public int TotalRequests { get; private set; }
        public int ArticleRequests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            TotalRequests++;
            var isIndex = request.RequestUri?.AbsolutePath.TrimEnd('/') == "/news";
            if (!isIndex)
                ArticleRequests++;

            var fixtureName = isIndex ? "news-index.html" : "article.html";
            var fixturePath = Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "Hytale",
                fixtureName);
            var body = await File.ReadAllTextAsync(fixturePath, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
                RequestMessage = request
            };
        }
    }

    private sealed class ConcurrentArticleHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _bothRequestsStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _articleRequests;

        public int ArticleRequests => Volatile.Read(ref _articleRequests);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _articleRequests) == 2)
                _bothRequestsStarted.TrySetResult();

            await _bothRequestsStarted.Task.WaitAsync(cancellationToken);
            var fixturePath = Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "Hytale",
                "article.html");
            var body = await File.ReadAllTextAsync(fixturePath, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
                RequestMessage = request
            };
        }
    }

    private sealed class FailingHttpHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            throw new HttpRequestException("Network access was not expected.");
        }
    }
}
