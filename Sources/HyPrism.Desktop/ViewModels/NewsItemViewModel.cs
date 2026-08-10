// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPrism.Models;
using HyPrism.Services.Core.Integration;
using HyPrism.Services.Core.Platform;

namespace HyPrism.Desktop.ViewModels;

public sealed partial class NewsItemViewModel : ObservableObject, IDisposable
{
    private readonly IBrowserService _browserService;
    private readonly Func<NewsItemViewModel, Task>? _openArticle;
    private readonly string? _publishedAt;
    private readonly string? _date;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    [NotifyPropertyChangedFor(nameof(HasNoImage))]
    private Bitmap? _image;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Date))]
    private string _displayDate = string.Empty;

    public NewsItemViewModel(
        NewsItemResponse item,
        IBrowserService browserService,
        Func<NewsItemViewModel, Task>? openArticle = null)
    {
        _browserService = browserService;
        _openArticle = openArticle;
        Title = item.Title;
        Excerpt = NewsService.CleanNewsExcerpt(item.Excerpt, item.Title);
        Url = item.Url;
        ImageUrl = item.ImageUrl;
        Author = item.Author;
        Source = item.Source ?? "hytale";
        SourceLabel = string.Equals(Source, "hyprism", StringComparison.OrdinalIgnoreCase)
            ? "HyPrism"
            : "Hytale";
        _publishedAt = item.PublishedAt;
        _date = item.Date;
        RefreshCulture();
    }

    public string Title { get; }
    public string Excerpt { get; }
    public string Url { get; }
    public string? ImageUrl { get; }
    public string Author { get; }
    public string Source { get; }
    public string SourceLabel { get; }
    public string Date => DisplayDate;
    public bool HasExcerpt => !string.IsNullOrWhiteSpace(Excerpt);
    public bool HasAuthor => !string.IsNullOrWhiteSpace(Author);
    public bool HasImage => Image is not null;
    public bool HasNoImage => Image is null;

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (_openArticle is not null)
        {
            await _openArticle(this);
        }
        else if (!string.IsNullOrWhiteSpace(Url))
        {
            _browserService.OpenURL(Url);
        }
    }

    [RelayCommand]
    private void OpenExternal()
    {
        if (!string.IsNullOrWhiteSpace(Url))
            _browserService.OpenURL(Url);
    }

    public async Task LoadImageAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        if (Image is not null)
            return;

        try
        {
            var bitmap = await RemoteNewsBitmap.LoadAsync(
                    ImageUrl,
                    1200,
                    httpClient,
                    cancellationToken)
                .ConfigureAwait(false);
            if (bitmap is null)
                return;

            if (cancellationToken.IsCancellationRequested)
            {
                bitmap.Dispose();
                return;
            }
            await Dispatcher.UIThread.InvokeAsync(() => Image = bitmap);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // A missing cover must not make the article itself unavailable.
        }
    }

    partial void OnImageChanging(Bitmap? oldValue, Bitmap? newValue)
        => oldValue?.Dispose();

    public void Dispose()
        => Image = null;

    public void RefreshCulture()
        => DisplayDate = FormatDate(_publishedAt, _date);

    internal static string FormatDate(string? publishedAt, string? date)
    {
        var value = string.IsNullOrWhiteSpace(publishedAt) ? date : publishedAt;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.CurrentCulture)
            : value ?? string.Empty;
    }
}
