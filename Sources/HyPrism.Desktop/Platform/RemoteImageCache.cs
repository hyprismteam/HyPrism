// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using HyPrism.Core;
using HyPrism.Core.Infrastructure;

namespace HyPrism.Desktop.Platform;

/// <summary>
/// Shares remote image downloads and persists their encoded bytes between launcher runs
/// </summary>
public sealed class RemoteImageCache
{
    private static readonly TimeSpan DiskLifetime = TimeSpan.FromDays(14);

    private readonly HttpClient _httpClient;
    private readonly string? _cacheDirectory;
    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]?>>> _memoryCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates the shared remote image cache
    /// </summary>
    /// <param name="httpClient">The shared HTTP client</param>
    /// <param name="appPath">Application paths used for the persistent cache</param>
    public RemoteImageCache(HttpClient httpClient, AppPathConfiguration? appPath = null)
    {
        _httpClient = httpClient;
        _cacheDirectory = appPath is null
            ? null
            : Path.Combine(appPath.AppDir, "Cache", "RemoteImages");
    }

    /// <summary>
    /// Gets encoded image bytes from memory, disk, or the remote source
    /// </summary>
    /// <param name="url">The absolute HTTP image URL</param>
    /// <param name="category">A stable cache category such as news or github</param>
    /// <param name="cancellationToken">Cancellation for the caller's wait</param>
    /// <returns>The encoded image bytes, or <c>null</c> when the image is unavailable</returns>
    public async Task<byte[]?> GetBytesAsync(
        string? url,
        string category,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            string.IsNullOrWhiteSpace(category) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var normalizedCategory = string.Concat(category
            .ToLowerInvariant()
            .Where(character => char.IsAsciiLetterOrDigit(character) || character == '-'));
        if (normalizedCategory.Length == 0)
            return null;

        var cacheKey = $"{normalizedCategory}|{uri.AbsoluteUri}";
        var pending = _memoryCache.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<byte[]?>>(
                () => LoadCoreAsync(uri, normalizedCategory),
                LazyThreadSafetyMode.ExecutionAndPublication));
        var bytes = await pending.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            _memoryCache.TryRemove(
                new KeyValuePair<string, Lazy<Task<byte[]?>>>(cacheKey, pending));
        }

        return bytes;
    }

    private async Task<byte[]?> LoadCoreAsync(Uri uri, string category)
    {
        var cachePath = GetCachePath(uri, category);
        if (cachePath is not null && File.Exists(cachePath))
        {
            try
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) <= DiskLifetime)
                {
                    var cachedBytes = await File.ReadAllBytesAsync(cachePath).ConfigureAwait(false);
                    if (cachedBytes.Length > 0)
                        return cachedBytes;
                }
            }
            catch (Exception exception)
            {
                Logger.Warning("Images", $"Could not read cached image: {exception.Message}");
            }
        }

        try
        {
            var bytes = await _httpClient.GetByteArrayAsync(uri).ConfigureAwait(false);
            if (bytes.Length == 0)
                return null;

            if (cachePath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                var temporaryPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
                await File.WriteAllBytesAsync(temporaryPath, bytes).ConfigureAwait(false);
                File.Move(temporaryPath, cachePath, overwrite: true);
            }

            return bytes;
        }
        catch (Exception exception)
        {
            Logger.Warning("Images", $"Could not cache remote image {uri.Host}: {exception.Message}");
            return null;
        }
    }

    private string? GetCachePath(Uri uri, string category)
    {
        if (_cacheDirectory is null)
            return null;

        var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))
            .ToLowerInvariant();
        return Path.Combine(_cacheDirectory, category, $"{hash}.bin");
    }
}
