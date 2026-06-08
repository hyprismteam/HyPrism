namespace HyPrism.Services.Core.Platform;

/// <summary>
/// Abstracts clipboard operations away from ViewModels to maintain MVVM pattern.
/// Provides async methods for reading and writing text to the system clipboard.
/// </summary>
public interface IClipboardService
{
    /// <summary>
    /// Sets the specified text to the system clipboard asynchronously.
    /// </summary>
    /// <param name="text">The text content to copy to the clipboard.</param>
    /// <returns>A task representing the asynchronous clipboard operation.</returns>
    Task SetTextAsync(string text);
    
    /// <summary>
    /// Retrieves text content from the system clipboard asynchronously.
    /// </summary>
    /// <returns>The text content from clipboard, or <c>null</c> if clipboard is empty or contains non-text data.</returns>
    Task<string?> GetTextAsync();
}
