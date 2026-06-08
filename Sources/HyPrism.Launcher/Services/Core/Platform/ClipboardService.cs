using ElectronNET.API;

namespace HyPrism.Services.Core.Platform;

/// <summary>
/// Clipboard service implementation using Electron's clipboard API.
/// Provides async methods for reading and writing text to the system clipboard.
/// </summary>
public class ClipboardService : IClipboardService
{
    /// <inheritdoc/>
    public Task SetTextAsync(string text)
    {
        Electron.Clipboard.WriteText(text);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<string?> GetTextAsync()
    {
        return await Electron.Clipboard.ReadTextAsync();
    }
}
