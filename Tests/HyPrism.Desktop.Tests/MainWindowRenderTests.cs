// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using HyPrism.Desktop.Features.About;
using HyPrism.Desktop.Features.Dashboard;
using HyPrism.Desktop.Features.Instances;
using HyPrism.Desktop.Features.News;
using HyPrism.Desktop.Features.Settings;
using HyPrism.Desktop.Localization;
using HyPrism.Desktop.Controls;
using HyPrism.Desktop.Platform;
using HyPrism.Desktop.Shell;
using HyPrism.Core.Models;
using HyPrism.Core.Application.Progress;
using HyPrism.Core.Application.Ports;
using HyPrism.Core.Game;
using HyPrism.Core.Game.Instances;
using HyPrism.Core.Game.Launch;
using HyPrism.Core.Accounts;
using Moq;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class MainWindowRenderTests
{
    [AvaloniaFact]
    public async Task ExternalUriLauncherRejectsUnsupportedSchemesBeforeResolvingWindow()
    {
        var topLevelRequested = false;
        var launcher = new ExternalUriLauncher(() =>
        {
            topLevelRequested = true;
            return null;
        });

        var launched = await launcher.LaunchAsync(new Uri("file:///tmp/hyprism"));
        var openedDirectory = await launcher.LaunchDirectoryAsync("relative/path");

        Assert.False(launched);
        Assert.False(openedDirectory);
        Assert.False(topLevelRequested);
    }

    [AvaloniaFact]
    public async Task NewsMediaViewModelsDecodeBlockAndInlineImages()
    {
        using var client = new HttpClient(new TinyPngHandler());
        var block = Assert.Single(NewsArticleBlockViewModel.Create(
        [
            new NewsContentNode
            {
                Kind = "image",
                ImageUrl = "https://cdn.hytale.com/gallery.png"
            }
        ]));
        using var inline = new NewsInlineImageViewModel(new NewsContentNode
        {
            Kind = "inline-image",
            ImageUrl = "https://cdn.hytale.com/emotes/heart.png",
            ImagePresentation = "emote"
        });

        await block.LoadImageAsync(client, CancellationToken.None);
        await inline.LoadAsync(client, CancellationToken.None);

        Assert.True(block.HasImage);
        Assert.NotNull(inline.Image);
        block.Dispose();
    }

    [AvaloniaTheory]
    [InlineData("emote", 24)]
    [InlineData("sticker", 64)]
    public void InlineNewsMediaUsesBlogRelativeSizing(string presentation, double expectedSize)
    {
        var node = new NewsContentNode
        {
            Kind = "inline-image",
            ImageUrl = $"https://cdn.hytale.com/emotes/{presentation}.png",
            AltText = $":{presentation}:",
            ImagePresentation = presentation
        };
        using var media = new NewsInlineImageViewModel(node);
        var control = new NewsRichTextBlock
        {
            InlineImages = [media],
            Nodes = [node]
        };

        var inline = Assert.IsType<InlineUIContainer>(Assert.Single(control.Inlines!));
        var image = Assert.IsType<Image>(inline.Child);
        Assert.Equal(BaselineAlignment.Center, inline.BaselineAlignment);
        Assert.Equal(expectedSize, image.Width);
        Assert.Equal(expectedSize, image.Height);
    }

    [AvaloniaFact]
    public void InlineCodeChipsKeepCompactMetricsInsideWrappedParagraphs()
    {
        var control = new NewsRichTextBlock
        {
            FontFamily = new FontFamily(
                "avares://HyPrism.Desktop/Assets/Fonts#Google Sans"),
            FontSize = 17,
            LineHeight = 28,
            TextWrapping = TextWrapping.Wrap,
            Nodes =
            [
                new NewsContentNode { Kind = "text", Text = "Added a " },
                new NewsContentNode { Kind = "inline-code", Text = "Trig" },
                new NewsContentNode
                {
                    Kind = "text",
                    Text = " density node to world generation. The new "
                },
                new NewsContentNode { Kind = "inline-code", Text = "\"Type\": \"Trig\"" },
                new NewsContentNode { Kind = "text", Text = " takes a " },
                new NewsContentNode { Kind = "inline-code", Text = "Function" },
                new NewsContentNode { Kind = "text", Text = " of " },
                new NewsContentNode { Kind = "inline-code", Text = "Sin" },
                new NewsContentNode { Kind = "text", Text = ", " },
                new NewsContentNode { Kind = "inline-code", Text = "Cos" },
                new NewsContentNode { Kind = "text", Text = ", or " },
                new NewsContentNode { Kind = "inline-code", Text = "Atan" },
                new NewsContentNode { Kind = "text", Text = " and an " },
                new NewsContentNode { Kind = "inline-code", Text = "InputScale" },
                new NewsContentNode { Kind = "text", Text = " before the function runs." }
            ]
        };
        var window = new Window
        {
            Width = 980,
            Height = 220,
            Background = new SolidColorBrush(Color.Parse("#0D0E10")),
            Content = new Border
            {
                Padding = new Thickness(28),
                Child = control
            }
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var chips = control.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("inlineCode"))
            .ToArray();
        Assert.Equal(7, chips.Length);
        Assert.All(chips, chip =>
        {
            Assert.Equal(new CornerRadius(6), chip.CornerRadius);
            Assert.InRange(chip.Bounds.Height, 18, 22);
            Assert.Equal(18, Assert.IsType<TextBlock>(chip.Child).LineHeight);
        });

        var previewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_INLINE_CODE_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(previewPath))
            frame!.Save(previewPath, PngBitmapEncoderOptions.Default);

        window.Close();
    }

    [AvaloniaFact]
    public void StickerParagraphKeepsOnlyItsLeadBesideTheSticker()
    {
        var block = Assert.Single(NewsArticleBlockViewModel.Create(
        [
            new NewsContentNode
            {
                Kind = "paragraph",
                Children =
                [
                    new NewsContentNode
                    {
                        Kind = "inline-image",
                        ImageUrl = "https://cdn.hytale.com/emotes/kweeb-wave.png",
                        ImagePresentation = "sticker"
                    },
                    new NewsContentNode { Kind = "text", Text = " Hello everyone! " },
                    new NewsContentNode { Kind = "line-break" },
                    new NewsContentNode { Kind = "text", Text = "Today, we want to shine a spotlight." }
                ]
            }
        ]));

        Assert.True(block.IsStickerParagraph);
        Assert.False(block.IsParagraph);
        Assert.NotNull(block.StickerImage);
        Assert.Equal("Hello everyone!", Assert.Single(block.StickerLeadNodes).Text?.Trim());
        Assert.Equal(
            "Today, we want to shine a spotlight.",
            Assert.Single(block.StickerBodyNodes).Text);

        block.Dispose();
    }

    [AvaloniaFact]
    public void ParagraphWrappedListItemDoesNotKeepAnEmptyTrailingLine()
    {
        var block = Assert.Single(NewsArticleBlockViewModel.Create(
        [
            new NewsContentNode
            {
                Kind = "unordered-list",
                Children =
                [
                    new NewsContentNode
                    {
                        Kind = "list-item",
                        Children =
                        [
                            new NewsContentNode
                            {
                                Kind = "paragraph",
                                Children =
                                [
                                    new NewsContentNode
                                    {
                                        Kind = "text",
                                        Text = "Compact patch note."
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        ]));
        var item = Assert.Single(block.ListItems);
        var control = new NewsRichTextBlock { Nodes = item.Nodes };

        Assert.NotNull(control.Inlines);
        Assert.Equal("Compact patch note.", Assert.IsType<Run>(Assert.Single(control.Inlines!)).Text);

        block.Dispose();
    }

    [AvaloniaFact]
    public void ArticleLinkPreservesItsLeadingSpaceAndUsesTheBrowserCommand()
    {
        string? openedUrl = null;
        var command = new RelayCommand<string?>(url => openedUrl = url);
        var control = new NewsRichTextBlock
        {
            LinkCommand = command,
            Nodes =
            [
                new NewsContentNode { Kind = "text", Text = "Documentation: " },
                new NewsContentNode
                {
                    Kind = "link",
                    Url = "https://hytalemodding.dev/",
                    Children =
                    [
                        new NewsContentNode
                        {
                            Kind = "text",
                            Text = "https://hytalemodding.dev/"
                        }
                    ]
                }
            ]
        };

        var plainText = Assert.IsType<Run>(control.Inlines![0]);
        var link = Assert.IsType<Run>(control.Inlines[1]);
        var linkForeground = Assert.IsType<SolidColorBrush>(link.Foreground);
        var underline = Assert.IsType<SolidColorBrush>(Assert.Single(link.TextDecorations!).Stroke);

        Assert.Equal("Documentation: ", plainText.Text);
        Assert.Equal("https://hytalemodding.dev/", link.Text);
        Assert.Equal(Color.Parse("#C9BCFF"), linkForeground.Color);
        Assert.Equal(0, underline.Opacity);
        Assert.DoesNotContain(control.Inlines, inline => inline is InlineUIContainer);
        Assert.Null(openedUrl);
    }

    [AvaloniaFact]
    public async Task CompactNewsLoadingUsesOpaqueReaderAndKeepsSkeletonBelowBack()
    {
        var progress = new Mock<IProgressReporter>();
        var instances = new Mock<IInstanceRepository>();
        var profile = new Mock<IProfileManager>();
        var profileManagement = new Mock<IProfileRepository>();
        var launchCoordinator = new Mock<IGameLaunchCoordinator>();
        var gameSession = new Mock<IGameInstallationWorkflow>();
        var gameProcess = new Mock<IGameProcessTracker>();
        var settings = new Mock<IDesktopSettingsStore>();
        var news = new Mock<IHytaleNewsClient>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var articleCompletion = new TaskCompletionSource<NewsArticleResponse?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        instances.Setup(service => service.GetCachedInstances()).Returns([]);
        profile.Setup(service => service.GetNick()).Returns("Reader Test");
        settings.SetupGet(service => service.AvailableBackgrounds).Returns([]);
        news.Setup(service => service.GetNewsAsync(It.IsAny<int>()))
            .ReturnsAsync(
            [
                new NewsItemResponse
                {
                    Title = "Uncached article",
                    Excerpt = "Loading without blocking the feed.",
                    Url = "https://hytale.com/news/2026/8/uncached-article",
                    Date = "2026-08-08",
                    Author = "Hytale Team"
                }
            ]);
        news.Setup(service => service.GetNewsArticleAsync(It.IsAny<string>()))
            .Returns(articleCompletion.Task);

        using var viewModel = new MainWindowViewModel(
            instances.Object,
            profile.Object,
            profileManagement.Object,
            launchCoordinator.Object,
            gameSession.Object,
            gameProcess.Object,
            progress.Object,
            settings.Object,
            news.Object,
            uriLauncher.Object,
            new HttpClient(),
            new StringLocalizer("en-US"));
        var window = new MainWindow
        {
            Width = 1024,
            Height = 700,
            DataContext = viewModel
        };

        window.Show();
        viewModel.NavigateCommand.Execute("news");
        Dispatcher.UIThread.RunJobs();
        var openTask = viewModel.FeaturedNews!.OpenCommand.ExecuteAsync(null);
        await Task.Delay(240);
        Dispatcher.UIThread.RunJobs();

        var compactShell = window.FindControl<Carousel>("CompactNewsShell");
        var articleHost = window.FindControl<ContentControl>("CompactArticleHost");
        Assert.NotNull(compactShell);
        Assert.NotNull(articleHost);
        Assert.Equal(1, compactShell!.SelectedIndex);
        Assert.True(viewModel.IsNewsArticleLoading);
        var transition = Assert.IsType<PageSlide>(compactShell.PageTransition);
        Assert.IsType<CubicEaseInOut>(transition.SlideInEasing);
        Assert.IsType<CubicEaseInOut>(transition.SlideOutEasing);
        Assert.True(viewModel.IsCompactNewsTransitionActive);
        Assert.Equal(
            2,
            window.GetVisualDescendants()
                .OfType<Border>()
                .Count(border => border.IsEffectivelyVisible &&
                                 border.Classes.Contains("newsTransitionEdgeFade")));

        var readerRoot = articleHost!.GetVisualDescendants()
            .OfType<Grid>()
            .First(grid => grid.IsEffectivelyVisible && grid.Background is ISolidColorBrush);
        Assert.Equal(
            Color.Parse("#0D0E10"),
            Assert.IsAssignableFrom<ISolidColorBrush>(readerRoot.Background).Color);
        var back = articleHost.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && button.Classes.Contains("articleBack"));
        var skeleton = articleHost.GetVisualDescendants()
            .OfType<Border>()
            .First(border => border.IsEffectivelyVisible && border.Classes.Contains("skeleton"));
        var backPosition = back.TranslatePoint(default, articleHost);
        var skeletonPosition = skeleton.TranslatePoint(default, articleHost);
        Assert.NotNull(backPosition);
        Assert.NotNull(skeletonPosition);
        Assert.True(skeletonPosition!.Value.Y >= backPosition!.Value.Y + back.Bounds.Height + 17);

        var loadingPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_COMPACT_LOADING_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(loadingPreviewPath))
        {
            var loadingFrame = window.CaptureRenderedFrame();
            Assert.NotNull(loadingFrame);
            loadingFrame!.Save(loadingPreviewPath, PngBitmapEncoderOptions.Default);
        }

        await Task.Delay(200);
        Dispatcher.UIThread.RunJobs();

        articleCompletion.SetResult(new NewsArticleResponse
        {
            Title = "Uncached article",
            Url = "https://hytale.com/news/2026/8/uncached-article",
            PublishedAt = "2026-08-08",
            Author = "Hytale Team",
            Content = [new NewsContentNode { Kind = "paragraph", Text = "Loaded." }]
        });
        await openTask;
        Dispatcher.UIThread.RunJobs();
        window.Close();
    }

    [AvaloniaFact]
    public async Task NewsPaginationAndLanguageChangesApplyWithoutRestart()
    {
        using var cultureScope = new CultureRestoreScope();
        var progress = new Mock<IProgressReporter>();
        var instances = new Mock<IInstanceRepository>();
        var profile = new Mock<IProfileManager>();
        var profileManagement = new Mock<IProfileRepository>();
        var launchCoordinator = new Mock<IGameLaunchCoordinator>();
        var gameSession = new Mock<IGameInstallationWorkflow>();
        var gameProcess = new Mock<IGameProcessTracker>();
        var settings = new Mock<IDesktopSettingsStore>();
        var news = new Mock<IHytaleNewsClient>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var language = "en-US";

        instances.Setup(service => service.GetCachedInstances()).Returns([]);
        profile.Setup(service => service.GetNick()).Returns("Reader Test");
        settings.SetupGet(service => service.Language).Returns(() => language);
        settings.SetupSet(service => service.Language = It.IsAny<string>())
            .Callback<string>(value => language = value);
        settings.SetupGet(service => service.AvailableBackgrounds).Returns([]);
        news.Setup(service => service.GetNewsAsync(It.IsAny<int>()))
            .ReturnsAsync((int count) => Enumerable.Range(1, count)
                .Select(index => new NewsItemResponse
                {
                    Title = $"News {index}",
                    Excerpt = "A paginated article excerpt.",
                    Url = $"https://hytale.com/news/2026/8/news-{index}",
                    Date = $"2026-08-{Math.Min(index, 28):00}",
                    Author = "Hytale Team"
                })
                .ToList());

        using var viewModel = new MainWindowViewModel(
            instances.Object,
            profile.Object,
            profileManagement.Object,
            launchCoordinator.Object,
            gameSession.Object,
            gameProcess.Object,
            progress.Object,
            settings.Object,
            news.Object,
            uriLauncher.Object,
            new HttpClient(),
            new StringLocalizer("en-US"));

        viewModel.NavigateCommand.Execute("news");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(12, 1 + viewModel.LatestNews.Count);
        Assert.True(viewModel.CanShowLoadMore);

        await viewModel.LoadMoreNewsCommand.ExecuteAsync(null);
        Assert.Equal(20, 1 + viewModel.LatestNews.Count);
        news.Verify(service => service.GetNewsAsync(20), Times.Once);

        viewModel.NavigateCommand.Execute("settings");
        var settingsBeforeChange = viewModel.Settings;
        var window = new MainWindow
        {
            Width = 1100,
            Height = 760,
            DataContext = viewModel
        };
        window.Show();
        Assert.NotNull(window.CaptureRenderedFrame());

        var languageComboBox = window.GetVisualDescendants()
            .OfType<ComboBox>()
            .Single(comboBox => ReferenceEquals(comboBox.ItemsSource, viewModel.Settings.Languages));
        var fadingLanguageComboBox = Assert.IsType<FadingComboBox>(languageComboBox);
        Assert.All(viewModel.Settings.Languages, choice =>
        {
            Assert.True(choice.HasIcon);
            Assert.NotNull(choice.Icon);
            Assert.Equal(new PixelSize(72, 48), choice.Icon!.PixelSize);
        });
        Assert.Contains(
            languageComboBox.GetVisualDescendants().OfType<Image>(),
            image => ReferenceEquals(image.Source, viewModel.Settings.SelectedLanguage.Icon));

        fadingLanguageComboBox.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        var languagePopup = fadingLanguageComboBox.GetVisualDescendants().OfType<FadingPopup>().Single();
        Assert.True(languagePopup.IsOpen);
        var languagePopupBorder = Assert.IsType<Border>(languagePopup.Child);
        Assert.Equal(8, languagePopup.VerticalOffset);
        Assert.False(languagePopup.IsLightDismissEnabled);
        Assert.True(languagePopup.WindowManagerAddShadowHint);
        Assert.Equal(new CornerRadius(18), languagePopupBorder.CornerRadius);
        Assert.Equal(
            Color.Parse("#1D1E21"),
            Assert.IsAssignableFrom<ISolidColorBrush>(languagePopupBorder.Background).Color);
        Assert.Equal(
            byte.MaxValue,
            Assert.IsAssignableFrom<ISolidColorBrush>(languagePopupBorder.Background).Color.A);
        await Task.Delay(220);
        Dispatcher.UIThread.RunJobs();
        var settingsComboPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_SETTINGS_COMBO_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(settingsComboPreviewPath))
        {
            window.CaptureRenderedFrame()!.Save(settingsComboPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(settingsComboPreviewPath));
        }

        var languagePopupScrollBar = languagePopupBorder.GetVisualDescendants()
            .OfType<ScrollBar>()
            .Single(scrollBar => scrollBar.Orientation == Avalonia.Layout.Orientation.Vertical);
        Assert.True(fadingLanguageComboBox.IsDropDownInteractionSource(languagePopupScrollBar));
        var languageDropDownGlyph = fadingLanguageComboBox.GetVisualDescendants()
            .OfType<PathIcon>()
            .Single(icon => icon.Name == "DropDownGlyph");
        Assert.Equal(14, languageDropDownGlyph.Width);
        Assert.Equal(14, languageDropDownGlyph.Height);
        var german = viewModel.Settings.Languages.Single(choice => choice.Value == "de-DE");
        var germanContainer = languagePopupBorder.GetVisualDescendants()
            .OfType<ComboBoxItem>()
            .Single(item => ReferenceEquals(item.Content, german));
        var selectionEvent = new FocusChangedEventArgs(InputElement.GotFocusEvent)
        {
            Source = germanContainer
        };
        Assert.True(fadingLanguageComboBox.UpdateSelectionFromEvent(germanContainer, selectionEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Same(german, languageComboBox.SelectedItem);
        Assert.False(fadingLanguageComboBox.IsDropDownOpen);
        Assert.True(languagePopup.IsOpen);
        Assert.Contains(
            languagePopupBorder.Transitions!,
            transition => transition is DoubleTransition { Property: { } property } &&
                          property == Visual.OpacityProperty);
        await Task.Delay(90);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.CaptureRenderedFrame());
        Assert.True(languagePopup.IsOpen);
        Assert.Equal(0, languagePopupBorder.Opacity);
        await Task.Delay(150);
        Dispatcher.UIThread.RunJobs();
        Assert.False(languagePopup.IsOpen);

        fadingLanguageComboBox.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(fadingLanguageComboBox.IsDropDownOpen);
        Assert.True(languagePopup.IsOpen);
        await Task.Delay(220);
        Dispatcher.UIThread.RunJobs();
        var languageDismissPoint = new Point(20, 20);
        window.MouseMove(languageDismissPoint);
        window.MouseDown(languageDismissPoint, MouseButton.Left);
        window.MouseUp(languageDismissPoint, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(fadingLanguageComboBox.IsDropDownOpen);
        Assert.False(languagePopup.IsRequestedOpen);
        Assert.True(languagePopup.IsOpen);
        await Task.Delay(230);
        Dispatcher.UIThread.RunJobs();
        Assert.False(languagePopup.IsOpen);

        var russian = viewModel.Settings.Languages.Single(choice => choice.Value == "ru-RU");
        languageComboBox.SelectedItem = russian;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(settingsBeforeChange, viewModel.Settings);
        Assert.Same(russian, viewModel.Settings.SelectedLanguage);
        Assert.Equal("Новости", viewModel.NewsLabel);
        Assert.Equal("Настройки", viewModel.Settings.PageTitle);
        Assert.Equal("Офлайн-аккаунт", viewModel.AccountType);
        Assert.Equal("Загрузить ещё", viewModel.LoadMoreLabel);
        Assert.Equal(
            "Фон и отображение новостей",
            viewModel.Settings.Categories.Single(category => category.Id == "visual").Description);
        Assert.Equal(
            "Среда выполнения, путь к Java и аргументы JVM",
            viewModel.Settings.Categories.Single(category => category.Id == "java").Description);
        Assert.Equal(
            "О программе",
            viewModel.Settings.Categories.Single(category => category.Id == "about").Label);
        Assert.All(
            viewModel.Settings.Categories,
            category => Assert.False(category.Description.EndsWith('.')));
        settings.VerifySet(service => service.Language = "ru-RU", Times.Once);
        window.Close();
    }

    [AvaloniaFact]
    public async Task SmoothNewsScrollerSupportsWheelAndMiddleClickAutoScroll()
    {
        var viewer = new SmoothScrollViewer
        {
            Width = 320,
            Height = 220,
            Content = new Border { Height = 1400 }
        };
        var window = new Window
        {
            Width = 420,
            Height = 320,
            Content = viewer
        };

        window.Show();
        Assert.NotNull(window.CaptureRenderedFrame());
        viewer.Measure(new Size(320, 220));
        viewer.Arrange(new Rect(0, 0, 320, 220));
        Dispatcher.UIThread.RunJobs();
        Assert.True(
            viewer.Extent.Height > viewer.Viewport.Height,
            $"Extent={viewer.Extent.Height}, viewport={viewer.Viewport.Height}");
        var center = viewer.TranslatePoint(new Point(160, 110), window);
        Assert.NotNull(center);

        window.MouseWheel(center!.Value, new Vector(0, -1), RawInputModifiers.None);
        await Task.Delay(120);
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewer.Offset.Y > 0);

        window.MouseDown(center.Value, MouseButton.Middle);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Cursor(StandardCursorType.SizeAll).ToString(), viewer.Cursor?.ToString());
        var beforeAutoScroll = viewer.Offset.Y;
        window.MouseMove(center.Value + new Vector(0, 80));
        await Task.Delay(120);
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewer.Offset.Y > beforeAutoScroll);
        Assert.Equal(
            new Cursor(StandardCursorType.BottomSide).ToString(),
            viewer.Cursor?.ToString());

        window.MouseMove(center.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Cursor(StandardCursorType.SizeAll).ToString(), viewer.Cursor?.ToString());

        window.MouseMove(center.Value - new Vector(0, 80));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Cursor(StandardCursorType.TopSide).ToString(), viewer.Cursor?.ToString());

        window.MouseDown(center.Value + new Vector(0, 80), MouseButton.Middle);
        window.MouseUp(center.Value + new Vector(0, 80), MouseButton.Middle);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(1024, 700, false)]
    [InlineData(1280, 800, true)]
    [InlineData(1920, 900, true)]
    public async Task ShellRendersAtSupportedDesktopSizes(int width, int height, bool isOfficialProfile)
    {
        var progress = new Mock<IProgressReporter>();
        var instances = new Mock<IInstanceRepository>();
        var profile = new Mock<IProfileManager>();
        var profileManagement = new Mock<IProfileRepository>();
        var launchCoordinator = new Mock<IGameLaunchCoordinator>();
        var gameSession = new Mock<IGameInstallationWorkflow>();
        var gameProcess = new Mock<IGameProcessTracker>();
        var settings = new Mock<IDesktopSettingsStore>();
        var news = new Mock<IHytaleNewsClient>();
        var uriLauncher = new Mock<IExternalUriLauncher>();
        var github = new Mock<IGitHubClient>();

        var selected = new InstanceInfo
        {
            Id = "preview",
            Name = "Hytale Release",
            Branch = "release",
            Version = 42,
            IsInstalled = true
        };

        instances.Setup(service => service.GetCachedInstances())
            .Returns([selected]);
        instances.Setup(service => service.GetSelectedInstance())
            .Returns(selected);
        instances.Setup(service => service.GetInstancePathById(selected.Id))
            .Returns("/tmp/hyprism-preview-instance");
        instances.Setup(service => service.IsClientPresent(It.IsAny<string>()))
            .Returns(true);
        profile.Setup(service => service.GetNick()).Returns("HyPrism Player");
        profileManagement.Setup(service => service.GetSelectedProfile())
            .Returns(new Profile
            {
                Name = "HyPrism Player",
                IsOfficial = isOfficialProfile
            });
        settings.SetupGet(service => service.AvailableBackgrounds).Returns(
            ["bg_1.jpg", "bg_2.jpg", "bg_3.jpg", "bg_4.png", "bg_5.jpg", "bg_6.png"]);
        github.Setup(service => service.GetContributorsAsync()).ReturnsAsync(
        [
            new GitHubUser { Login = "yyyumeniku", Type = "User" },
            new GitHubUser { Login = "Aarav2709", Type = "User" },
            .. Enumerable.Range(1, 10).Select(index => new GitHubUser
            {
                Login = $"contributor-{index}",
                AvatarUrl = $"https://avatars.githubusercontent.com/u/{index}",
                HtmlUrl = $"https://github.com/contributor-{index}",
                Type = "User",
                Contributions = 20 - index
            }),
            new GitHubUser { Login = "dependabot[bot]", Type = "Bot" },
            new GitHubUser { Login = "release-bot", Type = "User" }
        ]);
        github.Setup(service => service.GetLatestMainCommitAsync()).ReturnsAsync(
            new GitHubCommit(
                "abcdef1234567890",
                "feat: refine the native About page",
                "https://github.com/hyprismteam/HyPrism/commit/abcdef1"));
        github.Setup(service => service.LoadAvatarAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(TinyPngHandler.ImageBytes);
        news.Setup(service => service.GetNewsAsync(It.IsAny<int>()))
            .ReturnsAsync(
            [
                new NewsItemResponse
                {
                    Title = "Hytale launches a new adventure",
                    Excerpt = "A closer look at the world, its creatures, and the systems behind exploration.",
                    Url = "https://hytale.com/news/preview",
                    Date = "2026-08-05",
                    Author = "Hytale Team"
                },
                new NewsItemResponse
                {
                    Title = "Inside Hytale's latest world update",
                    Excerpt = "New environments and discoveries await.",
                    Url = "https://hytale.com/news/world-update",
                    Date = "2026-08-03",
                    Author = "Hytale Team"
                },
                new NewsItemResponse
                {
                    Title = "Building a world worth exploring",
                    Excerpt = "Meet the artists shaping Hytale's regions.",
                    Url = "https://hytale.com/news/world-art",
                    Date = "2026-08-02",
                    Author = "Hytale Team"
                },
                new NewsItemResponse
                {
                    Title = "A new look at adventure mode",
                    Excerpt = "The team shares its latest design work.",
                    Url = "https://hytale.com/news/adventure-mode",
                    Date = "2026-08-01",
                    Author = "Hytale Team"
                }
            ]);
        news.Setup(service => service.GetNewsArticleAsync(It.IsAny<string>()))
            .ReturnsAsync(new NewsArticleResponse
            {
                Title = "Hytale launches a new adventure",
                Excerpt = "A closer look at the world and its creatures.",
                Url = "https://hytale.com/news/2026/8/new-adventure",
                PublishedAt = "2026-08-05",
                Author = "Hytale Team",
                Categories = ["Game Update", "World Design"],
                Content =
                [
                    new NewsContentNode
                    {
                        Kind = "paragraph",
                        Children =
                        [
                            new NewsContentNode { Kind = "text", Text = "Explore a " },
                            new NewsContentNode
                            {
                                Kind = "bold",
                                Children = [new NewsContentNode { Kind = "text", Text = "living world" }]
                            },
                            new NewsContentNode
                            {
                                Kind = "text",
                                Text = " from inside the launcher. Documentation: "
                            },
                            new NewsContentNode
                            {
                                Kind = "link",
                                Url = "https://hytale.com/news",
                                Children =
                                [
                                    new NewsContentNode
                                    {
                                        Kind = "text",
                                        Text = "Hytale News"
                                    }
                                ]
                            }
                        ]
                    },
                    new NewsContentNode
                    {
                        Kind = "heading",
                        Level = 2,
                        Children = [new NewsContentNode { Kind = "text", Text = "Creative Gameplay Quality" }]
                    },
                    new NewsContentNode
                    {
                        Kind = "blockquote",
                        Children = [new NewsContentNode { Kind = "text", Text = "Every world tells a story." }]
                    },
                    new NewsContentNode
                    {
                        Kind = "unordered-list",
                        Children =
                        [
                            new NewsContentNode
                            {
                                Kind = "list-item",
                                Children =
                                [
                                    new NewsContentNode
                                    {
                                        Kind = "bold",
                                        Children =
                                        [
                                            new NewsContentNode
                                            {
                                                Kind = "text",
                                                Text = "Customize items further"
                                            }
                                        ]
                                    },
                                    new NewsContentNode
                                    {
                                        Kind = "unordered-list",
                                        Children =
                                        [
                                            new NewsContentNode
                                            {
                                                Kind = "list-item",
                                                Children =
                                                [
                                                    new NewsContentNode
                                                    {
                                                        Kind = "text",
                                                        Text = "Website and documentation: "
                                                    },
                                                    new NewsContentNode
                                                    {
                                                        Kind = "link",
                                                        Url = "https://hytalemodding.dev/",
                                                        Children =
                                                        [
                                                            new NewsContentNode
                                                            {
                                                                Kind = "text",
                                                                Text = "https://hytalemodding.dev/"
                                                            }
                                                        ]
                                                    },
                                                    new NewsContentNode
                                                    {
                                                        Kind = "text",
                                                        Text = ". Policies: "
                                                    },
                                                    new NewsContentNode
                                                    {
                                                        Kind = "link",
                                                        Url = "https://hytale.com/server-policies",
                                                        Children =
                                                        [
                                                            new NewsContentNode
                                                            {
                                                                Kind = "text",
                                                                Text = "Server Owner Policies"
                                                            }
                                                        ]
                                                    }
                                                ]
                                            },
                                            new NewsContentNode
                                            {
                                                Kind = "list-item",
                                                Children =
                                                [
                                                    new NewsContentNode
                                                    {
                                                        Kind = "text",
                                                        Text = "New creatures"
                                                    }
                                                ]
                                            }
                                        ]
                                    }
                                ]
                            }
                        ]
                    },
                    new NewsContentNode
                    {
                        Kind = "details",
                        Children =
                        [
                            new NewsContentNode
                            {
                                Kind = "summary",
                                Children =
                                [
                                    new NewsContentNode
                                    {
                                        Kind = "text",
                                        Text = "The technical details "
                                    },
                                    new NewsContentNode
                                    {
                                        Kind = "inline-image",
                                        ImageUrl = "https://cdn.hytale.com/emotes/hypixel-this-is-fine.png",
                                        ImagePresentation = "emote",
                                        AltText = ":hypixel-this-is-fine:"
                                    }
                                ]
                            },
                            new NewsContentNode
                            {
                                Kind = "paragraph",
                                Children =
                                [
                                    new NewsContentNode
                                    {
                                        Kind = "text",
                                        Text = "Hidden implementation notes."
                                    }
                                ]
                            }
                        ]
                    },
                    new NewsContentNode
                    {
                        Kind = "paragraph",
                        Children =
                        [
                            new NewsContentNode { Kind = "text", Text = "Run " },
                            new NewsContentNode { Kind = "inline-code", Text = "worldgen.reload()" },
                            new NewsContentNode { Kind = "text", Text = " to rebuild the preview." }
                        ]
                    },
                    new NewsContentNode
                    {
                        Kind = "code-block",
                        Text = "worldgen.reload();\nserver.save();"
                    }
                ]
            });

        using var viewModel = new MainWindowViewModel(
            instances.Object,
            profile.Object,
            profileManagement.Object,
            launchCoordinator.Object,
            gameSession.Object,
            gameProcess.Object,
            progress.Object,
            settings.Object,
            news.Object,
            uriLauncher.Object,
            new HttpClient(),
            new StringLocalizer("en-US"),
            null,
            github.Object);

        Assert.Equal(
            isOfficialProfile ? "Hytale Account" : "Offline Account",
            viewModel.AccountType);

        var window = new MainWindow
        {
            Width = width,
            Height = height,
            DataContext = viewModel
        };

        window.Show();
        var frame = window.CaptureRenderedFrame();
        var handCursor = new Cursor(StandardCursorType.Hand).ToString();
        var arrowCursor = new Cursor(StandardCursorType.Arrow).ToString();

        Assert.NotNull(frame);
        Assert.Equal(new PixelSize(width, height), frame!.PixelSize);
        Assert.All(
            window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEnabled),
            button => Assert.Equal(handCursor, button.Cursor?.ToString()));
        Assert.All(
            window.GetVisualDescendants().OfType<ComboBox>().Where(comboBox => comboBox.IsEnabled),
            comboBox => Assert.Equal(handCursor, comboBox.Cursor?.ToString()));

        var dashboard = Assert.Single(
            window.GetVisualDescendants().OfType<DashboardView>());
        Assert.True(dashboard.IsEffectivelyVisible);
        Assert.False(dashboard.ClipToBounds);
        var dashboardSurface = dashboard.FindControl<Border>("DashboardSurface");
        Assert.NotNull(dashboardSurface);
        Assert.True(dashboardSurface!.ClipToBounds);
        Assert.Equal(new CornerRadius(23), dashboardSurface.CornerRadius);
        var dashboardAction = Assert.Single(
            dashboard.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("dashboardPrimary") && button.IsEffectivelyVisible);
        Assert.DoesNotContain(
            dashboard.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("dashboardManage"));
        Assert.DoesNotContain(
            dashboard.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("dashboardInstanceSummary"));
        Assert.DoesNotContain(
            dashboardAction.GetVisualDescendants(),
            visual => visual.RenderTransform is ScaleTransform);
        AssertNoPressScale(dashboardAction);

        Assert.NotNull(window.FindControl<Border>("ResizeNorth")?.Cursor);
        Assert.NotNull(window.FindControl<Border>("ResizeSouth")?.Cursor);
        Assert.NotNull(window.FindControl<Border>("ResizeWest")?.Cursor);
        Assert.NotNull(window.FindControl<Border>("ResizeEast")?.Cursor);
        Assert.NotNull(window.FindControl<Border>("ResizeNorthWest")?.Cursor);
        Assert.NotNull(window.FindControl<Border>("ResizeNorthEast")?.Cursor);
        Assert.NotNull(window.FindControl<Border>("ResizeSouthWest")?.Cursor);
        Assert.NotNull(window.FindControl<Border>("ResizeSouthEast")?.Cursor);

        var primaryNavigation = window.FindControl<StackPanel>("PrimaryNavigation");
        Assert.NotNull(primaryNavigation);
        Assert.DoesNotContain(
            primaryNavigation!.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == "Mods");

        var minimizeButton = window.FindControl<Button>("MinimizeWindowButton");
        var maximizeButton = window.FindControl<Button>("MaximizeWindowButton");
        var closeButton = window.FindControl<Button>("CloseWindowButton");
        Assert.NotNull(minimizeButton);
        Assert.NotNull(maximizeButton);
        Assert.NotNull(closeButton);
        Assert.All(
            new[] { minimizeButton!, maximizeButton!, closeButton! },
            button => Assert.Contains(
                button.Transitions!,
                transition => transition is BrushTransition));

        var minimizeGlyph = Assert.Single(
            minimizeButton!.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("windowMinimize"));
        var minimizePosition = minimizeGlyph.TranslatePoint(default, minimizeButton);
        Assert.NotNull(minimizePosition);
        Assert.True(minimizePosition!.Value.Y > minimizeButton.Bounds.Height / 2);
        Assert.Contains(
            minimizeGlyph.Transitions!,
            transition => transition is BrushTransition);

        foreach (var iconButton in new[] { maximizeButton!, closeButton! })
        {
            var icon = Assert.Single(
                iconButton.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>());
            Assert.Contains(
                icon.Transitions!,
                transition => transition is BrushTransition);
        }

        if (width == 1280)
        {
            var instancesButton = window.FindControl<Button>("InstancesNavButton");
            Assert.NotNull(instancesButton);

            var button = instancesButton!;
            var originalBounds = button.Bounds;
            var hoverPoint = button.TranslatePoint(
                new Point(button.Bounds.Width / 2, button.Bounds.Height / 2),
                window);
            Assert.NotNull(hoverPoint);

            window.MouseMove(hoverPoint!.Value);
            Dispatcher.UIThread.RunJobs();

            Assert.True(button.IsPointerOver);
            Assert.Equal(originalBounds, button.Bounds);
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color.A);
            Assert.DoesNotContain(
                button.GetVisualDescendants(),
                visual => visual.RenderTransform is ScaleTransform);
            AssertNoPressScale(button);

            window.MouseDown(hoverPoint.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(originalBounds, button.Bounds);
            Assert.DoesNotContain(
                button.GetVisualDescendants(),
                visual => visual.RenderTransform is ScaleTransform);
            AssertNoPressScale(button);

            window.MouseUp(hoverPoint.Value, MouseButton.Left);
        }

        viewModel.NavigateCommand.Execute("instances");
        Dispatcher.UIThread.RunJobs();
        var instancesView = Assert.Single(window.GetVisualDescendants().OfType<InstancesView>());
        var usesCompactInstancesLayout = instancesView.Bounds.Width < 940;
        Assert.True(instancesView.IsEffectivelyVisible);
        Assert.DoesNotContain(
            instancesView.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == "Instances");
        var instanceListGroup = Assert.Single(
            instancesView.GetVisualDescendants().OfType<StackPanel>(),
            panel => panel.Classes.Contains("instancesListGroup"));
        var instancesScroll = Assert.Single(
            instancesView.GetVisualDescendants().OfType<ScrollViewer>(),
            scroll => scroll.Classes.Contains("instancesScroll"));
        Assert.Equal(new Thickness(14, 18, 4, 18), instancesScroll.Margin);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, instancesScroll.HorizontalContentAlignment);
        Assert.Equal(new Thickness(0, 0, 10, 0), instanceListGroup.Margin);
        Assert.Equal(2, instanceListGroup.Spacing);
        Assert.Single(
            instanceListGroup.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("instancesAddRow"));
        Assert.Equal(
            2,
            instanceListGroup.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
                .Count(path => path.Classes.Contains("instancesListHandle") || path.Classes.Contains("instancesListMore")));
        var instanceDragHandle = Assert.Single(
            instanceListGroup.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>(),
            path => path.Classes.Contains("instancesListHandle"));
        Assert.Equal(14, instanceDragHandle.Bounds.Width);
        Assert.Equal(arrowCursor, instanceDragHandle.Cursor?.ToString());
        var instanceDragTarget = Assert.Single(
            instanceListGroup.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("instancesListDragTarget"));
        Assert.Equal(usesCompactInstancesLayout ? 44 : 40, instanceDragTarget.Bounds.Width);
        Assert.Equal(usesCompactInstancesLayout ? 44 : 40, instanceDragTarget.Bounds.Height);
        Assert.Equal(arrowCursor, instanceDragTarget.Cursor?.ToString());
        var managedInstanceRow = Assert.Single(
            instanceListGroup.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("instancesListItem") && button.Classes.Contains("managed"));
        Assert.Equal(handCursor, managedInstanceRow.Cursor?.ToString());
        Assert.Equal(usesCompactInstancesLayout ? 78 : 72, managedInstanceRow.Bounds.Height);
        Assert.Equal(new CornerRadius(11), managedInstanceRow.CornerRadius);
        if (!usesCompactInstancesLayout)
        {
            await Task.Delay(220);
            _ = window.CaptureRenderedFrame();
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal(
            usesCompactInstancesLayout ? Colors.Transparent : Color.Parse("#12FFFFFF"),
            Assert.IsAssignableFrom<ISolidColorBrush>(managedInstanceRow.Background).Color);
        var managedInstanceTitle = Assert.Single(
            managedInstanceRow.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Classes.Contains("instancesListTitle"));
        var managedInstanceDescription = Assert.Single(
            managedInstanceRow.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Classes.Contains("settingsCategoryDescription"));
        Assert.Equal(15, managedInstanceTitle.FontSize);
        Assert.Equal(11, managedInstanceDescription.FontSize);
        var managedInstanceGameIcon = Assert.Single(
            managedInstanceRow.GetVisualDescendants().OfType<Image>(),
            image => image.Classes.Contains("instancesListGameIcon"));
        Assert.NotNull(managedInstanceGameIcon.Source);
        Assert.Equal(usesCompactInstancesLayout, managedInstanceGameIcon.IsVisible);
        Assert.Equal(usesCompactInstancesLayout ? 38 : 0, managedInstanceGameIcon.Width);
        Assert.Equal(usesCompactInstancesLayout ? 38 : 0, managedInstanceGameIcon.Height);
        var managedInstanceRowPoint = managedInstanceRow.TranslatePoint(
            new Point(managedInstanceRow.Bounds.Width / 2, managedInstanceRow.Bounds.Height / 2),
            window);
        Assert.NotNull(managedInstanceRowPoint);
        window.MouseMove(managedInstanceRowPoint!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.True(managedInstanceRow.IsPointerOver);
        await Task.Delay(220);
        _ = window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(
            Color.Parse(usesCompactInstancesLayout ? "#08FFFFFF" : "#18FFFFFF"),
            Assert.IsAssignableFrom<ISolidColorBrush>(managedInstanceRow.Background).Color);

        var inactiveInstance = new InstanceItemViewModel(
            "inactive-preview",
            "Inactive Preview",
            "v41",
            "Pre-Release",
            false,
            false);
        viewModel.AllInstances.Add(inactiveInstance);
        Dispatcher.UIThread.RunJobs();
        var inactiveInstanceRow = Assert.Single(
            instanceListGroup.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("instancesListItem") && !button.Classes.Contains("managed"));
        Assert.Equal(
            Colors.Transparent,
            Assert.IsAssignableFrom<ISolidColorBrush>(inactiveInstanceRow.Background).Color);
        var inactiveInstanceRowPoint = inactiveInstanceRow.TranslatePoint(
            new Point(inactiveInstanceRow.Bounds.Width / 2, inactiveInstanceRow.Bounds.Height / 2),
            window);
        Assert.NotNull(inactiveInstanceRowPoint);
        window.MouseMove(inactiveInstanceRowPoint!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.True(inactiveInstanceRow.IsPointerOver);
        await Task.Delay(220);
        _ = window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(
            Color.Parse("#08FFFFFF"),
            Assert.IsAssignableFrom<ISolidColorBrush>(inactiveInstanceRow.Background).Color);
        window.MouseMove(new Point(0, 0));
        Dispatcher.UIThread.RunJobs();
        viewModel.AllInstances.Remove(inactiveInstance);
        Dispatcher.UIThread.RunJobs();

        var addInstanceRow = Assert.Single(
            instanceListGroup.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("instancesAddRow"));
        Assert.Equal(default, addInstanceRow.CornerRadius);
        Assert.Equal(default, addInstanceRow.BorderThickness);
        Assert.Contains(
            addInstanceRow.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == viewModel.NewInstanceTitle);
        Assert.Equal(
            Colors.Transparent,
            Assert.IsAssignableFrom<ISolidColorBrush>(addInstanceRow.Background).Color);
        Assert.Equal(
            14,
            Assert.Single(addInstanceRow.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()).Bounds.Width);

        var dragPoint = instanceDragTarget.TranslatePoint(
            new Point(instanceDragTarget.Bounds.Width - 3, instanceDragTarget.Bounds.Height / 2),
            window);
        Assert.NotNull(dragPoint);
        var resolvedDragPoint = dragPoint!.Value;
        var dragPreview = instancesView.FindControl<Border>("InstanceDragPreview");
        Assert.NotNull(dragPreview);
        window.MouseMove(resolvedDragPoint);
        window.MouseDown(resolvedDragPoint, MouseButton.Left);
        window.MouseMove(resolvedDragPoint + new Vector(36, 24));
        Dispatcher.UIThread.RunJobs();
        Assert.True(dragPreview!.IsVisible);
        Assert.Equal(default, dragPreview.BorderThickness);
        Assert.Equal(new CornerRadius(11), dragPreview.CornerRadius);
        var dragPreviewGameIcon = Assert.Single(
            dragPreview.GetVisualDescendants().OfType<Image>(),
            image => image.Classes.Contains("instancesListGameIcon"));
        Assert.Equal(usesCompactInstancesLayout, dragPreviewGameIcon.IsVisible);
        Assert.Equal(usesCompactInstancesLayout ? 38 : 0, dragPreviewGameIcon.Width);
        var dragPreviewPath = Environment.GetEnvironmentVariable(
            usesCompactInstancesLayout
                ? "HYPRISM_INSTANCES_COMPACT_DRAG_RENDER_OUTPUT"
                : "HYPRISM_INSTANCES_DRAG_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(dragPreviewPath))
            window.CaptureRenderedFrame()!.Save(dragPreviewPath, PngBitmapEncoderOptions.Default);
        window.MouseUp(resolvedDragPoint + new Vector(36, 24), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(dragPreview.IsVisible);
        var instanceActions = instancesView.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.Classes.Contains("instanceAction"))
            .ToList();
        Assert.Equal(3, instanceActions.Count);
        Assert.Single(instanceActions, button => button.Classes.Contains("primary"));
        Assert.Single(instanceActions, button => button.Classes.Contains("danger"));
        var instanceMenuRows = instancesView.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.Classes.Contains("instanceMenuRow"))
            .ToList();
        Assert.Equal(
            5,
            instanceMenuRows.Count);
        var instanceMenuIcons = instancesView.GetVisualDescendants()
            .OfType<Image>()
            .Where(image => image.Classes.Contains("instanceMenuIcon"))
            .ToList();
        Assert.Equal(5, instanceMenuIcons.Count);
        Assert.All(instanceMenuIcons, icon =>
        {
            Assert.Equal(28, icon.Width);
            Assert.Equal(28, icon.Height);
            Assert.NotNull(icon.Source);
        });
        Assert.All(instanceMenuRows.Take(4), row => Assert.InRange(row.Bounds.Height, 68.5, 69.5));
        Assert.InRange(instanceMenuRows[^1].Bounds.Height, 65.5, 66.5);
        Assert.DoesNotContain(
            instancesView.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("instanceLaunch"));
        var instanceGameIcon = Assert.Single(
            instancesView.GetVisualDescendants().OfType<Image>(),
            image => image.Classes.Contains("instanceGameIcon"));
        Assert.Equal(144, instanceGameIcon.Width);
        Assert.Equal(144, instanceGameIcon.Height);
        Assert.NotNull(instanceGameIcon.Source);
        Assert.Equal(instancesView.Bounds.Width >= 940, instanceGameIcon.IsVisible);
        var compactInstanceGameIcon = Assert.Single(
            instancesView.GetVisualDescendants().OfType<Image>(),
            image => image.Classes.Contains("compactInstanceGameIcon"));
        Assert.Equal(30, compactInstanceGameIcon.Width);
        Assert.Equal(30, compactInstanceGameIcon.Height);
        Assert.NotNull(compactInstanceGameIcon.Source);
        var instanceSummary = Assert.Single(
            instancesView.GetVisualDescendants().OfType<StackPanel>(),
            panel => panel.Classes.Contains("instanceSummary"));
        var instanceHubContent = instancesView.FindControl<StackPanel>("InstanceHubContent");
        Assert.NotNull(instanceHubContent);
        Assert.Equal(instanceHubContent!.Spacing, instanceSummary.Spacing);

        var instancesContent = instancesView.FindControl<Grid>("InstancesContent");
        var instancesListPane = instancesView.FindControl<Border>("InstancesListPane");
        var compactInstanceToolbar = instancesView.FindControl<Border>("CompactInstanceToolbar");
        var compactInstanceSplitAction = instancesView.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Classes.Contains("compactInstanceSplitAction"));
        var compactInstancePrimaryAction = instancesView.FindControl<Button>("CompactInstancePrimaryAction");
        var compactInstanceMoreButton = instancesView.FindControl<Button>("CompactInstanceMoreButton");
        var compactInstanceMenuPopup = instancesView.FindControl<FadingPopup>("CompactInstanceMenuPopup");
        var wideInstanceActions = instancesView.FindControl<StackPanel>("WideInstanceActions");
        Assert.NotNull(instancesContent);
        Assert.NotNull(instancesListPane);
        if (!usesCompactInstancesLayout)
            Assert.Equal(276, instancesListPane!.Bounds.Width);
        Assert.NotNull(compactInstanceToolbar);
        Assert.NotNull(compactInstancePrimaryAction);
        Assert.NotNull(compactInstanceMoreButton);
        Assert.NotNull(compactInstanceMenuPopup);
        Assert.True(compactInstanceMenuPopup!.WindowManagerAddShadowHint);
        Assert.False(compactInstanceMenuPopup.IsLightDismissEnabled);
        Assert.NotNull(wideInstanceActions);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, wideInstanceActions!.HorizontalAlignment);
        var instanceContentTranslation = Assert.IsType<TranslateTransform>(instancesContent!.RenderTransform);
        Assert.Equal(usesCompactInstancesLayout, compactInstanceToolbar!.IsVisible);

        if (usesCompactInstancesLayout)
        {
            Assert.True(instanceContentTranslation.X > 0);
            var compactListPreviewPath = Environment.GetEnvironmentVariable(
                "HYPRISM_INSTANCES_COMPACT_LIST_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(compactListPreviewPath))
            {
                var compactListFrame = window.CaptureRenderedFrame();
                Assert.NotNull(compactListFrame);
                compactListFrame!.Save(compactListPreviewPath, PngBitmapEncoderOptions.Default);
                Assert.True(File.Exists(compactListPreviewPath));
            }

            var instanceButton = instancesListPane!.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Classes.Contains("instancesListItem"));
            instanceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(360);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, instanceContentTranslation.X);
            Assert.False(wideInstanceActions.IsVisible);
            Assert.True(compactInstanceSplitAction.IsEffectivelyVisible);
            Assert.True(compactInstancePrimaryAction!.IsEffectivelyVisible);
            Assert.True(compactInstanceGameIcon.IsEffectivelyVisible);

            var compactActionRight = compactInstanceSplitAction.TranslatePoint(
                new Point(compactInstanceSplitAction.Bounds.Width, 0),
                window);
            var compactContentRight = instanceHubContent!.TranslatePoint(
                new Point(instanceHubContent.Bounds.Width, 0),
                window);
            Assert.NotNull(compactActionRight);
            Assert.NotNull(compactContentRight);
            Assert.InRange(
                Math.Abs(compactActionRight!.Value.X - compactContentRight!.Value.X),
                0,
                0.5);

            var compactMenuPoint = compactInstanceMoreButton!.TranslatePoint(
                new Point(compactInstanceMoreButton.Bounds.Width / 2, compactInstanceMoreButton.Bounds.Height / 2),
                window);
            Assert.NotNull(compactMenuPoint);
            window.MouseMove(compactMenuPoint!.Value);
            window.MouseDown(compactMenuPoint.Value, MouseButton.Left);
            window.MouseUp(compactMenuPoint.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(compactInstanceMenuPopup!.IsRequestedOpen);
            Assert.True(compactInstanceMenuPopup.IsOpen);
            await Task.Delay(220);
            Dispatcher.UIThread.RunJobs();

            var compactMenuPreviewPath = Environment.GetEnvironmentVariable(
                "HYPRISM_INSTANCES_COMPACT_MENU_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(compactMenuPreviewPath))
            {
                window.CaptureRenderedFrame()!.Save(compactMenuPreviewPath, PngBitmapEncoderOptions.Default);
                Assert.True(File.Exists(compactMenuPreviewPath));
            }

            window.MouseMove(compactMenuPoint.Value);
            window.MouseDown(compactMenuPoint.Value, MouseButton.Left);
            window.MouseUp(compactMenuPoint.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.False(compactInstanceMenuPopup.IsRequestedOpen);
            Assert.True(compactInstanceMenuPopup.IsOpen);
            await Task.Delay(90);
            Dispatcher.UIThread.RunJobs();

            var compactMenuClosingPreviewPath = Environment.GetEnvironmentVariable(
                "HYPRISM_INSTANCES_COMPACT_MENU_CLOSING_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(compactMenuClosingPreviewPath))
            {
                window.CaptureRenderedFrame()!.Save(compactMenuClosingPreviewPath, PngBitmapEncoderOptions.Default);
                Assert.True(File.Exists(compactMenuClosingPreviewPath));
            }

            await Task.Delay(140);
            Dispatcher.UIThread.RunJobs();
            Assert.False(compactInstanceMenuPopup.IsOpen);

            window.MouseMove(compactMenuPoint.Value);
            window.MouseDown(compactMenuPoint.Value, MouseButton.Left);
            window.MouseUp(compactMenuPoint.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(compactInstanceMenuPopup.IsRequestedOpen);
            Assert.True(compactInstanceMenuPopup.IsOpen);
            await Task.Delay(220);
            Dispatcher.UIThread.RunJobs();

            var compactMenuDismissPoint = instanceHubContent!.TranslatePoint(new Point(40, 110), window);
            Assert.NotNull(compactMenuDismissPoint);
            window.MouseMove(compactMenuDismissPoint!.Value);
            window.MouseDown(compactMenuDismissPoint.Value, MouseButton.Left);
            window.MouseUp(compactMenuDismissPoint.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.False(compactInstanceMenuPopup.IsRequestedOpen);
            Assert.True(compactInstanceMenuPopup.IsOpen);
            await Task.Delay(230);
            Dispatcher.UIThread.RunJobs();
            Assert.False(compactInstanceMenuPopup.IsOpen);

            var compactPreviewPath = Environment.GetEnvironmentVariable("HYPRISM_INSTANCES_COMPACT_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(compactPreviewPath) && width == 1024)
            {
                window.CaptureRenderedFrame()!.Save(compactPreviewPath, PngBitmapEncoderOptions.Default);
                Assert.True(File.Exists(compactPreviewPath));
            }
        }
        else
        {
            Assert.Equal(0, instanceContentTranslation.X);
            Assert.True(instancesListPane!.IsHitTestVisible);
            Assert.True(instancesContent.IsHitTestVisible);
            Assert.True(wideInstanceActions.IsEffectivelyVisible);
            Assert.False(compactInstanceSplitAction.IsEffectivelyVisible);
            Assert.False(compactInstanceGameIcon.IsEffectivelyVisible);
        }

        var instanceHub = instancesView.FindControl<Grid>("InstanceHubScreen");
        var instanceSection = instancesView.FindControl<Grid>("InstanceSectionScreen");
        var instanceInfoGroup = instancesView.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Classes.Contains("instanceInfoGroup"));
        Assert.NotNull(instanceHub);
        Assert.NotNull(instanceSection);
        Assert.True(instanceInfoGroup.IsVisible);
        Assert.Equal(new CornerRadius(14), instanceInfoGroup.CornerRadius);
        Assert.Equal(
            4,
            instanceInfoGroup.GetVisualDescendants()
                .OfType<Border>()
                .Count(border => border.Classes.Contains("instanceInfoCell")));
        viewModel.SelectInstanceSectionCommand.Execute("mods");
        Assert.True(instanceHub!.IsVisible);
        Assert.Equal(usesCompactInstancesLayout, instanceSection!.IsVisible);
        await Task.Delay(usesCompactInstancesLayout ? 360 : 240);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(usesCompactInstancesLayout, instanceHub!.IsVisible);
        Assert.True(instanceSection!.IsVisible);
        Assert.Contains("articleToolbar", instanceSection.GetVisualDescendants()
            .OfType<Border>()
            .First(border => border.Classes.Contains("articleToolbar"))
            .Classes);
        var instanceSectionTitle = instanceSection.GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(textBlock => textBlock.Classes.Contains("articleToolbarTitle"));
        Assert.Equal("Installed mods", instanceSectionTitle.Text);

        var sectionPreviewPath = Environment.GetEnvironmentVariable("HYPRISM_INSTANCES_SECTION_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(sectionPreviewPath) && width == 1280)
        {
            window.CaptureRenderedFrame()!.Save(sectionPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(sectionPreviewPath));
        }

        viewModel.CloseInstanceSectionCommand.Execute(null);
        Assert.Equal(usesCompactInstancesLayout, instanceHub.IsVisible);
        Assert.True(instanceSection.IsVisible);
        Assert.Equal("Installed mods", instanceSectionTitle.Text);
        await Task.Delay(usesCompactInstancesLayout ? 360 : 240);
        Dispatcher.UIThread.RunJobs();
        Assert.True(instanceHub.IsVisible);
        Assert.False(instanceSection.IsVisible);

        var instancesPreviewPath = Environment.GetEnvironmentVariable("HYPRISM_INSTANCES_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(instancesPreviewPath) && width == 1280)
        {
            window.CaptureRenderedFrame()!.Save(instancesPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(instancesPreviewPath));
        }

        var previewPath = Environment.GetEnvironmentVariable("HYPRISM_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(previewPath) && width == 1280)
        {
            frame.Save(previewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(previewPath));
        }

        if (width == 1920)
        {
            window.Width = 1024;
            Dispatcher.UIThread.RunJobs();
            Assert.True(compactInstanceToolbar.IsVisible);
            Assert.False(instancesContent.IsHitTestVisible);
            Assert.True(instanceContentTranslation.X > 0);
            Assert.Equal(
                Colors.Transparent,
                Assert.IsAssignableFrom<ISolidColorBrush>(addInstanceRow.Background).Color);

            window.Width = width;
            Dispatcher.UIThread.RunJobs();
            Assert.False(compactInstanceToolbar.IsVisible);
            Assert.True(instancesContent.IsHitTestVisible);

            managedInstanceRow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            window.Width = 1024;
            Dispatcher.UIThread.RunJobs();
            Assert.True(compactInstanceToolbar.IsVisible);
            Assert.True(instancesContent.IsHitTestVisible);
            Assert.Equal(0, instanceContentTranslation.X);

            Assert.True(instancesView.TryCloseCompactContent());
            window.Width = width;
            Dispatcher.UIThread.RunJobs();
        }

        viewModel.NavigateCommand.Execute("news");
        Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.HasFeaturedNews);
        Assert.Equal(3, viewModel.LatestNews.Count);
        news.Verify(
            service => service.GetNewsAsync(12),
            Times.Once);

        var newsLayout = window.FindControl<Grid>("NewsResponsiveLayout");
        var compactNewsShell = window.FindControl<Carousel>("CompactNewsShell");
        var wideNewsShell = window.FindControl<Grid>("WideNewsShell");
        var wideNewsFeedBackground = window.FindControl<Border>("WideNewsFeedBackground");
        var compactArticleHost = window.FindControl<ContentControl>("CompactArticleHost");
        var wideArticleHost = window.FindControl<ContentControl>("WideArticleHost");
        Assert.NotNull(newsLayout);
        Assert.NotNull(compactNewsShell);
        Assert.NotNull(wideNewsShell);
        Assert.NotNull(wideNewsFeedBackground);
        Assert.NotNull(compactArticleHost);
        Assert.NotNull(wideArticleHost);
        var compactTransition = Assert.IsType<PageSlide>(compactNewsShell!.PageTransition);
        Assert.Equal(PageSlide.SlideAxis.Horizontal, compactTransition.Orientation);
        Assert.IsType<CubicEaseInOut>(compactTransition.SlideInEasing);
        Assert.IsType<CubicEaseInOut>(compactTransition.SlideOutEasing);

        var usesWideLayout = newsLayout!.Bounds.Width >= 1180;
        Assert.Equal(!usesWideLayout, compactNewsShell!.IsVisible);
        Assert.Equal(usesWideLayout, wideNewsShell!.IsVisible);

        var activeNewsShell = usesWideLayout
            ? (Control)wideNewsShell
            : compactNewsShell;
        var newsListItems = activeNewsShell.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.Classes.Contains("newsListItem"))
            .ToArray();
        Assert.Equal(4, newsListItems.Length);
        Assert.All(newsListItems, item => Assert.InRange(item.Bounds.Height, 103, 105));
        Assert.All(newsListItems, item =>
        {
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(item.Background).Color.A);
            Assert.Equal(new Thickness(0), item.BorderThickness);
            Assert.Single(item.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>());

            var newsItem = Assert.IsType<NewsItemViewModel>(item.DataContext);
            var title = item.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(textBlock => textBlock.Text == newsItem.Title);
            var date = item.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(textBlock => textBlock.Text == newsItem.Date);
            var titleTop = title.TranslatePoint(default, item);
            var dateTop = date.TranslatePoint(default, item);
            Assert.NotNull(titleTop);
            Assert.NotNull(dateTop);
            Assert.True(dateTop!.Value.Y > titleTop!.Value.Y);
        });
        if (width == 1280)
        {
            var hoverTarget = newsListItems[1];
            var hoverPoint = hoverTarget.TranslatePoint(
                new Point(hoverTarget.Bounds.Width / 2, hoverTarget.Bounds.Height / 2),
                window);
            Assert.NotNull(hoverPoint);
            window.MouseMove(hoverPoint!.Value);
            await Task.Delay(220);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                Color.Parse("#08FFFFFF"),
                Assert.IsAssignableFrom<ISolidColorBrush>(hoverTarget.Background).Color);
            Assert.NotNull(hoverTarget.Transitions);
        }
        Assert.DoesNotContain(
            activeNewsShell.GetVisualDescendants().OfType<Button>(),
            button => button.Classes.Contains("newsFeaturedCard"));
        Assert.DoesNotContain(
            activeNewsShell.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => string.Equals(textBlock.Text, "Hytale", StringComparison.Ordinal) ||
                         string.Equals(textBlock.Text, "Latest", StringComparison.Ordinal));
        Assert.Equal(
            Color.Parse("#18191B"),
            Assert.IsAssignableFrom<ISolidColorBrush>(wideNewsFeedBackground!.Background).Color);
        var feedScrollViewer = activeNewsShell.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(scrollViewer => scrollViewer.Classes.Contains("newsFeedScroll"));
        Assert.Equal(ScrollBarVisibility.Auto, feedScrollViewer.VerticalScrollBarVisibility);
        var newsFeedItems = activeNewsShell.GetVisualDescendants()
            .OfType<StackPanel>()
            .Single(panel => panel.Name == "NewsFeedItems");
        Assert.Equal(new Thickness(14, 18), newsFeedItems.Margin);
        var firstNewsItemPosition = newsListItems[0].TranslatePoint(default, feedScrollViewer);
        Assert.NotNull(firstNewsItemPosition);
        Assert.InRange(firstNewsItemPosition!.Value.X, 13.5, 14.5);
        for (var index = 1; index < newsListItems.Length; index++)
        {
            var previousPosition = newsListItems[index - 1].TranslatePoint(default, feedScrollViewer);
            var currentPosition = newsListItems[index].TranslatePoint(default, feedScrollViewer);
            Assert.NotNull(previousPosition);
            Assert.NotNull(currentPosition);
            var gap = currentPosition!.Value.Y -
                      (previousPosition!.Value.Y + newsListItems[index - 1].Bounds.Height);
            Assert.InRange(gap, 1.5, 2.5);
        }
        AssertUsesApplicationScrollBar(feedScrollViewer);

        await viewModel.FeaturedNews!.OpenCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.IsNewsArticleVisible);
        Assert.False(viewModel.IsNewsFeedVisible);
        Assert.True(viewModel.FeaturedNews.IsSelected);
        Assert.Equal(usesWideLayout ? 0 : 1, viewModel.CompactNewsPageIndex);
        Assert.Equal(usesWideLayout ? 0 : 1, compactNewsShell.SelectedIndex);
        Assert.Equal("Hytale launches a new adventure", viewModel.SelectedNewsArticle?.Title);
        var selectedArticle = viewModel.SelectedNewsArticle!;
        Assert.False(viewModel.IsNewsArticleSkeletonVisible);
        Assert.Equal(7, viewModel.SelectedNewsArticle?.Blocks.Count);
        Assert.Equal(selectedArticle.Blocks.Count, selectedArticle.RenderedBlocks.Count);
        Assert.Equal(
            "Hytale Team  ·  Game Update  ·  World Design  ·  05 Aug 2026",
            selectedArticle.Metadata);
        news.Verify(
            service => service.GetNewsArticleAsync(viewModel.FeaturedNews.Url),
            Times.Once);

        await viewModel.FeaturedNews.OpenCommand.ExecuteAsync(null);
        Assert.Same(selectedArticle, viewModel.SelectedNewsArticle);
        news.Verify(
            service => service.GetNewsArticleAsync(viewModel.FeaturedNews.Url),
            Times.Once);

        await Task.Delay(220);
        Dispatcher.UIThread.RunJobs();
        var selectedNewsButton = newsListItems.Single(button =>
            ReferenceEquals(button.DataContext, viewModel.FeaturedNews));
        Assert.Equal(
            Color.Parse("#12FFFFFF"),
            Assert.IsAssignableFrom<ISolidColorBrush>(selectedNewsButton.Background).Color);

        var readerFrame = window.CaptureRenderedFrame();
        Assert.NotNull(readerFrame);
        Assert.Equal(new PixelSize(width, height), readerFrame!.PixelSize);

        var activeArticleHost = usesWideLayout ? wideArticleHost! : compactArticleHost!;
        Assert.DoesNotContain(
            activeArticleHost.GetVisualDescendants().OfType<Border>(),
            border => border.IsEffectivelyVisible && border.Classes.Contains("skeleton"));
        var articleBody = activeArticleHost.GetVisualDescendants()
            .OfType<StackPanel>()
            .Single(panel => panel.Classes.Contains("articleBody"));
        Assert.Contains("revealed", articleBody.Classes);
        Assert.NotNull(articleBody.Transitions);
        Assert.InRange(articleBody.Opacity, 0.99, 1);
        var articleScrollViewer = activeArticleHost.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(scrollViewer => scrollViewer.Classes.Contains("newsArticleScroll"));
        Assert.Equal(ScrollBarVisibility.Auto, articleScrollViewer.VerticalScrollBarVisibility);
        AssertUsesApplicationScrollBar(articleScrollViewer);
        Assert.Equal(
            usesWideLayout ? 0 : 1,
            activeArticleHost.GetVisualDescendants()
                .OfType<Button>()
                .Count(button => button.IsEffectivelyVisible && button.Classes.Contains("articleBack")));
        var articleHeader = activeArticleHost.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Classes.Contains("newsArticleHeader"));
        Assert.Equal(new Thickness(0), articleHeader.BorderThickness);
        Assert.True(
            articleHeader.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Classes.Contains("articleHeaderMask"))
                .IsEffectivelyVisible);
        var originalButton = activeArticleHost.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && button.Classes.Contains("articleAction"));
        Assert.Equal(new Thickness(0), originalButton.BorderThickness);
        Assert.Equal(
            0,
            Assert.IsAssignableFrom<ISolidColorBrush>(originalButton.Background).Color.A);
        if (usesWideLayout)
        {
            Assert.DoesNotContain(
                activeArticleHost.GetVisualDescendants().OfType<Border>(),
                border => border.IsEffectivelyVisible && border.Classes.Contains("articleToolbar"));
            Assert.Contains(originalButton, articleHeader.GetVisualDescendants());
            var headerTitle = articleHeader.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(textBlock => textBlock.IsEffectivelyVisible && textBlock.Text == selectedArticle.Title);
            var originalButtonPosition = originalButton.TranslatePoint(default, articleHeader);
            var headerTitlePosition = headerTitle.TranslatePoint(default, articleHeader);
            Assert.NotNull(originalButtonPosition);
            Assert.NotNull(headerTitlePosition);
            Assert.True(originalButtonPosition!.Value.Y < headerTitlePosition!.Value.Y);
            Assert.Equal(1, wideArticleHost!.Opacity);
        }
        else
        {
            var articleToolbar = activeArticleHost.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.IsEffectivelyVisible && border.Classes.Contains("articleToolbar"));
            Assert.Equal(new Thickness(0), articleToolbar.BorderThickness);
            Assert.InRange(articleToolbar.Margin.Left, 23.5, 24.5);
            Assert.InRange(articleToolbar.Bounds.Height, 55.5, 56.5);
            var toolbarPosition = articleToolbar.TranslatePoint(default, activeArticleHost);
            var headerPosition = articleHeader.TranslatePoint(default, activeArticleHost);
            Assert.NotNull(toolbarPosition);
            Assert.NotNull(headerPosition);
            Assert.InRange(
                Math.Abs(toolbarPosition!.Value.X - headerPosition!.Value.X),
                0,
                6);
            var backButton = articleToolbar.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.IsEffectivelyVisible && button.Classes.Contains("articleBack"));
            Assert.Equal(new Thickness(0), backButton.BorderThickness);
            Assert.Equal(
                0,
                Assert.IsAssignableFrom<ISolidColorBrush>(backButton.Background).Color.A);
            var toolbarTitle = articleToolbar.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(textBlock => textBlock.Classes.Contains("articleToolbarTitle"));
            Assert.Equal(0, toolbarTitle.Opacity);
            Assert.Equal(16, toolbarTitle.FontSize);
            var backIcon = backButton.GetVisualDescendants()
                .OfType<Avalonia.Controls.Shapes.Path>()
                .Single();
            Assert.Equal(12, backIcon.Width);
            Assert.Equal(12, backIcon.Height);
            var initialBackPosition = backButton.TranslatePoint(default, activeArticleHost);
            var initialOriginalPosition = originalButton.TranslatePoint(default, activeArticleHost);
            Assert.NotNull(initialBackPosition);
            Assert.NotNull(initialOriginalPosition);

            articleScrollViewer.Offset = new Vector(0, 54);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(280);
            Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.IsNewsArticleScrolled);
            Assert.InRange(articleToolbar.Margin.Left, 23.5, 24.5);
            Assert.InRange(toolbarTitle.Opacity, 0.99, 1);
            var scrolledBackPosition = backButton.TranslatePoint(default, activeArticleHost);
            var scrolledOriginalPosition = originalButton.TranslatePoint(default, activeArticleHost);
            var toolbarTitlePosition = toolbarTitle.TranslatePoint(default, articleToolbar);
            Assert.NotNull(scrolledBackPosition);
            Assert.NotNull(scrolledOriginalPosition);
            Assert.NotNull(toolbarTitlePosition);
            Assert.InRange(
                Math.Abs(scrolledBackPosition!.Value.X - initialBackPosition!.Value.X),
                0,
                0.5);
            Assert.InRange(
                Math.Abs(scrolledOriginalPosition!.Value.X - initialOriginalPosition!.Value.X),
                0,
                0.5);
            Assert.InRange(
                toolbarTitlePosition!.Value.X + toolbarTitle.Bounds.Width / 2,
                articleToolbar.Bounds.Width / 2 - 1,
                articleToolbar.Bounds.Width / 2 + 1);
        }
        var articleActionHoverPoint = originalButton.TranslatePoint(
            new Point(originalButton.Bounds.Width / 2, originalButton.Bounds.Height / 2),
            window);
        Assert.NotNull(articleActionHoverPoint);
        window.MouseMove(articleActionHoverPoint!.Value);
        await Task.Delay(220);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(
            0,
            Assert.IsAssignableFrom<ISolidColorBrush>(originalButton.Background).Color.A);
        Assert.NotNull(originalButton.Transitions);
        var contentHeading = activeArticleHost.GetVisualDescendants()
            .OfType<NewsRichTextBlock>()
            .Single(control => control.IsEffectivelyVisible &&
                               control.Classes.Contains("articleHeading"));
        Assert.True(double.IsNaN(contentHeading.LineHeight));
        Assert.Contains(
            contentHeading.Inlines!.OfType<Run>(),
            run => run.Text == "Creative Gameplay Quality");
        var inlineCode = activeArticleHost.GetVisualDescendants()
            .OfType<Border>()
            .First(border =>
                border.IsEffectivelyVisible &&
                border.Classes.Contains("inlineCode") &&
                border.Child is TextBlock { Text: "worldgen.reload()" });
        var inlineCodeText = Assert.IsType<TextBlock>(inlineCode.Child);
        Assert.Contains("JetBrains Mono", inlineCodeText.FontFamily.Name);
        Assert.Equal(18, inlineCodeText.LineHeight);
        Assert.Equal(new CornerRadius(6), inlineCode.CornerRadius);
        var blockCode = activeArticleHost.GetVisualDescendants()
            .OfType<Border>()
            .First(border =>
                border.IsEffectivelyVisible &&
                border.Classes.Contains("articleCode"));
        var blockCodeText = Assert.IsType<TextBlock>(blockCode.Child);
        Assert.Contains("JetBrains Mono", blockCodeText.FontFamily.Name);
        Assert.Equal(new CornerRadius(10), blockCode.CornerRadius);
        Assert.Contains(
            activeArticleHost.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.IsEffectivelyVisible &&
                         textBlock.Text == selectedArticle.Metadata);
        Assert.DoesNotContain(
            activeArticleHost.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.IsEffectivelyVisible &&
                         textBlock.Text == "A closer look at the world and its creatures.");

        var detailsButton = activeArticleHost.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.IsEffectivelyVisible &&
                              button.Classes.Contains("articleDetailsHeader"));
        var detailsBlock = Assert.IsType<NewsArticleBlockViewModel>(detailsButton.DataContext);
        Assert.False(detailsBlock.IsDetailsExpanded);
        var detailsPanel = Assert.IsType<StackPanel>(detailsButton.Parent);
        var detailsContainer = Assert.IsType<Border>(detailsPanel.Parent);
        Assert.Equal(
            detailsContainer.Bounds.Width -
                detailsContainer.BorderThickness.Left -
                detailsContainer.BorderThickness.Right,
            detailsButton.Bounds.Width,
            0.5);
        var detailsLabel = detailsButton.GetVisualDescendants()
            .OfType<NewsRichTextBlock>()
            .Single(control => control.Classes.Contains("articleDetailsSummary"));
        var detailsEmote = detailsButton.GetVisualDescendants()
            .OfType<Image>()
            .Single(image => image.Width == 24 && image.Height == 24);
        var detailsLabelPosition = detailsLabel.TranslatePoint(default, detailsButton);
        var detailsEmotePosition = detailsEmote.TranslatePoint(default, detailsButton);
        Assert.NotNull(detailsLabelPosition);
        Assert.NotNull(detailsEmotePosition);
        var detailsLabelCenter = detailsLabelPosition!.Value.Y + detailsLabel.Bounds.Height / 2;
        var detailsEmoteCenter = detailsEmotePosition!.Value.Y + detailsEmote.Bounds.Height / 2;
        Assert.InRange(Math.Abs(detailsLabelCenter - detailsEmoteCenter), 0, 0.5);
        Assert.InRange(
            detailsEmotePosition.Value.X -
                (detailsLabelPosition.Value.X + detailsLabel.Bounds.Width),
            6.5,
            7.5);
        if (width > 1024)
        {
            var detailsHoverPoint = detailsButton.TranslatePoint(
                new Point(detailsButton.Bounds.Width * 0.6, detailsButton.Bounds.Height / 2),
                window);
            Assert.NotNull(detailsHoverPoint);
            window.MouseMove(detailsHoverPoint!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.True(detailsButton.IsPointerOver);
            Assert.Equal(
                Color.Parse("#08FFFFFF"),
                Assert.IsAssignableFrom<ISolidColorBrush>(detailsButton.Background).Color);
        }
        Assert.DoesNotContain(
            activeArticleHost.GetVisualDescendants().OfType<NewsRichTextBlock>(),
            control => control.Inlines?.OfType<Run>().Any(run =>
                           run.Text == "Hidden implementation notes.") == true);
        detailsButton.Command!.Execute(detailsButton.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.True(detailsBlock.IsDetailsExpanded);
        Assert.Contains(
            activeArticleHost.GetVisualDescendants().OfType<NewsRichTextBlock>(),
            control => control.IsEffectivelyVisible &&
                       control.Inlines?.OfType<Run>().Any(run =>
                           run.Text == "Hidden implementation notes.") == true);

        var quote = activeArticleHost.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Classes.Contains("articleQuote") && border.IsVisible);
        var quoteText = quote.GetVisualDescendants().OfType<NewsRichTextBlock>().Single();
        Assert.NotNull(quoteText.Inlines);
        Assert.IsNotType<LineBreak>(quoteText.Inlines!.Last());
        var articleText = activeArticleHost.GetVisualDescendants()
            .OfType<NewsRichTextBlock>()
            .Single(control => control.IsEffectivelyVisible &&
                control.Inlines?.OfType<Run>().Any(run => run.Text == "Hytale News") == true);
        Assert.NotNull(articleText.Inlines);
        var articleInlines = articleText.Inlines!;
        var articleLink = articleInlines.OfType<Run>()
            .Single(run => run.Text == "Hytale News");
        var articleLinkForeground = Assert.IsType<SolidColorBrush>(articleLink.Foreground);
        var articleLinkUnderline = Assert.IsType<SolidColorBrush>(
            Assert.Single(articleLink.TextDecorations!).Stroke);
        Assert.Equal(Color.Parse("#C9BCFF"), articleLinkForeground.Color);
        Assert.Equal(0, articleLinkUnderline.Opacity);

        var linkedListText = activeArticleHost.GetVisualDescendants()
            .OfType<NewsRichTextBlock>()
            .Single(control => control.IsEffectivelyVisible &&
                control.Inlines?.OfType<Run>().Any(run =>
                    run.Text == "https://hytalemodding.dev/") == true);
        var linkedListRuns = linkedListText.Inlines!.OfType<Run>().ToArray();
        var linkedListLinkIndex = Array.FindIndex(linkedListRuns, run =>
            run.Text == "https://hytalemodding.dev/");
        Assert.True(linkedListLinkIndex > 0);
        Assert.EndsWith(" ", linkedListRuns[linkedListLinkIndex - 1].Text);
        Assert.DoesNotContain(linkedListText.Inlines!, inline => inline is InlineUIContainer);
        var parentListText = activeArticleHost.GetVisualDescendants()
            .OfType<NewsRichTextBlock>()
            .Single(control => control.IsEffectivelyVisible &&
                control.Inlines?.OfType<Run>().Any(run =>
                    run.Text == "Customize items further") == true);
        var parentListPoint = parentListText.TranslatePoint(default, activeArticleHost);
        var nestedListPoint = linkedListText.TranslatePoint(default, activeArticleHost);
        Assert.NotNull(parentListPoint);
        Assert.NotNull(nestedListPoint);
        Assert.True(nestedListPoint!.Value.X >= parentListPoint!.Value.X + 28);

        var nestedListRow = Assert.IsType<Grid>(linkedListText.Parent);
        var nestedMarker = nestedListRow.Children
            .OfType<Avalonia.Controls.Shapes.Ellipse>()
            .Single(ellipse => ellipse.IsVisible);
        Assert.Equal(5, nestedMarker.Width);
        Assert.Equal(5, nestedMarker.Height);
        Assert.Equal(new Thickness(1, 11.5, 0, 0), nestedMarker.Margin);

        var linkStart = 0;
        foreach (var inline in articleInlines)
        {
            if (ReferenceEquals(inline, articleLink))
                break;
            linkStart += inline switch
            {
                Run run => run.Text?.Length ?? 0,
                LineBreak => 1,
                InlineUIContainer => 1,
                _ => 0
            };
        }

        var linkBounds = articleText.TextLayout.HitTestTextPosition(linkStart);
        var linkPoint = articleText.TranslatePoint(
            new Point(
                linkBounds.X + Math.Min(4, linkBounds.Width / 2),
                linkBounds.Y + linkBounds.Height / 2),
            window);
        Assert.NotNull(linkPoint);
        window.MouseMove(linkPoint!.Value);
        await Task.Delay(200);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Cursor(StandardCursorType.Hand).ToString(), articleText.Cursor?.ToString());
        Assert.Equal(1, articleLinkUnderline.Opacity);
        Assert.Equal(Color.Parse("#E0D8FF"), articleLinkForeground.Color);

        window.MouseDown(linkPoint.Value, MouseButton.Left);
        window.MouseUp(linkPoint.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        uriLauncher.Verify(
            service => service.LaunchAsync(
                new Uri("https://hytale.com/news"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var nestedLinkRun = linkedListRuns[linkedListLinkIndex];
        var nestedUnderline = Assert.IsType<SolidColorBrush>(
            Assert.Single(nestedLinkRun.TextDecorations!).Stroke);
        var nestedLinkStart = linkedListRuns
            .Take(linkedListLinkIndex)
            .Sum(run => run.Text?.Length ?? 0);
        var nestedLinkBounds = linkedListText.TextLayout.HitTestTextPosition(nestedLinkStart);
        var nestedLinkPoint = linkedListText.TranslatePoint(
            new Point(
                nestedLinkBounds.X + Math.Min(4, nestedLinkBounds.Width / 2),
                nestedLinkBounds.Y + nestedLinkBounds.Height / 2),
            window);
        Assert.NotNull(nestedLinkPoint);
        window.MouseMove(nestedLinkPoint!.Value);
        await Task.Delay(200);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Cursor(StandardCursorType.Hand).ToString(), linkedListText.Cursor?.ToString());
        Assert.Equal(1, nestedUnderline.Opacity);
        window.MouseDown(nestedLinkPoint.Value, MouseButton.Left);
        window.MouseUp(nestedLinkPoint.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        uriLauncher.Verify(
            service => service.LaunchAsync(
                new Uri("https://hytalemodding.dev/"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var policyLinkIndex = Array.FindIndex(linkedListRuns, run =>
            run.Text == "Server Owner Policies");
        Assert.True(policyLinkIndex > linkedListLinkIndex);
        var policyLinkRun = linkedListRuns[policyLinkIndex];
        var policyUnderline = Assert.IsType<SolidColorBrush>(
            Assert.Single(policyLinkRun.TextDecorations!).Stroke);
        var policyLinkStart = linkedListRuns
            .Take(policyLinkIndex)
            .Sum(run => run.Text?.Length ?? 0);
        var policyLinkBounds = linkedListText.TextLayout.HitTestTextPosition(policyLinkStart);
        var policyLinkPoint = linkedListText.TranslatePoint(
            new Point(
                policyLinkBounds.X + Math.Min(4, policyLinkBounds.Width / 2),
                policyLinkBounds.Y + policyLinkBounds.Height / 2),
            window);
        Assert.NotNull(policyLinkPoint);
        window.MouseMove(policyLinkPoint!.Value);
        await Task.Delay(200);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, policyUnderline.Opacity);
        window.MouseDown(policyLinkPoint.Value, MouseButton.Left);
        window.MouseUp(policyLinkPoint.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        uriLauncher.Verify(
            service => service.LaunchAsync(
                new Uri("https://hytale.com/server-policies"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var readerPreviewPath = Environment.GetEnvironmentVariable("HYPRISM_READER_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(readerPreviewPath) && width == 1280)
        {
            readerFrame.Save(readerPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(readerPreviewPath));
        }

        var compactReaderPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_READER_COMPACT_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(compactReaderPreviewPath) && width == 1024)
        {
            readerFrame.Save(compactReaderPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(compactReaderPreviewPath));
        }

        var wideReaderPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_READER_WIDE_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(wideReaderPreviewPath) && width == 1920)
        {
            readerFrame.Save(wideReaderPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(wideReaderPreviewPath));
        }

        var closeTask = viewModel.CloseNewsArticleCommand.ExecuteAsync(null);
        if (!usesWideLayout)
        {
            Assert.Same(selectedArticle, viewModel.SelectedNewsArticle);
            Assert.Equal(0, viewModel.CompactNewsPageIndex);
            Assert.DoesNotContain(
                activeArticleHost.GetVisualDescendants().OfType<TextBlock>(),
                textBlock => textBlock.IsEffectivelyVisible &&
                             textBlock.Text == viewModel.SelectArticleLabel);
        }
        await closeTask;
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.IsNewsFeedVisible);
        Assert.Null(viewModel.SelectedNewsArticle);
        Assert.False(viewModel.FeaturedNews.IsSelected);
        Assert.Equal(0, viewModel.CompactNewsPageIndex);
        Assert.Equal(0, compactNewsShell.SelectedIndex);

        await viewModel.FeaturedNews.OpenCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(selectedArticle, viewModel.SelectedNewsArticle);
        Assert.False(viewModel.IsNewsArticleSkeletonVisible);
        Assert.DoesNotContain(
            activeArticleHost.GetVisualDescendants().OfType<Border>(),
            border => border.IsEffectivelyVisible && border.Classes.Contains("skeleton"));
        news.Verify(
            service => service.GetNewsArticleAsync(viewModel.FeaturedNews.Url),
            Times.Once);
        await viewModel.CloseNewsArticleCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var newsPreviewPath = Environment.GetEnvironmentVariable("HYPRISM_NEWS_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(newsPreviewPath) && width == 1280)
        {
            var newsFrame = window.CaptureRenderedFrame();
            Assert.NotNull(newsFrame);
            newsFrame!.Save(newsPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(newsPreviewPath));
        }

        var compactNewsPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_NEWS_COMPACT_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(compactNewsPreviewPath) && width == 1024)
        {
            var newsFrame = window.CaptureRenderedFrame();
            Assert.NotNull(newsFrame);
            newsFrame!.Save(compactNewsPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(compactNewsPreviewPath));
        }

        var wideNewsPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_NEWS_WIDE_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(wideNewsPreviewPath) && width == 1920)
        {
            var newsFrame = window.CaptureRenderedFrame();
            Assert.NotNull(newsFrame);
            newsFrame!.Save(wideNewsPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(wideNewsPreviewPath));
        }

        viewModel.NavigateCommand.Execute("settings");
        Dispatcher.UIThread.RunJobs();

        var settingsView = window.GetVisualDescendants().OfType<SettingsView>().Single();
        var settingsScroll = settingsView.FindControl<ScrollViewer>("SettingsContent");
        var categoryScroll = settingsView.FindControl<ScrollViewer>("SettingsCategoryScroll");
        var settingsRail = settingsView.FindControl<Border>("SettingsCategoryRail");
        var compactSettingsToolbar = settingsView.FindControl<Border>("CompactSettingsToolbar");
        var compactSettingsTitle = settingsView.FindControl<TextBlock>("CompactSettingsTitle");
        var settingsMain = settingsView.FindControl<Grid>("SettingsMain");
        Assert.NotNull(settingsScroll);
        Assert.NotNull(categoryScroll);
        Assert.NotNull(settingsRail);
        Assert.NotNull(compactSettingsToolbar);
        Assert.NotNull(compactSettingsTitle);
        Assert.NotNull(settingsMain);
        AssertUsesApplicationScrollBar(categoryScroll!);
        AssertUsesApplicationScrollBar(settingsScroll!);
        var settingsToggles = settingsView.GetVisualDescendants().OfType<ToggleSwitch>().ToArray();
        Assert.NotEmpty(settingsToggles);
        Assert.All(settingsToggles, toggle =>
        {
            Assert.Null(toggle.OnContent);
            Assert.Null(toggle.OffContent);
            Assert.Equal(new Thickness(0), toggle.BorderThickness);
            Assert.Equal(48, toggle.Width);
        });
        Assert.All(settingsToggles.Where(toggle => toggle.IsEffectivelyVisible), toggle =>
        {
            var track = toggle.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "SettingsSwitchTrack");
            var movingKnobs = toggle.GetVisualDescendants()
                .OfType<Grid>()
                .Single(grid => grid.Name == "PART_MovingKnobs");
            var knob = toggle.GetVisualDescendants()
                .OfType<Avalonia.Controls.Shapes.Ellipse>()
                .Single(ellipse => ellipse.Name == "SettingsSwitchKnob");
            Assert.Equal(new Thickness(0), track.BorderThickness);
            Assert.Equal(new CornerRadius(13), track.CornerRadius);
            Assert.Equal(18, knob.Width);
            Assert.Equal(18, knob.Height);
            var movement = Assert.IsType<DoubleTransition>(Assert.Single(
                movingKnobs.Transitions!,
                transition => transition is DoubleTransition { Property: { } property } &&
                              property == Canvas.LeftProperty));
            Assert.Equal(TimeSpan.FromMilliseconds(220), movement.Duration);
            Assert.IsType<CubicEaseInOut>(movement.Easing);
        });
        if (width == 1280)
        {
            var toggle = settingsToggles.First(item => item.IsEffectivelyVisible);
            var track = toggle.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "SettingsSwitchTrack");
            Assert.Equal(
                Color.Parse("#303237"),
                Assert.IsAssignableFrom<ISolidColorBrush>(track.Background).Color);
            toggle.IsChecked = true;
            await Task.Delay(240);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                Color.Parse("#35A85B"),
                Assert.IsAssignableFrom<ISolidColorBrush>(track.Background).Color);
            toggle.IsChecked = false;
        }
        var settingsComboBoxes = settingsView.GetVisualDescendants().OfType<ComboBox>().ToArray();
        Assert.NotEmpty(settingsComboBoxes);
        Assert.All(settingsComboBoxes, comboBox =>
        {
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, comboBox.HorizontalAlignment);
            Assert.Equal(new Thickness(0), comboBox.BorderThickness);
            Assert.Equal(new CornerRadius(11), comboBox.CornerRadius);
            Assert.NotNull(comboBox.ItemContainerTheme);
        });
        var settingsTextBoxes = settingsView.GetVisualDescendants().OfType<TextBox>().ToArray();
        Assert.NotEmpty(settingsTextBoxes);
        Assert.All(settingsTextBoxes, textBox =>
        {
            Assert.Equal(new Thickness(0), textBox.BorderThickness);
            Assert.Equal(new CornerRadius(11), textBox.CornerRadius);
            Assert.Null(textBox.FocusAdorner);
        });
        var compactSettingsLayout = settingsView.Bounds.Width < 940;
        Assert.True(settingsRail!.IsEffectivelyVisible);
        Assert.Equal(compactSettingsLayout, compactSettingsToolbar!.IsVisible);
        Assert.Equal(!compactSettingsLayout, settingsMain!.IsHitTestVisible);
        var categoryIcons = settingsView.GetVisualDescendants()
            .OfType<Image>()
            .Where(image => image.Classes.Contains("settingsCategoryIcon"))
            .ToArray();
        Assert.Equal(9, categoryIcons.Length);
        Assert.All(categoryIcons, icon => Assert.NotNull(icon.Source));
        Assert.Equal(9, categoryIcons.Count(icon => icon.IsEffectivelyVisible));
        var categoryDescriptions = settingsView.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.Classes.Contains("settingsCategoryDescription"))
            .ToArray();
        Assert.Equal(9, categoryDescriptions.Length);
        Assert.Equal(9, categoryDescriptions.Count(description => description.IsEffectivelyVisible));
        var categoryTitles = settingsView.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.Classes.Contains("settingsCategoryTitle"))
            .ToArray();
        Assert.Equal(9, categoryTitles.Length);
        Assert.All(
            categoryTitles,
            title => Assert.Equal(
                Color.Parse("#F7F7F8"),
                Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground).Color));
        var categoryButtons = settingsView.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.Classes.Contains("settingsRailCategory"))
            .ToArray();
        Assert.Equal(9, categoryButtons.Length);
        Assert.All(
            categoryButtons,
            button => Assert.InRange(
                Math.Abs(button.Bounds.Width - (categoryScroll!.Viewport.Width - 10)),
                0,
                1));
        if (compactSettingsLayout)
        {
            window.MouseMove(new Point(0, 0));
            await Task.Delay(220);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                Color.Parse("#0D0E10"),
                Assert.IsAssignableFrom<ISolidColorBrush>(settingsRail.Background).Color);
            var selectedCategoryButton = settingsView.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Classes.Contains("settingsRailCategory") &&
                                  button.Classes.Contains("selected"));
            Assert.Equal(
                0,
                Assert.IsAssignableFrom<ISolidColorBrush>(selectedCategoryButton.Background).Color.A);
            var selectedCategoryPoint = selectedCategoryButton.TranslatePoint(
                new Point(
                    selectedCategoryButton.Bounds.Width / 2,
                    selectedCategoryButton.Bounds.Height / 2),
                window);
            Assert.NotNull(selectedCategoryPoint);
            window.MouseMove(selectedCategoryPoint!.Value);
            await Task.Delay(220);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                8,
                Assert.IsAssignableFrom<ISolidColorBrush>(selectedCategoryButton.Background).Color.A);
            AssertNoPressScale(selectedCategoryButton);
            window.MouseMove(new Point(0, 0));
            await Task.Delay(220);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                0,
                Assert.IsAssignableFrom<ISolidColorBrush>(selectedCategoryButton.Background).Color.A);

            var categoryScrollBar = categoryScroll.GetVisualDescendants()
                .OfType<ScrollBar>()
                .Single(scrollBar => scrollBar.Orientation == Avalonia.Layout.Orientation.Vertical);
            Assert.True(categoryScrollBar.IsVisible);
            var categoryScrollThumb = categoryScrollBar.GetVisualDescendants().OfType<Thumb>().Single();
            var scrollBarPoint = categoryScrollThumb.TranslatePoint(
                new Point(categoryScrollThumb.Bounds.Width / 2, categoryScrollThumb.Bounds.Height / 2),
                window);
            Assert.NotNull(scrollBarPoint);
            window.MouseMove(scrollBarPoint!.Value);
            await Task.Delay(700);
            Dispatcher.UIThread.RunJobs();
            Assert.True(categoryScrollBar.IsExpanded);
            var expandedThumb = categoryScrollBar.GetVisualDescendants().OfType<Thumb>().Single();
            Assert.Equal(6, expandedThumb.Width);
            Assert.InRange(expandedThumb.Bounds.Width, 5.5, 6.5);
            var expandedThumbCenter = expandedThumb.TranslatePoint(
                new Point(expandedThumb.Bounds.Width / 2, expandedThumb.Bounds.Height / 2),
                categoryScrollBar);
            Assert.NotNull(expandedThumbCenter);
            Assert.InRange(
                Math.Abs(expandedThumbCenter!.Value.X - (categoryScrollBar.Bounds.Width / 2)),
                0,
                1);

            var expandedScrollBarPreviewPath = Environment.GetEnvironmentVariable(
                "HYPRISM_SETTINGS_SCROLLBAR_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(expandedScrollBarPreviewPath) && width == 1024)
            {
                var settingsFrame = window.CaptureRenderedFrame();
                Assert.NotNull(settingsFrame);
                settingsFrame!.Save(expandedScrollBarPreviewPath, PngBitmapEncoderOptions.Default);
                Assert.True(File.Exists(expandedScrollBarPreviewPath));
            }

            window.MouseMove(new Point(0, 0));
        }
        else
        {
            Assert.Equal(276, settingsRail.Bounds.Width);
            Assert.Equal(
                Color.Parse("#18191B"),
                Assert.IsAssignableFrom<ISolidColorBrush>(settingsRail.Background).Color);
        }
        var visibleSettingsGroups = settingsView.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.IsEffectivelyVisible && border.Classes.Contains("settingsGroup"))
            .ToArray();
        Assert.Equal(2, visibleSettingsGroups.Length);
        Assert.All(visibleSettingsGroups, group =>
        {
            Assert.Equal(new Thickness(0), group.BorderThickness);
            Assert.Equal(
                Color.Parse("#151618"),
                Assert.IsAssignableFrom<ISolidColorBrush>(group.Background).Color);
            Assert.Equal(new CornerRadius(14), group.CornerRadius);

            var rows = group.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.IsEffectivelyVisible && border.Classes.Contains("settingsRow"))
                .ToArray();
            Assert.NotEmpty(rows);
            Assert.All(rows.Where(row => !row.Classes.Contains("last")), row =>
            {
                Assert.Equal(new Thickness(0, 0, 0, 3), row.BorderThickness);
                Assert.Equal(
                    Color.Parse("#0D0E10"),
                    Assert.IsAssignableFrom<ISolidColorBrush>(row.BorderBrush).Color);
            });
        });
        var visibleSettingsHeadings = settingsView.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && text.Classes.Contains("settingsCategoryHeading"))
            .ToArray();
        Assert.Equal(2, visibleSettingsHeadings.Length);
        Assert.Contains(visibleSettingsHeadings, heading => heading.Text == viewModel.Settings.LanguageCategoryTitle);
        Assert.Contains(visibleSettingsHeadings, heading => heading.Text == viewModel.Settings.GeneralTitle);
        Assert.DoesNotContain(
            settingsView.GetVisualDescendants().OfType<Grid>(),
            grid => grid.Name == "SettingsHeader");
        Assert.Equal(9, viewModel.Settings.Categories.Count);
        Assert.DoesNotContain(viewModel.Settings.Categories, category => category.Id == "developer");
        Assert.True(viewModel.Settings.IsGeneral);
        Assert.All(categoryButtons, AssertNoPressScale);

        var javaCategory = viewModel.Settings.Categories.Single(category => category.Id == "java");
        viewModel.Settings.SelectCategoryCommand.Execute(javaCategory);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(
            3,
            settingsView.GetVisualDescendants().OfType<Border>().Count(
                border => border.IsEffectivelyVisible && border.Classes.Contains("settingsGroup")));
        var javaCategoryHeadings = settingsView.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && text.Classes.Contains("settingsCategoryHeading"))
            .Select(text => text.Text)
            .ToArray();
        Assert.Contains(viewModel.Settings.JavaRuntimeLabel, javaCategoryHeadings);
        Assert.Contains(viewModel.Settings.RamAllocationLabel, javaCategoryHeadings);
        Assert.Contains(viewModel.Settings.JavaArgumentsLabel, javaCategoryHeadings);
        var maximumMemorySlider = settingsView.FindControl<Slider>("JavaMaximumMemorySlider");
        var initialMemorySlider = settingsView.FindControl<Slider>("JavaInitialMemorySlider");
        Assert.NotNull(maximumMemorySlider);
        Assert.NotNull(initialMemorySlider);
        Assert.Equal(1024, maximumMemorySlider!.Minimum);
        Assert.Equal(viewModel.Settings.MaximumJavaRamMb, maximumMemorySlider.Maximum);
        Assert.Equal(256, maximumMemorySlider.TickFrequency);
        Assert.True(maximumMemorySlider.IsSnapToTickEnabled);
        Assert.Equal(1024, initialMemorySlider!.Minimum);
        Assert.Equal(viewModel.Settings.JavaMaximumRamMb, initialMemorySlider.Maximum);
        Assert.Equal(256, initialMemorySlider.TickFrequency);
        Assert.True(initialMemorySlider.IsSnapToTickEnabled);
        Assert.True(viewModel.Settings.JavaInitialRamMb <= viewModel.Settings.JavaMaximumRamMb);
        viewModel.Settings.JavaArguments = "-Xms1G -Xmx8G -Dfile.encoding=UTF-8";
        Assert.Equal("-Dfile.encoding=UTF-8", viewModel.Settings.JavaArguments);
        Assert.True(viewModel.Settings.HasJavaArgumentsError);
        Assert.DoesNotContain(
            settingsView.GetVisualDescendants().OfType<TextBlock>(),
            text => string.Equals(text.Text, "G1GC", StringComparison.OrdinalIgnoreCase));

        var javaSettingsPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_JAVA_SETTINGS_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(javaSettingsPreviewPath) && width == 1280)
        {
            var javaSettingsFrame = window.CaptureRenderedFrame();
            Assert.NotNull(javaSettingsFrame);
            javaSettingsFrame!.Save(javaSettingsPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(javaSettingsPreviewPath));
        }

        var visualCategory = viewModel.Settings.Categories.Single(category => category.Id == "visual");
        viewModel.Settings.SelectCategoryCommand.Execute(visualCategory);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(7, viewModel.Settings.Backgrounds.Count);
        Assert.True(viewModel.Settings.Backgrounds[0].IsAuto);
        Assert.Equal(3, viewModel.Settings.Backgrounds[0].Previews.Count);
        Assert.Equal(
            2,
            settingsView.GetVisualDescendants().OfType<Border>().Count(
                border => border.IsEffectivelyVisible && border.Classes.Contains("settingsGroup")));
        var visualCategoryHeadings = settingsView.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && text.Classes.Contains("settingsCategoryHeading"))
            .Select(text => text.Text)
            .ToArray();
        Assert.Equal(2, visualCategoryHeadings.Length);
        Assert.Equal(viewModel.Settings.VisualTitle, visualCategoryHeadings[0]);
        Assert.Equal(viewModel.Settings.BackgroundLabel, visualCategoryHeadings[1]);
        var backgroundChoices = settingsView.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.IsEffectivelyVisible && button.Classes.Contains("backgroundChoice"))
            .ToArray();
        Assert.Equal(7, backgroundChoices.Length);
        Assert.All(backgroundChoices, choice =>
        {
            Assert.InRange(choice.Bounds.Width / choice.Bounds.Height, 1.76, 1.79);
            Assert.True(choice.Bounds.Width >= 120);
        });
        var firstBackgroundRowY = backgroundChoices[0].TranslatePoint(default, settingsView)!.Value.Y;
        var backgroundsInFirstRow = backgroundChoices.Count(choice =>
            Math.Abs(choice.TranslatePoint(default, settingsView)!.Value.Y - firstBackgroundRowY) < 1);
        Assert.Equal(width == 1920 ? 4 : 3, backgroundsInFirstRow);
        var visualSettingsPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_VISUAL_SETTINGS_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(visualSettingsPreviewPath) && width == 1280)
        {
            var visualSettingsFrame = window.CaptureRenderedFrame();
            Assert.NotNull(visualSettingsFrame);
            visualSettingsFrame!.Save(visualSettingsPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(visualSettingsPreviewPath));
        }

        var generalCategory = viewModel.Settings.Categories.Single(category => category.Id == "general");
        viewModel.Settings.SelectCategoryCommand.Execute(generalCategory);
        Dispatcher.UIThread.RunJobs();

        var settingsPreviewPath = Environment.GetEnvironmentVariable("HYPRISM_SETTINGS_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(settingsPreviewPath) && width == 1280)
        {
            var settingsFrame = window.CaptureRenderedFrame();
            Assert.NotNull(settingsFrame);
            settingsFrame!.Save(settingsPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(settingsPreviewPath));
        }

        var compactSettingsPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_SETTINGS_COMPACT_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(compactSettingsPreviewPath) && width == 1024)
        {
            var settingsFrame = window.CaptureRenderedFrame();
            Assert.NotNull(settingsFrame);
            settingsFrame!.Save(compactSettingsPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(compactSettingsPreviewPath));
        }

        var wideSettingsPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_SETTINGS_WIDE_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(wideSettingsPreviewPath) && width == 1920)
        {
            var settingsFrame = window.CaptureRenderedFrame();
            Assert.NotNull(settingsFrame);
            settingsFrame!.Save(wideSettingsPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(wideSettingsPreviewPath));
        }

        if (width == 1920)
        {
            window.Width = 1024;
            Dispatcher.UIThread.RunJobs();
            Assert.True(compactSettingsToolbar.IsVisible);
            Assert.False(settingsMain.IsHitTestVisible);
            Assert.True(Assert.IsType<TranslateTransform>(settingsMain.RenderTransform).X > 0);
            Assert.True(settingsRail!.IsHitTestVisible);

            window.Width = width;
            Dispatcher.UIThread.RunJobs();
            Assert.False(compactSettingsToolbar.IsVisible);

            var generalCategoryButton = settingsView.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Classes.Contains("settingsRailCategory") &&
                                  button.DataContext is SettingCategoryViewModel { Id: "general" });
            generalCategoryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            window.Width = 1024;
            Dispatcher.UIThread.RunJobs();
            Assert.True(compactSettingsToolbar.IsVisible);
            Assert.True(settingsMain.IsHitTestVisible);
            Assert.Equal(0, Assert.IsType<TranslateTransform>(settingsMain.RenderTransform).X);
            Assert.True(compactSettingsTitle!.IsEffectivelyVisible);
            Assert.Equal(viewModel.Settings.ActiveCategoryTitle, compactSettingsTitle.Text);

            Assert.True(settingsView.TryCloseCompactContent());
            window.Width = width;
            Dispatcher.UIThread.RunJobs();
        }

        if (compactSettingsLayout)
        {
            var downloadsCategory = settingsView.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Classes.Contains("settingsRailCategory") &&
                                  button.DataContext is SettingCategoryViewModel { Id: "downloads" });
            var categoryPoint = downloadsCategory.TranslatePoint(
                new Point(downloadsCategory.Bounds.Width / 2, downloadsCategory.Bounds.Height / 2),
                window);
            Assert.NotNull(categoryPoint);
            window.MouseDown(categoryPoint!.Value, MouseButton.Left);
            window.MouseUp(categoryPoint.Value, MouseButton.Left);
            await Task.Delay(340);
            Dispatcher.UIThread.RunJobs();
            Assert.True(settingsMain.IsHitTestVisible);
            Assert.Equal(0, Assert.IsType<TranslateTransform>(settingsMain.RenderTransform).X);
            Assert.True(compactSettingsTitle!.IsEffectivelyVisible);
            Assert.Equal(viewModel.Settings.DownloadsTitle, compactSettingsTitle.Text);
            var compactTitleCenter = compactSettingsTitle.TranslatePoint(
                new Point(compactSettingsTitle.Bounds.Width / 2, compactSettingsTitle.Bounds.Height / 2),
                compactSettingsToolbar);
            Assert.NotNull(compactTitleCenter);
            Assert.InRange(
                Math.Abs(compactTitleCenter!.Value.X - (compactSettingsToolbar.Bounds.Width / 2)),
                0,
                1);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await Task.Delay(340);
            Dispatcher.UIThread.RunJobs();
            Assert.False(settingsMain.IsHitTestVisible);
            Assert.True(Assert.IsType<TranslateTransform>(settingsMain.RenderTransform).X > 0);

            window.MouseDown(categoryPoint.Value, MouseButton.Left);
            window.MouseUp(categoryPoint.Value, MouseButton.Left);
            await Task.Delay(340);
            Dispatcher.UIThread.RunJobs();
            Assert.True(settingsMain.IsHitTestVisible);
            Assert.Equal(0, Assert.IsType<TranslateTransform>(settingsMain.RenderTransform).X);

            var compactSettingsContentPreviewPath = Environment.GetEnvironmentVariable(
                "HYPRISM_SETTINGS_COMPACT_CONTENT_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(compactSettingsContentPreviewPath) && width == 1024)
            {
                var settingsContentFrame = window.CaptureRenderedFrame();
                Assert.NotNull(settingsContentFrame);
                settingsContentFrame!.Save(
                    compactSettingsContentPreviewPath,
                    PngBitmapEncoderOptions.Default);
                Assert.True(File.Exists(compactSettingsContentPreviewPath));
            }

            var settingsBack = settingsView.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.IsEffectivelyVisible && button.Classes.Contains("articleBack"));
            var backPoint = settingsBack.TranslatePoint(
                new Point(settingsBack.Bounds.Width / 2, settingsBack.Bounds.Height / 2),
                window);
            Assert.NotNull(backPoint);
            window.MouseDown(backPoint!.Value, MouseButton.Left);
            window.MouseUp(backPoint.Value, MouseButton.Left);
            await Task.Delay(340);
            Dispatcher.UIThread.RunJobs();
            Assert.False(settingsMain.IsHitTestVisible);
            Assert.True(Assert.IsType<TranslateTransform>(settingsMain.RenderTransform).X > 0);
        }

        foreach (var category in viewModel.Settings.Categories)
        {
            viewModel.Settings.SelectCategoryCommand.Execute(category);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(category.Id, viewModel.Settings.SelectedCategory);
            Assert.Same(category, Assert.Single(viewModel.Settings.Categories, item => item.IsSelected));
            Assert.Contains(
                settingsView.GetVisualDescendants().OfType<Border>(),
                border => border.IsEffectivelyVisible && border.Classes.Contains("settingsGroup"));
        }

        var aboutCategory = viewModel.Settings.Categories.Single(category => category.Id == "about");
        viewModel.Settings.SelectCategoryCommand.Execute(aboutCategory);
        for (var attempt = 0; attempt < 50 && viewModel.Settings.IsAboutDataLoading; attempt++)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }
        Dispatcher.UIThread.RunJobs();
        Assert.False(string.IsNullOrWhiteSpace(viewModel.Settings.AboutCurrentVersion));
        Assert.Equal(6, viewModel.Settings.AboutTeamMembers.Count);
        Assert.Contains(viewModel.Settings.AboutTeamMembers, member => member.GitHubLogin == "Aarav2709");
        Assert.DoesNotContain(viewModel.Settings.AboutTeamMembers, member => member.GitHubLogin == "CupRusk");
        Assert.All(viewModel.Settings.AboutTeamMembers, member => Assert.True(member.HasAvatar));
        Assert.InRange(viewModel.Settings.AboutContributors.Count, 1, 10);
        Assert.DoesNotContain(
            viewModel.Settings.AboutContributors,
            contributor => contributor.Login.Contains("bot", StringComparison.OrdinalIgnoreCase));
        var hiddenContributorCount = viewModel.Settings.HasMoreAboutContributors
            ? int.Parse(viewModel.Settings.AboutContributorOverflow[1..], CultureInfo.InvariantCulture)
            : 0;
        Assert.Equal(10, viewModel.Settings.AboutContributors.Count + hiddenContributorCount);
        if (width >= 1920)
        {
            Assert.Equal(10, viewModel.Settings.AboutContributors.Count);
            Assert.False(viewModel.Settings.HasMoreAboutContributors);
        }
        else
        {
            Assert.True(viewModel.Settings.HasMoreAboutContributors);
        }
        Assert.Equal("abcdef1", viewModel.Settings.AboutLatestCommitSha);
        Assert.Equal("feat: refine the native About page", viewModel.Settings.AboutLatestCommitHint);
        Assert.DoesNotContain(
            settingsView.GetVisualDescendants().OfType<Border>(),
            border => border.IsEffectivelyVisible && border.Classes.Contains("aboutHero"));
        Assert.Equal(
            6,
            settingsView.GetVisualDescendants().OfType<Button>().Count(
                button => button.IsEffectivelyVisible && button.Classes.Contains("aboutTeamMember")));
        Assert.Equal(
            viewModel.Settings.AboutContributors.Count + (viewModel.Settings.HasMoreAboutContributors ? 1 : 0),
            settingsView.GetVisualDescendants().OfType<Button>().Count(
                button => button.IsEffectivelyVisible && button.Classes.Contains("aboutContributor")));
        var contributorsContainer = settingsView.FindControl<Border>("AboutContributorsContainer");
        var contributorsRow = settingsView.FindControl<StackPanel>("AboutContributorsRow");
        Assert.NotNull(contributorsContainer);
        Assert.NotNull(contributorsRow);
        var contributorsRowCenter = contributorsRow!.TranslatePoint(
            new Point(contributorsRow.Bounds.Width / 2, contributorsRow.Bounds.Height / 2),
            contributorsContainer);
        Assert.NotNull(contributorsRowCenter);
        Assert.InRange(
            Math.Abs(contributorsRowCenter!.Value.X - contributorsContainer!.Bounds.Width / 2),
            0,
            1);
        Assert.DoesNotContain(
            settingsView.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.IsEffectivelyVisible && textBlock.Text == viewModel.Settings.AboutTeamHint);
        Assert.All(
            settingsView.GetVisualDescendants().OfType<Button>().Where(
                button => button.IsEffectivelyVisible &&
                          (button.Classes.Contains("aboutLink") ||
                           button.Classes.Contains("aboutTeamMember") ||
                           button.Classes.Contains("aboutContributor"))),
            AssertNoPressScale);
        Assert.All(
            settingsView.GetVisualDescendants()
                .OfType<Avalonia.Controls.Shapes.Path>()
                .Where(path => path.IsEffectivelyVisible && path.Classes.Contains("aboutOpenIcon")),
            path => Assert.Contains(
                path.Transitions!,
                transition => transition is BrushTransition { Property: { } property } &&
                              property == Avalonia.Controls.Shapes.Shape.FillProperty));
        var aboutDisclaimer = settingsView.GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(textBlock => textBlock.IsEffectivelyVisible &&
                                 textBlock.Text == viewModel.Settings.AboutDisclaimer);
        Assert.IsType<StackPanel>(aboutDisclaimer.Parent);

        viewModel.Settings.OpenDocumentationCommand.Execute(null);
        uriLauncher.Verify(
            service => service.LaunchAsync(
                new Uri("https://hyprismteam.github.io/HyPrism/docs/"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        viewModel.Settings.OpenLatestCommitCommand.Execute(null);
        uriLauncher.Verify(
            service => service.LaunchAsync(
                new Uri("https://github.com/hyprismteam/HyPrism/commit/abcdef1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        viewModel.Settings.OpenAllContributorsCommand.Execute(null);
        uriLauncher.Verify(
            service => service.LaunchAsync(
                new Uri("https://github.com/hyprismteam/HyPrism/graphs/contributors"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        viewModel.Settings.OpenHytaleEulaCommand.Execute(null);
        uriLauncher.Verify(
            service => service.LaunchAsync(
                new Uri("https://hytale.com/eula"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        var aboutPreviewPath = Environment.GetEnvironmentVariable("HYPRISM_ABOUT_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(aboutPreviewPath) && width == 1280)
        {
            settingsScroll.ScrollToHome();
            Dispatcher.UIThread.RunJobs();
            var aboutFrame = window.CaptureRenderedFrame();
            Assert.NotNull(aboutFrame);
            aboutFrame!.Save(aboutPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(aboutPreviewPath));
        }

        var aboutTeamPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_ABOUT_TEAM_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(aboutTeamPreviewPath) && width == 1280)
        {
            settingsScroll.ScrollToEnd();
            Dispatcher.UIThread.RunJobs();
            var aboutTeamFrame = window.CaptureRenderedFrame();
            Assert.NotNull(aboutTeamFrame);
            aboutTeamFrame!.Save(aboutTeamPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(aboutTeamPreviewPath));
        }

        var compactAboutPreviewPath = Environment.GetEnvironmentVariable(
            "HYPRISM_ABOUT_COMPACT_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(compactAboutPreviewPath) && width == 1024)
        {
            var aboutCategoryButton = settingsView.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.IsEffectivelyVisible && ReferenceEquals(button.DataContext, aboutCategory));
            aboutCategoryButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(340);
            settingsScroll.ScrollToHome();
            Dispatcher.UIThread.RunJobs();
            var compactAboutFrame = window.CaptureRenderedFrame();
            Assert.NotNull(compactAboutFrame);
            compactAboutFrame!.Save(compactAboutPreviewPath, PngBitmapEncoderOptions.Default);
            Assert.True(File.Exists(compactAboutPreviewPath));
        }

        window.Close();
    }

    private static void AssertNoPressScale(Control control)
    {
        if (control.RenderTransform is ScaleTransform scale)
        {
            Assert.Equal(1, scale.ScaleX);
            Assert.Equal(1, scale.ScaleY);
        }
    }

    private static void AssertUsesApplicationScrollBar(ScrollViewer scrollViewer)
    {
        var contentPresenter = scrollViewer.GetVisualDescendants()
            .OfType<Avalonia.Controls.Presenters.ScrollContentPresenter>()
            .Single(control => control.Name == "PART_ContentPresenter");
        Assert.Equal(1, Grid.GetColumnSpan(contentPresenter));

        var scrollBar = scrollViewer.GetVisualDescendants()
            .OfType<ScrollBar>()
            .Single(control => control.Orientation == Avalonia.Layout.Orientation.Vertical);
        Assert.Equal(12, scrollBar.Width);
        Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(scrollBar.Background).Color.A);

        var thumbs = scrollBar.GetVisualDescendants().OfType<Thumb>().ToArray();
        if (thumbs.Length == 0)
        {
            Assert.False(scrollBar.IsVisible);
            return;
        }

        var thumb = Assert.Single(thumbs);
        Assert.Equal(scrollBar.IsExpanded ? 6 : 3, thumb.Width);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, thumb.HorizontalAlignment);
        Assert.True(thumb.CornerRadius.TopLeft >= 999);
        Assert.False(thumb.RenderTransform is ScaleTransform);
        Assert.Contains(
            thumb.Transitions!,
            transition => transition is DoubleTransition { Property.Name: "Width" });

        var thumbBorder = thumb.GetVisualDescendants().OfType<Border>().Single();
        Assert.True(thumbBorder.CornerRadius.TopLeft >= 999);
        Assert.Contains(
            thumbBorder.Transitions!,
            transition => transition is BrushTransition { Property.Name: "Background" });

        var track = scrollBar.GetVisualDescendants()
            .OfType<Avalonia.Controls.Shapes.Rectangle>()
            .Single(rectangle => rectangle.Name == "TrackRect");
        Assert.Equal(0, track.Opacity);

        var arrowButtons = scrollBar.GetVisualDescendants()
            .OfType<RepeatButton>()
            .Where(button => button.Name is "PART_LineUpButton" or "PART_LineDownButton")
            .ToArray();
        Assert.Equal(2, arrowButtons.Length);
        Assert.All(arrowButtons, button => Assert.False(button.IsVisible));
    }

    private sealed class CultureRestoreScope : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }

    private sealed class TinyPngHandler : HttpMessageHandler
    {
        private static readonly byte[] Png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

        public static byte[] ImageBytes => Png;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Png),
                RequestMessage = request
            });
    }
}
