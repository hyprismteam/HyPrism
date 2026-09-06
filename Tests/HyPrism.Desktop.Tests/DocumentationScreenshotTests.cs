// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HyPrism.Core.Game.Sources;
using HyPrism.Core.Game.Versions;
using HyPrism.Core.Models;
using HyPrism.Desktop.Controls;
using HyPrism.Desktop.Features.Settings;
using HyPrism.Desktop.Shell;
using Moq;
using Xunit;

namespace HyPrism.Desktop.Tests;

/// <summary>
/// Captures launcher screenshots used by the documentation. The test is skipped
/// unless HYPRISM_DOCS_SCREENSHOTS points at an output directory
/// </summary>
public sealed class DocumentationScreenshotTests
{
    private const int WindowWidth = 1280;
    private const int WindowHeight = 800;
    private const int CaptureScale = 2;

    [AvaloniaFact]
    public async Task CaptureDocumentationScreenshots()
    {
        var outputDirectory = ResolveOutputDirectory();
        if (outputDirectory is null)
        {
            Assert.Skip("HYPRISM_DOCS_SCREENSHOTS is not set");
            return;
        }

        var mirrorDirectory = Path.Combine(Path.GetTempPath(), $"hyprism-docs-shots-{Guid.NewGuid():N}");
        Directory.CreateDirectory(mirrorDirectory);

        try
        {
            await CaptureAsync(
                Path.Combine(outputDirectory!, "en"),
                mirrorDirectory,
                "en-US");
            await CaptureAsync(
                Path.Combine(outputDirectory!, "ru"),
                mirrorDirectory,
                "ru-RU");
        }
        finally
        {
            try
            {
                Directory.Delete(mirrorDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string? ResolveOutputDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("HYPRISM_DOCS_SCREENSHOTS");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        return null;
    }

    private static async Task CaptureAsync(
        string outputDirectory,
        string mirrorDirectory,
        string language)
    {
        Directory.CreateDirectory(outputDirectory);
        using var httpClient = new HttpClient();
        var mirrorCatalog = new MirrorCatalog(mirrorDirectory, httpClient);
        mirrorCatalog.Save(new MirrorMeta
        {
            Id = "community-eu",
            Name = "Community EU",
            SourceType = "pattern",
            Pattern = new MirrorPatternConfig
            {
                BaseUrl = "https://eu.example.org/hytale",
                VersionDiscovery = new VersionDiscoveryConfig
                {
                    Method = "static-list",
                    StaticVersions = [7]
                }
            }
        });
        mirrorCatalog.Save(new MirrorMeta
        {
            Id = "community-us",
            Name = "Community US",
            SourceType = "pattern",
            Pattern = new MirrorPatternConfig
            {
                BaseUrl = "https://us.example.org/hytale",
                VersionDiscovery = new VersionDiscoveryConfig
                {
                    Method = "static-list",
                    StaticVersions = [7]
                }
            }
        });

        var versions = new Mock<IGameVersionCatalog>();
        versions
            .Setup(service => service.ProbeSourceAvailabilityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MirrorSpeedTestResult
            {
                MirrorId = "probe",
                IsAvailable = true,
                HasVersionsForCurrentPlatform = true,
                PingMs = 34
            });

        using var viewModel = MainWindowViewModelFactory.Create(
            httpClient,
            mirrorCatalog,
            versions.Object,
            language);
        var window = new MainWindow
        {
            Width = WindowWidth,
            Height = WindowHeight,
            DataContext = viewModel
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        await WaitFramesAsync(4);

        async Task CapturePageAsync(string route, string fileName, Action? prepare = null)
        {
            prepare?.Invoke();
            viewModel.NavigateCommand.Execute(route);
            await WaitFramesAsync(6);
            Capture(window, Path.Combine(outputDirectory, fileName));
        }

        await CapturePageAsync("dashboard", "home.png");
        await CapturePageAsync("instances", "instances.png");
        await CapturePageAsync("profiles", "profiles.png");

        await CapturePageAsync("news", "news.png");
        if (viewModel.FeaturedNews is not null)
        {
            await viewModel.FeaturedNews.OpenCommand.ExecuteAsync(null);
            await WaitFramesAsync(10);
            Capture(window, Path.Combine(outputDirectory, "news-article.png"));
            await viewModel.CloseNewsArticleCommand.ExecuteAsync(null);
            await WaitFramesAsync(4);
        }

        await CapturePageAsync("settings", "settings-downloads.png", () =>
            SelectSettingsCategory(viewModel, "downloads"));
        await WaitFramesAsync(8);

        SelectSettingsCategory(viewModel, "general");
        await WaitFramesAsync(4);
        Capture(window, Path.Combine(outputDirectory, "settings-general.png"));

        SelectSettingsCategory(viewModel, "java");
        await WaitFramesAsync(4);
        var settingsView = window.GetVisualDescendants().OfType<SettingsView>().Single();
        var javaArgumentsModal = settingsView.FindControl<OverlayModal>("JavaArgumentModal");
        Assert.NotNull(javaArgumentsModal);
        viewModel.Settings.ShowAddJavaArgumentCommand.Execute(null);
        viewModel.Settings.NewJavaArgument = "-Dfile.encoding=UTF-8";
        await WaitForModalAsync(javaArgumentsModal!);
        var sheet = javaArgumentsModal!.FindControl<Grid>("OverlayModalSheet");
        Assert.NotNull(sheet);
        using var dialogFrame = RenderAtCaptureScale(sheet!);
        dialogFrame.Save(Path.Combine(outputDirectory, "java-arguments.png"), PngBitmapEncoderOptions.Default);

        window.Close();
    }

    private static void SelectSettingsCategory(MainWindowViewModel viewModel, string id)
        => viewModel.Settings.SelectCategoryCommand.Execute(
            viewModel.Settings.Categories.Single(category => category.Id == id));

    private static async Task WaitForModalAsync(OverlayModal modal)
    {
        var startedAt = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(startedAt) < TimeSpan.FromSeconds(5))
        {
            Dispatcher.UIThread.RunJobs();
            var sheet = modal.FindControl<Grid>("OverlayModalSheet");
            if (sheet is not null &&
                sheet.RenderTransform is Avalonia.Media.TranslateTransform transform &&
                transform.Y == 0)
            {
                return;
            }

            await Task.Delay(16, TestContext.Current.CancellationToken);
        }

        Assert.Fail("The Java argument dialog did not finish opening within five seconds");
    }

    private static async Task WaitFramesAsync(int frames)
    {
        for (var index = 0; index < frames; index++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(16, TestContext.Current.CancellationToken);
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string path)
    {
        using var frame = RenderAtCaptureScale(window);
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }

    private static RenderTargetBitmap RenderAtCaptureScale(Visual visual)
    {
        // Render the actual views at high density, without enlarging a captured bitmap
        var frame = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(visual.Bounds.Width * CaptureScale),
                (int)Math.Ceiling(visual.Bounds.Height * CaptureScale)),
            new Vector(96 * CaptureScale, 96 * CaptureScale));
        frame.Render(visual);
        return frame;
    }
}
