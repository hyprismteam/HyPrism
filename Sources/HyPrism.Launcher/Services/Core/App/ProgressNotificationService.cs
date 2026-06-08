using HyPrism.Models;
using HyPrism.Services.Core.Integration;

namespace HyPrism.Services.Core.App;

/// <summary>
/// Manages progress notifications for download, installation, and game state changes.
/// Coordinates with Discord Rich Presence to reflect current activity.
/// </summary>
public class ProgressNotificationService : IProgressNotificationService
{
    private readonly IDiscordService _discordService;
    
    /// <inheritdoc/>
    public event Action<ProgressUpdateMessage>? DownloadProgressChanged;
    
    /// <inheritdoc/>
    public event Action<string, int>? GameStateChanged;
    
    /// <inheritdoc/>
    public event Action<string, string, string?>? ErrorOccurred;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="ProgressNotificationService"/> class.
    /// </summary>
    /// <param name="discordService">The Discord service for Rich Presence updates.</param>
    public ProgressNotificationService(IDiscordService discordService)
    {
        _discordService = discordService;
    }
    
    /// <inheritdoc/>
    public void SendProgress(string stage, int progress, string messageKey, object[]? args, long downloaded, long total)
    {
        var msg = new ProgressUpdateMessage 
        { 
            State = stage, 
            Progress = progress, 
            MessageKey = messageKey, 
            Args = args,
            DownloadedBytes = downloaded,
            TotalBytes = total
        };
        
        DownloadProgressChanged?.Invoke(msg);
        
        // Don't update Discord during download/install to avoid showing extraction messages
        // Only update on complete or idle
        if (stage == "complete")
        {
            _discordService.SetPresence(DiscordService.PresenceState.Idle);
        }
    }

    /// <inheritdoc/>
    public void ReportDownloadProgress(string stage, int progress, string messageKey, object[]? args = null, long downloaded = 0, long total = 0) 
        => SendProgress(stage, progress, messageKey, args, downloaded, total);
    /// Sends game state change notification.
    /// </summary>
    public void SendGameStateEvent(string state, int? exitCode = null)
    {
        switch (state)
        {
            case "starting":
                GameStateChanged?.Invoke(state, 0);
                break;
            case "started":
                GameStateChanged?.Invoke(state, 0);
                _discordService.SetPresence(DiscordService.PresenceState.Playing);
                break;
            case "running":
                GameStateChanged?.Invoke(state, 0);
                _discordService.SetPresence(DiscordService.PresenceState.Playing);
                break;
            case "stopped":
                GameStateChanged?.Invoke(state, exitCode ?? 0);
                _discordService.SetPresence(DiscordService.PresenceState.Idle);
                break;
        }
    }

    public void ReportGameStateChanged(string state, int? exitCode = null) => SendGameStateEvent(state, exitCode);

    public void SendErrorEvent(string type, string message, string? technical = null)
    {
        ErrorOccurred?.Invoke(type, message, technical);
    }
    
    public void ReportError(string type, string message, string? technical = null) 
        => SendErrorEvent(type, message, technical);
}
