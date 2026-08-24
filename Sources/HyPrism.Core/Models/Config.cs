// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;

namespace HyPrism.Core.Models;

public class Config
{
    /// <summary>Launcher config schema version string.</summary>
    public string Version { get; set; } = "2.0.0";
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
    /// [DEPRECATED] Instance cache moved to Instances/instances.json.
    /// Kept for reading old configs during migration only.
    /// </summary>
    [Obsolete("Instance cache is now stored in Instances/Instances.json")]
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

    /// <summary>Custom root directory for game instances. Empty string means the default OS-specific path is used.</summary>
    public string InstanceDirectory { get; set; } = "";
    /// <summary>Whether the launcher background music is enabled.</summary>
    public bool MusicEnabled { get; set; } = true;

    /// <summary>
    /// If true, the launcher will close after successfully launching the game.
    /// </summary>
    public bool CloseAfterLaunch { get; set; } = false;

    /// <summary>
    /// If true, Discord announcements will be shown in the launcher.
    /// </summary>
    public bool ShowDiscordAnnouncements { get; set; } = true;

    /// <summary>
    /// List of Discord announcement IDs that have been dismissed by the user.
    /// </summary>
    public List<string> DismissedAnnouncementIds { get; set; } = [];

    /// <summary>
    /// If true, news will not be fetched or displayed.
    /// </summary>
    public bool DisableNews { get; set; } = false;

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
    /// If true, local profiles request a session from the configured authentication service.
    /// If false, local profiles use an ephemeral on-device OmniAuth session.
    /// Official profiles always use official Hytale authentication.
    /// </summary>
    public bool OnlineMode { get; set; } = true;

    /// <summary>
    /// Authentication service domain used by connected local profiles (e.g., "sessions.sanasol.ws").
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
