// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Media.Imaging;
using HyPrism.Desktop.Platform;

namespace HyPrism.Desktop.Controls;

internal static class RemoteBitmapLoader
{
    public static async Task<Bitmap?> LoadAsync(
        string? url,
        int decodeWidth,
        HttpClient httpClient,
        CancellationToken cancellationToken,
        RemoteImageCache? imageCache = null,
        string imageCacheCategory = "news")
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var imageUri) ||
            imageUri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        try
        {
            byte[] imageBytes;
            if (imageCache is not null)
            {
                imageBytes = await imageCache
                    .GetBytesAsync(imageUri.AbsoluteUri, imageCacheCategory, cancellationToken)
                    .ConfigureAwait(false) ?? [];
            }
            else
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
                imageBytes = buffer.ToArray();
            }

            if (imageBytes.Length == 0)
                return null;

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
            return null;
        }
    }
}
