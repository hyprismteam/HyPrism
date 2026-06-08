namespace HyPrism.Services.Core.App;

/// <summary>
/// Provides high-level access to all launcher settings with change notifications.
/// Abstracts configuration persistence and validation from the UI layer.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Raised when the background image or mode changes.
    /// </summary>
    event Action<string?>? OnBackgroundChanged;
    
    /// <summary>
    /// Raised when the accent color setting changes.
    /// </summary>
    event Action<string>? OnAccentColorChanged;
    
    /// <summary>
    /// Gets the current UI language code (e.g., "en-US", "ru-RU").
    /// </summary>
    /// <returns>The current language code.</returns>
    string GetLanguage();
    
    /// <summary>
    /// Sets the UI language and persists the change.
    /// </summary>
    /// <param name="language">The language code to set (e.g., "en-US").</param>
    /// <returns><c>true</c> if the language was changed; <c>false</c> if unchanged.</returns>
    bool SetLanguage(string language);
    
    /// <summary>
    /// Gets whether background music is enabled.
    /// </summary>
    /// <returns><c>true</c> if music is enabled; otherwise, <c>false</c>.</returns>
    bool GetMusicEnabled();
    
    /// <summary>
    /// Sets the background music enabled state.
    /// </summary>
    /// <param name="enabled">Whether to enable background music.</param>
    /// <returns><c>true</c> if the setting was successfully saved.</returns>
    bool SetMusicEnabled(bool enabled);
    
    /// <summary>
    /// Gets the current launcher update branch ("release" or "beta").
    /// </summary>
    /// <returns>The current launcher branch.</returns>
    string GetLauncherBranch();
    
    /// <summary>
    /// Sets the launcher update branch.
    /// </summary>
    /// <param name="branch">The branch to use ("release" or "beta").</param>
    /// <returns><c>true</c> if the setting was successfully saved.</returns>
    bool SetLauncherBranch(string branch);
    
    /// <summary>
    /// Gets the game version branch type (e.g. "release", "beta").
    /// </summary>
    string GetVersionType();

    /// <summary>
    /// Sets the game version branch type.
    /// </summary>
    bool SetVersionType(string type);

    /// <summary>
    /// Gets the selected game version number.
    /// </summary>
    int GetSelectedVersion();

    /// <summary>
    /// Sets the selected game version number.
    /// </summary>
    bool SetSelectedVersion(int version);

    /// <summary>
    /// Gets whether the launcher should close after launching the game.
    /// </summary>
    /// <returns><c>true</c> if launcher closes after launch; otherwise, <c>false</c>.</returns>
    bool GetCloseAfterLaunch();
    
    /// <summary>
    /// Sets whether the launcher should close after launching the game.
    /// </summary>
    /// <param name="close">Whether to close after launch.</param>
    /// <returns><c>true</c> if the setting was successfully saved.</returns>
    bool SetCloseAfterLaunch(bool close);

    /// <summary>
    /// Gets whether the launcher should automatically launch the game after download/install.
    /// </summary>
    bool GetLaunchAfterDownload();

    /// <summary>
    /// Sets whether the launcher should automatically launch the game after download/install.
    /// </summary>
    bool SetLaunchAfterDownload(bool enabled);
    
    /// <summary>
    /// Gets whether Discord announcement notifications are shown.
    /// </summary>
    /// <returns><c>true</c> if announcements are shown; otherwise, <c>false</c>.</returns>
    bool GetShowDiscordAnnouncements();
    
    /// <summary>
    /// Sets whether to show Discord announcement notifications.
    /// </summary>
    /// <param name="show">Whether to show announcements.</param>
    /// <returns><c>true</c> if the setting was successfully saved.</returns>
    bool SetShowDiscordAnnouncements(bool show);
    
    /// <summary>
    /// Checks if a specific announcement has been dismissed by the user.
    /// </summary>
    /// <param name="id">The unique announcement identifier.</param>
    /// <returns><c>true</c> if the announcement was dismissed; otherwise, <c>false</c>.</returns>
    bool IsAnnouncementDismissed(string id);
    
    /// <summary>
    /// Marks an announcement as dismissed so it won't be shown again.
    /// </summary>
    /// <param name="id">The unique announcement identifier to dismiss.</param>
    /// <returns><c>true</c> if the dismissal was successfully saved.</returns>
    bool DismissAnnouncement(string id);
    
    /// <summary>
    /// Gets whether the news section is disabled.
    /// </summary>
    /// <returns><c>true</c> if news is disabled; otherwise, <c>false</c>.</returns>
    bool GetDisableNews();
    
    /// <summary>
    /// Sets whether to disable the news section.
    /// </summary>
    /// <param name="disable">Whether to disable news.</param>
    /// <returns><c>true</c> if the setting was successfully saved.</returns>
    bool SetDisableNews(bool disable);
    
    /// <summary>
    /// Gets the current background mode ("default", "custom", or "none").
    /// </summary>
    /// <returns>The current background mode identifier.</returns>
    string GetBackgroundMode();
    
    /// <summary>
    /// Sets the background mode for the launcher.
    /// </summary>
    /// <param name="mode">The background mode ("default", "custom", or "none").</param>
    /// <returns><c>true</c> if the setting was successfully saved.</returns>
    bool SetBackgroundMode(string mode);
    
    /// <summary>
    /// Gets a list of available background image names from embedded resources.
    /// </summary>
    /// <returns>A list of background image identifiers.</returns>
    List<string> GetAvailableBackgrounds();
    
    /// <summary>
    /// Gets the current accent color in hexadecimal format.
    /// </summary>
    /// <returns>The accent color hex string (e.g., "#FF5500").</returns>
    string GetAccentColor();
    
    /// <summary>
    /// Sets the application accent color.
    /// </summary>
    /// <param name="color">The accent color in hexadecimal format.</param>
    /// <returns><c>true</c> if the setting was successfully saved.</returns>
    bool SetAccentColor(string color);
    
    /// <summary>
    /// Gets whether the user has completed the initial onboarding flow.
    /// </summary>
    /// <returns><c>true</c> if onboarding is complete; otherwise, <c>false</c>.</returns>
    bool GetHasCompletedOnboarding();
    
    /// <summary>
    /// Sets whether onboarding has been completed.
    /// </summary>
    /// <param name="completed">Whether onboarding is complete.</param>
    /// <returns><c>true</c> if the setting was successfully saved.</returns>
    bool SetHasCompletedOnboarding(bool completed);
    
    /// <summary>
    /// Resets the onboarding state to show the onboarding flow again.
    /// </summary>
    /// <returns><c>true</c> if the reset was successful.</returns>
    bool ResetOnboarding();
    
    /// <summary>
    /// Gets whether online/authenticated mode is enabled.
    /// </summary>
    /// <returns><c>true</c> if online mode is enabled; otherwise, <c>false</c>.</returns>
    bool GetOnlineMode();
    
    /// <summary>
    /// Sets the online/authenticated mode state.
    /// </summary>
    /// <param name="online">Whether to enable online mode.</param>
    /// <returns><c>true</c> if the setting was successfully saved.</returns>
    bool SetOnlineMode(bool online);
    
    /// <summary>
    /// Gets the authentication domain for online mode.
    /// </summary>
    /// <returns>The current authentication domain URL.</returns>
    string GetAuthDomain();

    /// <summary>
    /// Gets custom Java runtime arguments passed to Java processes during launch.
    /// </summary>
    /// <returns>The current Java arguments string.</returns>
    string GetJavaArguments();

    /// <summary>
    /// Gets whether launcher should use a custom Java executable path.
    /// </summary>
    /// <returns><c>true</c> when custom Java is enabled; otherwise, <c>false</c>.</returns>
    bool GetUseCustomJava();

    /// <summary>
    /// Gets custom Java executable path.
    /// </summary>
    /// <returns>Absolute executable path or empty string.</returns>
    string GetCustomJavaPath();
    
    /// <summary>
    /// Sets the authentication domain for online mode.
    /// </summary>
    /// <param name="domain">The authentication domain URL.</param>
    /// <returns><c>true</c> if the setting was successfully saved.</returns>
    bool SetAuthDomain(string domain);

    /// <summary>
    /// Sets custom Java runtime arguments passed to Java processes during launch.
    /// </summary>
    /// <param name="args">A whitespace-separated Java arguments string.</param>
    /// <returns><c>true</c> if the setting was successfully saved.</returns>
    bool SetJavaArguments(string args);

    /// <summary>
    /// Enables or disables custom Java runtime usage.
    /// </summary>
    /// <param name="enabled">Whether custom Java should be used.</param>
    /// <returns><c>true</c> if the setting was successfully saved.</returns>
    bool SetUseCustomJava(bool enabled);

    /// <summary>
    /// Sets custom Java executable path.
    /// </summary>
    /// <param name="path">Absolute path to java/java.exe.</param>
    /// <returns><c>true</c> if the setting was successfully saved.</returns>
    bool SetCustomJavaPath(string path);
    
    /// <summary>
    /// Gets the GPU preference for game launch ("dedicated", "integrated", or "auto").
    /// </summary>
    /// <returns>The current GPU preference string.</returns>
    string GetGpuPreference();
    
    /// <summary>
    /// Sets the GPU preference for game launch.
    /// </summary>
    /// <param name="preference">The GPU preference ("dedicated", "integrated", or "auto").</param>
    /// <returns><c>true</c> if the setting was successfully saved.</returns>
    bool SetGpuPreference(string preference);
    
    /// <summary>
    /// Gets whether the experimental DualAuth Java Agent is enabled.
    /// When false (default), uses stable legacy JAR patching.
    /// </summary>
    bool GetUseDualAuth();

    /// <summary>
    /// Sets whether the experimental DualAuth Java Agent is enabled.
    /// </summary>
    /// <param name="useDualAuth">Whether to enable DualAuth.</param>
    /// <returns><c>true</c> if the setting was successfully saved.</returns>
    bool SetUseDualAuth(bool useDualAuth);

    /// <summary>
    /// Gets the custom environment variables for game launch.
    /// </summary>
    /// <returns>Space-separated KEY=VALUE pairs.</returns>
    string GetGameEnvironmentVariables();
    
    /// <summary>
    /// Sets custom environment variables for game launch.
    /// </summary>
    /// <param name="envVars">Space-separated KEY=VALUE pairs.</param>
    /// <returns><c>true</c> if the setting was successfully saved.</returns>
    bool SetGameEnvironmentVariables(string envVars);
    
    /// <summary>
    /// Gets the current instance directory path.
    /// </summary>
    /// <returns>The absolute path to the instances directory.</returns>
    string GetInstanceDirectory();

    /// <summary>
    /// Gets whether alpha release type mods should be shown in the mod manager.
    /// </summary>
    bool GetShowAlphaMods();

    /// <summary>
    /// Sets whether alpha release type mods should be shown in the mod manager.
    /// </summary>
    bool SetShowAlphaMods(bool show);
}
