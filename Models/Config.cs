using System;
using System.Collections.Generic;

namespace HyPrism.Models;

public class Config
{
    public string Version { get; set; } = "2.0.0";
    public string UUID { get; set; } = "";
    public string Nick { get; set; } = "Hyprism";
    
    /// <summary>
    /// ID of the currently selected instance to launch.
    /// Empty string means no instance selected (will prompt to create one).
    /// </summary>
    public string SelectedInstanceId { get; set; } = "";

    /// <summary>
    /// ID of the currently active profile.
    /// Empty string means no profile selected.
    /// </summary>
    public string SelectedProfileId { get; set; } = "";

    /// <summary>
    /// [DEPRECATED] Use SelectedProfileId instead.
    /// Index-based active profile selection. Kept for backwards compatibility during migration.
    /// </summary>
    [Obsolete("Use SelectedProfileId instead")]
    public int ActiveProfileIndex { get; set; } = -1;
    
    /// <summary>
    /// [DEPRECATED] Instance cache moved to Instances/instances.json.
    /// Kept for reading old configs during migration only.
    /// </summary>
    [Obsolete("Instance cache is now stored in Instances/instances.json")]
    public List<InstanceInfo>? Instances { get; set; }
    
    /// <summary>
    /// [DEPRECATED] Use SelectedInstanceId instead.
    /// Game branch type. Kept for backwards compatibility during migration.
    /// </summary>
    [Obsolete("Use SelectedInstanceId and Instances instead")]
    public string VersionType { get; set; } = "release";
    
    /// <summary>
    /// [DEPRECATED] Use SelectedInstanceId instead.
    /// Selected version number. Kept for backwards compatibility during migration.
    /// </summary>
    [Obsolete("Use SelectedInstanceId and Instances instead")]
    public int SelectedVersion { get; set; } = 0;
    
    public string InstanceDirectory { get; set; } = "";
    public bool MusicEnabled { get; set; } = true;
    
    /// <summary>
    /// Launcher update channel: "release" for stable updates, "beta" for beta updates.
    /// Beta releases are named like "beta3-3.0.0" on GitHub.
    /// </summary>
    public string LauncherBranch { get; set; } = "release";

    /// <summary>
    /// INTERNAL: Which launcher update channel is currently installed on disk.
    /// Used to detect channel switches (release ↔ beta) and allow reinstall/downgrade
    /// when the user changes <see cref="LauncherBranch"/>.
    /// Empty means "unknown" and will be initialized at runtime.
    /// </summary>
    public string InstalledLauncherBranch { get; set; } = "";
    
    /// <summary>
    /// If true, the launcher will close after successfully launching the game.
    /// </summary>
    public bool CloseAfterLaunch { get; set; } = false;

    /// <summary>
    /// If true (default), the launcher will launch the game automatically after download/install completes.
    /// If false, the launcher will only download/install and leave the game stopped.
    /// </summary>
    public bool LaunchAfterDownload { get; set; } = true;
    
    /// <summary>
    /// If true, Discord announcements will be shown in the launcher.
    /// </summary>
    public bool ShowDiscordAnnouncements { get; set; } = true;
    
    /// <summary>
    /// List of Discord announcement IDs that have been dismissed by the user.
    /// </summary>
    public List<string> DismissedAnnouncementIds { get; set; } = new();
    
    /// <summary>
    /// If true, news will not be fetched or displayed.
    /// </summary>
    public bool DisableNews { get; set; } = false;

    /// <summary>
    /// Accent color for the UI (HEX code). Default is Hytale Orange (#FFA845).
    /// </summary>
    public string AccentColor { get; set; } = "#FFA845"; 
    
    /// <summary>
    /// Background mode: "auto" for rotating backgrounds, or a specific background filename.
    /// Changed from "slideshow" to "auto" in v2.0.4.
    /// </summary>
    public string BackgroundMode { get; set; } = "auto";
    
    /// <summary>
    /// Current interface language code (e.g., "en-US", "ru-RU", "de-DE")
    /// </summary>
    public string Language { get; set; } = "en-US";
    
    /// <summary>
    /// If true, game will run in online mode (requires authentication).
    /// If false, game runs in offline mode.
    /// </summary>
    public bool OnlineMode { get; set; } = true;
    
    /// <summary>
    /// Auth server domain for online mode (e.g., "sessions.sanasol.ws").
    /// </summary>
    public string AuthDomain { get; set; } = "sessions.sanasol.ws";

    /// <summary>
    /// Custom JVM arguments passed through JAVA_TOOL_OPTIONS for Java processes started by the game client.
    /// Example: "-Xmx4G -Dfile.encoding=UTF-8".
    /// </summary>
    public string JavaArguments { get; set; } = "";

    /// <summary>
    /// If true, launcher uses CustomJavaPath instead of bundled JRE.
    /// </summary>
    public bool UseCustomJava { get; set; } = false;

    /// <summary>
    /// Absolute path to custom Java executable (java/java.exe).
    /// </summary>
    public string CustomJavaPath { get; set; } = "";
    
    /// <summary>
    /// Last directory used for mod export. Defaults to Desktop.
    /// </summary>
    public string LastExportPath { get; set; } = "";
    
    /// <summary>
    /// If true, show alpha/beta mods in mod search results.
    /// </summary>
    public bool ShowAlphaMods { get; set; } = false;
    
    /// <summary>
    /// [DEPRECATED] Profile list moved to Profiles/profiles.json.
    /// Kept for reading old configs during migration only.
    /// </summary>
    [Obsolete("Profile list is now stored in Profiles/profiles.json")]
    public List<Profile>? Profiles { get; set; }

    /// <summary>
    /// Whether the user has completed the initial onboarding flow.
    /// </summary>
    public bool HasCompletedOnboarding { get; set; } = false;
    
    /// <summary>
    /// GPU preference for game launch: "dedicated" (default), "integrated", or "auto".
    /// On laptops with dual GPUs, this controls which GPU the game uses via environment variables.
    /// </summary>
    public string GpuPreference { get; set; } = "dedicated";
    
    /// <summary>
    /// Custom environment variables for game launch in KEY=VALUE format (one per line).
    /// These are applied to the game process and can override default variables.
    /// Example: "SDL_VIDEODRIVER=x11" or "VK_ICD_FILENAMES=/path/to/icd.json"
    /// </summary>
    public string GameEnvironmentVariables { get; set; } = "";
    
    /// <summary>
    /// If true (default), uses DualAuth Java Agent for server authentication.
    /// If false, uses legacy static JAR patching instead (opt-in via Settings).
    /// </summary>
    public bool UseDualAuth { get; set; } = true;

    /// <summary>
    /// CurseForge API key for mod manager functionality.
    /// Automatically fetched on first launch if not set.
    /// </summary>
    public string CurseForgeKey { get; set; } = "";
    
    /// <summary>
    /// [DEPRECATED] Mirror selection is now automatic at runtime and this value is ignored.
    /// Kept for reading old configs without JSON parse errors.
    /// </summary>
    [Obsolete("Mirror selection is automatic; this field is not read at runtime")]
    public string PreferredMirror { get; set; } = "";
}
