namespace HyPrism.Services.Core.Platform;

/// <summary>
/// Provides browser-related operations for opening URLs in the default system browser.
/// </summary>
public interface IBrowserService
{
    /// <summary>
    /// Opens the specified URL in the system's default web browser.
    /// </summary>
    /// <param name="url">The URL to open. Must be a valid absolute URI.</param>
    /// <returns><c>true</c> if the browser was launched successfully; otherwise, <c>false</c>.</returns>
    bool OpenURL(string url);
}
