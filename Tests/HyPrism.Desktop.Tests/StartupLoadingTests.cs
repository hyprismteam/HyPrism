// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Net;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Labs.Lottie;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HyPrism.Core.Accounts;
using HyPrism.Core.Application.Ports;
using HyPrism.Core.Application.Progress;
using HyPrism.Core.Game;
using HyPrism.Core.Game.Instances;
using HyPrism.Core.Game.Launch;
using HyPrism.Desktop.Features.News;
using HyPrism.Desktop.Features.Settings;
using HyPrism.Desktop.Localization;
using HyPrism.Desktop.Platform;
using HyPrism.Desktop.Shell;
using Moq;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class StartupLoadingTests
{
    private static readonly byte[] PreviewPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [AvaloniaFact]
    public async Task StartupPreloadWarmsNewsPreviewBeforeRevealingLauncher()
    {
        var instances = new Mock<IInstanceRepository>();
        var profiles = new Mock<IProfileManager>();
        var profileRepository = new Mock<IProfileRepository>();
        var launchCoordinator = new Mock<IGameLaunchCoordinator>();
        var installationWorkflow = new Mock<IGameInstallationWorkflow>();
        var gameProcess = new Mock<IGameProcessTracker>();
        var progress = new Mock<IProgressReporter>();
        var settings = new Mock<IDesktopSettingsStore>();
        var news = new Mock<IHytaleNewsClient>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var imageHandler = new PreviewHandler();
        using var httpClient = new HttpClient(imageHandler);

        instances.Setup(service => service.GetCachedInstances()).Returns([]);
        profiles.Setup(service => service.GetNick()).Returns("Startup Test");
        profileRepository.Setup(service => service.GetProfiles()).Returns([]);
        settings.SetupGet(service => service.AvailableBackgrounds).Returns([]);
        news.Setup(service => service.GetNewsAsync(It.IsAny<int>())).ReturnsAsync(
        [
            new NewsItemResponse
            {
                Title = "Cached before navigation",
                Url = "https://hytale.com/news/cached",
                ImageUrl = "https://cdn.example.com/news-preview.png"
            }
        ]);

        using var viewModel = new MainWindowViewModel(
            instances.Object,
            profiles.Object,
            profileRepository.Object,
            launchCoordinator.Object,
            installationWorkflow.Object,
            gameProcess.Object,
            progress.Object,
            settings.Object,
            news.Object,
            uriLauncher.Object,
            httpClient,
            new StringLocalizer("en-US"),
            remoteImageCache: new RemoteImageCache(httpClient));
        viewModel.BeginStartupLoading();

        var window = new MainWindow
        {
            Width = 1180,
            Height = 760,
            DataContext = viewModel
        };
        window.Show();
        await Task.Delay(80);
        Dispatcher.UIThread.RunJobs();

        var startupScreen = Assert.IsType<Border>(
            window.FindControl<Border>("StartupLoadingScreen"));
        var launcherShell = Assert.IsType<Grid>(window.FindControl<Grid>("LauncherShell"));
        var startupAnimation = Assert.IsType<Lottie>(window.FindControl<Lottie>("StartupAnimation"));
        var startupBrand = Assert.IsType<Image>(window.FindControl<Image>("StartupBrand"));
        var windowChrome = Assert.IsType<Grid>(window.FindControl<Grid>("WindowChrome"));
        var minimizeButton = Assert.IsType<Button>(window.FindControl<Button>("MinimizeWindowButton"));
        var resizeEast = Assert.IsType<Border>(window.FindControl<Border>("ResizeEast"));
        Assert.True(startupScreen.IsEffectivelyVisible);
        Assert.True(startupScreen.IsHitTestVisible);
        Assert.Equal(0, launcherShell.Opacity);
        Assert.Equal("/Assets/Lotties/figures.json", startupAnimation.Path);
        Assert.Equal(96, startupAnimation.Width);
        Assert.Equal(96, startupAnimation.Height);
        Assert.NotNull(startupAnimation.OpacityMask);
        Assert.Equal(1, Grid.GetRow(startupBrand));
        Assert.True(startupBrand.Bounds.Top > startupAnimation.Bounds.Top);
        Assert.True(windowChrome.IsEffectivelyVisible);
        Assert.True(minimizeButton.IsEffectivelyVisible);
        Assert.True(minimizeButton.IsHitTestVisible);
        Assert.True(resizeEast.IsEffectivelyVisible);
        Assert.True(resizeEast.IsHitTestVisible);

        var renderPath = Environment.GetEnvironmentVariable("HYPRISM_STARTUP_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(renderPath))
            window.CaptureRenderedFrame()!.Save(renderPath, PngBitmapEncoderOptions.Default);

        var preloadStopwatch = Stopwatch.StartNew();
        var preloadTask = viewModel.PreloadStartupDataAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(
            () => viewModel.StartupLoadingStatus == "Ready to launch",
            TimeSpan.FromSeconds(2));

        Assert.False(preloadTask.IsCompleted);
        await preloadTask;
        Assert.True(preloadStopwatch.Elapsed >= TimeSpan.FromMilliseconds(900));

        Assert.NotNull(viewModel.FeaturedNews?.Image);
        Assert.Equal(1, imageHandler.Requests);
        news.Verify(service => service.GetNewsAsync(It.IsAny<int>()), Times.Once);

        viewModel.NavigateCommand.Execute("news");
        await Task.Delay(80);
        news.Verify(service => service.GetNewsAsync(It.IsAny<int>()), Times.Once);

        viewModel.CompleteStartupLoading();
        await Task.Delay(480);
        Dispatcher.UIThread.RunJobs();

        Assert.False(startupScreen.IsVisible);
        Assert.Equal(1, launcherShell.Opacity);
        Assert.True(launcherShell.IsHitTestVisible);
        window.Close();
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate() && DateTime.UtcNow < deadline)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.True(predicate());
    }

    private sealed class PreviewHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(PreviewPng),
                RequestMessage = request
            });
        }
    }
}
