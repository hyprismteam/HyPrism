using HyPrism.Services.Core.Infrastructure;
using System.Net;

namespace HyPrism.Services.Game.Download;

/// <summary>
/// Provides file download functionality with progress tracking and resume support.
/// Used for downloading game files, patches, and other assets.
/// </summary>
public class DownloadService : IDownloadService
{
  private readonly HttpClient _httpClient;
  private const int MaxDownloadAttempts = 4;

  /// <summary>
  /// Initializes a new instance of the <see cref="DownloadService"/> class.
  /// </summary>
  /// <param name="httpClient">The HTTP client for downloading files.</param>
  public DownloadService(HttpClient httpClient)
  {
    _httpClient = httpClient;
  }

  /// <inheritdoc/>
  public async Task DownloadFileAsync(
      string url,
      string destinationPath,
      Action<int, long, long> progressCallback,
      CancellationToken cancellationToken = default)
  {
    await DownloadFileAsync(url, destinationPath, progressCallback, null, cancellationToken);
  }

  /// <inheritdoc/>
  public async Task DownloadFileAsync(
      string url,
      string destinationPath,
      Action<int, long, long> progressCallback,
      Dictionary<string, string>? headers,
      CancellationToken cancellationToken = default)
  {
    for (int attempt = 1; attempt <= MaxDownloadAttempts; attempt++)
    {
      cancellationToken.ThrowIfCancellationRequested();

      long existingLength = 0;
      if (File.Exists(destinationPath))
      {
        existingLength = new FileInfo(destinationPath).Length;
      }

      long totalBytes = await GetFileSizeAsync(url, headers, cancellationToken);
      bool canResume = existingLength > 0 && totalBytes > 0 && existingLength < totalBytes;

      if (canResume)
      {
        Logger.Info("Download", $"Resuming download from byte {existingLength} of {totalBytes}");
      }
      else if (existingLength >= totalBytes && totalBytes > 0)
      {
        Logger.Info("Download", "File already downloaded fully.");
        progressCallback?.Invoke(100, totalBytes, totalBytes);
        return;
      }
      else
      {
        existingLength = 0;
      }

      try
      {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(request, headers);
        if (canResume)
        {
          request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingLength, null);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (canResume && response.StatusCode != HttpStatusCode.PartialContent)
        {
          Logger.Warning("Download", "Server did not accept Range header, restarting download.");
          canResume = false;
          existingLength = 0;
        }
        else if (!response.IsSuccessStatusCode)
        {
          var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
          Logger.Error("Download", $"Download failed from {url}: HTTP {(int)response.StatusCode} {response.StatusCode}. Response: {errorBody?.Substring(0, Math.Min(500, errorBody?.Length ?? 0))}");
          throw new HttpRequestException(
              $"Download failed: HTTP {(int)response.StatusCode} {response.StatusCode}",
              null,
              response.StatusCode);
        }

        if (totalBytes <= 0)
        {
          totalBytes = response.Content.Headers.ContentLength ?? -1;
          if (canResume && totalBytes > 0)
          {
            totalBytes += existingLength;
          }
        }

        FileMode fileMode = canResume ? FileMode.Append : FileMode.Create;

        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(destinationPath, fileMode, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        long totalRead = existingLength;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
          await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
          totalRead += bytesRead;

          // Always report downloaded bytes so UI can show a running counter even when total is unknown.
          var progress = totalBytes > 0 ? (int)((totalRead * 100) / totalBytes) : 0;
          progressCallback?.Invoke(progress, totalRead, totalBytes);
        }

        await fileStream.FlushAsync(cancellationToken);

        // Defensive check for truncated payloads that ended without a clean HTTP error.
        if (totalBytes > 0 && totalRead < totalBytes)
        {
          throw new IOException($"Unexpected end of stream: downloaded {totalRead} of {totalBytes} bytes");
        }

        Logger.Info("Download", $"Download finished. {totalRead / 1024 / 1024} MB to {destinationPath}");
        return;
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex) when (attempt < MaxDownloadAttempts && IsRetryableDownloadException(ex))
      {
        var delayMs = 800 * attempt;
        Logger.Warning("Download", $"Transient download error on attempt {attempt}/{MaxDownloadAttempts}: {ex.Message}. Retrying in {delayMs}ms...");
        await Task.Delay(delayMs, cancellationToken);
      }
      catch (Exception ex)
      {
        // Log final error for diagnostics and rethrow to let caller decide how to present it.
        Logger.Error("Download", $"Download error on attempt {attempt}/{MaxDownloadAttempts}: {ex.Message}");
        throw;
      }
    }

    Logger.Error("Download", "Download failed after maximum retry attempts.");
    throw new IOException("Download failed after maximum retry attempts.");
  }

  private static bool IsRetryableDownloadException(Exception ex)
  {
    if (ex is IOException)
    {
      return true;
    }

    if (ex is HttpRequestException hre)
    {
      if (hre.StatusCode == HttpStatusCode.Forbidden)
      {
        return false;
      }

      if (hre.StatusCode == null)
      {
        return true;
      }

      var code = (int)hre.StatusCode.Value;
      return code == 408 || code == 429 || code >= 500;
    }

    return false;
  }

  /// <summary>
  /// Check file size without downloading.
  /// </summary>
  public Task<long> GetFileSizeAsync(string url, CancellationToken cancellationToken = default)
  {
    return GetFileSizeAsync(url, null, cancellationToken);
  }

  /// <summary>
  /// Check file size without downloading, with custom headers.
  /// </summary>
  public async Task<long> GetFileSizeAsync(string url, Dictionary<string, string>? headers, CancellationToken cancellationToken = default)
  {
    try
    {
      using var request = new HttpRequestMessage(HttpMethod.Head, url);
      ApplyHeaders(request, headers);
      using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

      if (!response.IsSuccessStatusCode)
      {
        return -1;
      }

      return response.Content.Headers.ContentLength ?? -1;
    }
    catch (Exception ex)
    {
      Logger.Warning("Download", $"GetFileSizeAsync failed for {url}: {ex.Message}");
      return -1;
    }
  }

  /// <summary>
  /// Check if file exists on server.
  /// </summary>
  public Task<bool> FileExistsAsync(string url, CancellationToken cancellationToken = default)
  {
    return FileExistsAsync(url, null, cancellationToken);
  }

  /// <summary>
  /// Check if file exists on server, with custom headers.
  /// </summary>
  public async Task<bool> FileExistsAsync(string url, Dictionary<string, string>? headers, CancellationToken cancellationToken = default)
  {
    try
    {
      using var request = new HttpRequestMessage(HttpMethod.Head, url);
      ApplyHeaders(request, headers);
      using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      Logger.Warning("Download", $"FileExistsAsync failed for {url}: {ex.Message}");
      return false;
    }
  }

  /// <summary>
  /// Applies custom headers to an HTTP request.
  /// </summary>
  private static void ApplyHeaders(HttpRequestMessage request, Dictionary<string, string>? headers)
  {
    if (headers == null || headers.Count == 0)
      return;

    foreach (var (name, value) in headers)
    {
      request.Headers.TryAddWithoutValidation(name, value);
    }
  }
}
