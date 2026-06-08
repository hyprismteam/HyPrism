namespace HyPrism.Services.Game.Download;

/// <summary>
/// Provides file download functionality with progress reporting and HTTP utilities.
/// </summary>
public interface IDownloadService
{
    /// <summary>
    /// Downloads a file from a URL to a local destination with progress reporting.
    /// </summary>
    /// <param name="url">The URL of the file to download.</param>
    /// <param name="destinationPath">The local path where the file will be saved.</param>
    /// <param name="progressCallback">Callback for reporting progress (percentage, bytes downloaded, total bytes).</param>
    /// <param name="ct">Token to cancel the download.</param>
    Task DownloadFileAsync(string url, string destinationPath, Action<int, long, long> progressCallback, CancellationToken ct = default);

    /// <summary>
    /// Downloads a file from a URL to a local destination with progress reporting and custom headers.
    /// </summary>
    /// <param name="url">The URL of the file to download.</param>
    /// <param name="destinationPath">The local path where the file will be saved.</param>
    /// <param name="progressCallback">Callback for reporting progress (percentage, bytes downloaded, total bytes).</param>
    /// <param name="headers">Custom HTTP headers to include in the request.</param>
    /// <param name="ct">Token to cancel the download.</param>
    Task DownloadFileAsync(string url, string destinationPath, Action<int, long, long> progressCallback, Dictionary<string, string>? headers, CancellationToken ct = default);

    /// <summary>
    /// Gets the size of a remote file without downloading it.
    /// </summary>
    /// <param name="url">The URL of the file.</param>
    /// <param name="ct">Token to cancel the request.</param>
    /// <returns>The file size in bytes, or -1 if the size cannot be determined.</returns>
    Task<long> GetFileSizeAsync(string url, CancellationToken ct = default);

    /// <summary>
    /// Gets the size of a remote file without downloading it, with custom headers.
    /// </summary>
    /// <param name="url">The URL of the file.</param>
    /// <param name="headers">Custom HTTP headers to include in the request.</param>
    /// <param name="ct">Token to cancel the request.</param>
    /// <returns>The file size in bytes, or -1 if the size cannot be determined.</returns>
    Task<long> GetFileSizeAsync(string url, Dictionary<string, string>? headers, CancellationToken ct = default);

    /// <summary>
    /// Checks whether a file exists at the specified URL.
    /// </summary>
    /// <param name="url">The URL to check.</param>
    /// <param name="ct">Token to cancel the request.</param>
    /// <returns><c>true</c> if the file exists; otherwise, <c>false</c>.</returns>
    Task<bool> FileExistsAsync(string url, CancellationToken ct = default);

    /// <summary>
    /// Checks whether a file exists at the specified URL, with custom headers.
    /// </summary>
    /// <param name="url">The URL to check.</param>
    /// <param name="headers">Custom HTTP headers to include in the request.</param>
    /// <param name="ct">Token to cancel the request.</param>
    /// <returns><c>true</c> if the file exists; otherwise, <c>false</c>.</returns>
    Task<bool> FileExistsAsync(string url, Dictionary<string, string>? headers, CancellationToken ct = default);
}
