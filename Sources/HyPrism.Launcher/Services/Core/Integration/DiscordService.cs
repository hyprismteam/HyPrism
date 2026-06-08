using DiscordRPC;
using DiscordRPC.Logging;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.Core.Integration;

/// <summary>
/// Silent logger for Discord RPC that suppresses connection error spam.
/// Only logs critical errors to HyPrism's logger when they are not expected connection failures.
/// </summary>
internal class SilentDiscordLogger : ILogger
{
    /// <summary>
    /// Gets or sets the minimum log level. Defaults to Error.
    /// </summary>
    public LogLevel Level { get; set; } = LogLevel.Error;

    /// <summary>
    /// Logs a trace message. Suppressed in this implementation.
    /// </summary>
    /// <param name="message">The message format string.</param>
    /// <param name="args">Format arguments.</param>
    public void Trace(string message, params object[] args) { }
    
    /// <summary>
    /// Logs an info message. Suppressed in this implementation.
    /// </summary>
    /// <param name="message">The message format string.</param>
    /// <param name="args">Format arguments.</param>
    public void Info(string message, params object[] args) { }
    
    /// <summary>
    /// Logs a warning message. Suppressed in this implementation.
    /// </summary>
    /// <param name="message">The message format string.</param>
    /// <param name="args">Format arguments.</param>
    public void Warning(string message, params object[] args) { }
    
    /// <summary>
    /// Logs an error message. Only logs non-connection errors to avoid spam when Discord is not running.
    /// </summary>
    /// <param name="message">The message format string.</param>
    /// <param name="args">Format arguments.</param>
    public void Error(string message, params object[] args)
    {
        // Only log if it's not a connection failure (those are expected when Discord is not running)
        if (!message.Contains("Failed connection") && !message.Contains("Failed to connect"))
        {
            Logger.Warning("Discord", string.Format(message, args));
        }
    }
}

/// <summary>
/// Manages Discord Rich Presence integration for displaying launcher and game status.
/// Handles connection failures gracefully when Discord is not running.
/// </summary>
public class DiscordService : IDiscordService, IDisposable
{
    private const string ApplicationId = "1464867466382540995";
    
    private DiscordRpcClient? _client;
    private bool _disposed;
    private bool _enabled;
    private DateTime _startTime;
    
    /// <summary>
    /// Defines the possible presence states for Discord Rich Presence.
    /// </summary>
    public enum PresenceState
    {
        /// <summary>User is idle in the launcher.</summary>
        Idle,
        /// <summary>Game files are being downloaded.</summary>
        Downloading,
        /// <summary>Game is being installed or extracted.</summary>
        Installing,
        /// <summary>User is playing the game.</summary>
        Playing
    }

    /// <inheritdoc/>
    public void Initialize()
    {
        if (string.IsNullOrEmpty(ApplicationId))
        {
            Logger.Info("Discord", "Discord RPC disabled (no Application ID configured)");
            _enabled = false;
            return;
        }
        
        try
        {
            _client = new DiscordRpcClient(ApplicationId);
            // Use silent logger to suppress connection error spam
            _client.Logger = new SilentDiscordLogger();
            
            // Set SkipIdenticalPresence to false to avoid Merge issues
            _client.SkipIdenticalPresence = false;
            
            _client.OnReady += (sender, e) =>
            {
                Logger.Success("Discord", $"Connected to Discord as {e.User.Username}");
                _enabled = true;
            };
            
            _client.OnError += (sender, e) =>
            {
                // Only log if Discord was previously connected
                if (_enabled)
                {
                    Logger.Warning("Discord", $"Discord RPC error: {e.Message}");
                }
                _enabled = false;
            };
            
            _client.OnConnectionFailed += (sender, e) =>
            {
                // Silently disable RPC if Discord is not running (expected behavior)
                _enabled = false;
            };
            
            _client.Initialize();
            _startTime = DateTime.UtcNow;
            
            // Set initial idle presence
            SetPresence(PresenceState.Idle);
            
            Logger.Info("Discord", "Discord RPC initialized");
        }
        catch (Exception ex)
        {
            Logger.Warning("Discord", $"Failed to initialize Discord RPC: {ex.Message}");
            _enabled = false;
        }
    }

    /// <inheritdoc/>
    public void SetPresence(PresenceState state, string? details = null, int? progress = null)
    {
        if (!_enabled || _client == null || !_client.IsInitialized) return;

        try
        {
            var presence = new RichPresence
            {
                Details = "In Launcher",
                State = "Browsing versions",
                Assets = new Assets
                {
                    LargeImageKey = "hyprism_logo",
                    LargeImageText = "HyPrism Launcher",
                    SmallImageKey = "hyprism_logo",
                    SmallImageText = "HyPrism"
                }
            };

            switch (state)
            {
                case PresenceState.Idle:
                    presence.Details = "In Launcher";
                    presence.State = "Browsing versions";
                    presence.Timestamps = new Timestamps(_startTime);
                    if (presence.Assets != null)
                    {
                        presence.Assets.SmallImageKey = "hyprism_logo";
                        presence.Assets.SmallImageText = "Idle";
                    }
                    break;

                case PresenceState.Downloading:
                    presence.Details = "Downloading Hytale";
                    presence.State = details ?? "Preparing...";
                    if (presence.Assets != null)
                    {
                        presence.Assets.SmallImageKey = "download";
                        presence.Assets.SmallImageText = "Downloading";
                    }
                    break;

                case PresenceState.Installing:
                    presence.Details = "Installing Hytale";
                    presence.State = details ?? "Extracting...";
                    if (presence.Assets != null)
                    {
                        presence.Assets.SmallImageKey = "install";
                        presence.Assets.SmallImageText = "Installing";
                    }
                    break;

                case PresenceState.Playing:
                    presence.Details = "Playing Hytale";
                    presence.State = details ?? "In Game";
                    presence.Timestamps = new Timestamps(DateTime.UtcNow);
                    if (presence.Assets != null)
                    {
                        presence.Assets.SmallImageKey = "playing";
                        presence.Assets.SmallImageText = "Playing";
                    }
                    break;
            }

            // Ensure assets are always populated to prevent null reference
            if (presence.Assets != null)
            {
                presence.Assets.LargeImageKey ??= "hyprism_logo";
                presence.Assets.LargeImageText ??= "HyPrism Launcher";
                presence.Assets.SmallImageKey ??= "hyprism_logo";
                presence.Assets.SmallImageText ??= "HyPrism";
            }

            _client.SetPresence(presence);
        }
        catch (Exception ex)
        {
            Logger.Warning("Discord", $"Failed to set presence: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void ClearPresence()
    {
        try
        {
            _client?.ClearPresence();
        }
        catch (Exception ex)
        {
            Logger.Warning("Discord", $"Failed to clear presence: {ex.Message}");
        }
    }

    /// <summary>
    /// Disposes the Discord RPC client and clears presence.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        try
        {
            _client?.ClearPresence();
            _client?.Dispose();
            Logger.Info("Discord", "Discord RPC disposed");
        }
        catch (Exception ex)
        {
            Logger.Warning("Discord", $"Error disposing Discord RPC: {ex.Message}");
        }
    }
}
