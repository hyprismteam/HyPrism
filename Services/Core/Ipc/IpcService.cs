using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using ElectronNET.API;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Core.App;
using HyPrism.Services.Core.Integration;
using HyPrism.Services.Core.Platform;
using HyPrism.Services.Game;
using HyPrism.Services.Game.Butler;
using HyPrism.Services.Game.Instance;
using HyPrism.Services.Game.Launch;
using HyPrism.Services.Game.Mod;
using HyPrism.Services.Game.Sources;
using HyPrism.Services.User;
using HyPrism.Services.Core.Ipc.Attributes;
using HyPrism.Services.Core.Ipc.Requests;
using HyPrism.Services.Core.Ipc.Responses;
using HyPrism.Services.Game.Version;

namespace HyPrism.Services.Core.Ipc;

/// <summary>
/// Bridges all Electron IPC channels to .NET services.
/// Registered as a singleton via DI; call <see cref="IpcServiceBase.RegisterAll"/> once at startup.
/// </summary>
/// <remarks>
/// Methods are discovered and wired automatically by <see cref="IpcServiceBase.RegisterAll"/>
/// through reflection — no manual <c>Electron.IpcMain.On</c> calls are needed here.
/// <para>Attribute conventions:</para>
/// <list type="bullet">
///   <item><c>[IpcInvoke("channel")]</c> — request/reply; return type → TypeScript response type.</item>
///   <item><c>[IpcSend("channel")]</c>   — fire-and-forget; no reply sent to renderer.</item>
///   <item><c>[IpcEvent("channel")]</c>  — C# → JS push; method accepts <c>Action&lt;T&gt; emit</c>
///         and subscribes to a C# event in its body.</item>
/// </list>
/// <para>
/// TypeScript bindings are auto-generated from method signatures by the Roslyn CLI tool in
/// <c>HyPrism.IpcGen/</c>. Re-generate: <c>dotnet build</c> (MSBuild target <c>GenerateIpcTs</c>).
/// </para>
/// </remarks>
public class IpcService(IServiceProvider services) : IpcServiceBase(services)
{
    #region Launcher Update

    /// <summary>Triggers an asynchronous check for available launcher updates.</summary>
    [IpcInvoke("hyprism:update:check")]
    public async Task<SuccessResult> CheckForUpdate()
    {
        await Services.GetRequiredService<IUpdateService>().CheckForLauncherUpdatesAsync();
        return new SuccessResult(true);
    }

    /// <summary>Downloads and installs the pending launcher update, then restarts the app.</summary>
    [IpcInvoke("hyprism:update:install", 300_000)]
    public async Task<bool> InstallUpdate()
    {
        var ok = await Services.GetRequiredService<IUpdateService>().UpdateAsync(null);
        if (ok)
            _ = Task.Run(async () =>
            {
                await Task.Delay(750);
                try { Electron.App.Exit(); } catch { Environment.Exit(0); }
            });
        return ok;
    }

    /// <summary>Subscribes the renderer to launcher-update-available push notifications.</summary>
    [IpcEvent("hyprism:update:available")]
    public void SubscribeUpdateAvailable(Action<LauncherUpdateInfo> emit)
    {
        Services.GetRequiredService<IUpdateService>().LauncherUpdateAvailable += info =>
        {
            try
            {
                var json = JsonSerializer.Serialize(info, IpcServiceBase.JsonOpts);
                var typed = JsonSerializer.Deserialize<LauncherUpdateInfo>(json, IpcServiceBase.JsonOpts);
                if (typed != null) emit(typed);
            }
            catch { /* swallow */ }
        };
    }

    /// <summary>Subscribes the renderer to launcher update download progress events.</summary>
    [IpcEvent("hyprism:update:progress")]
    public void SubscribeUpdateProgress(Action<LauncherUpdateProgress> emit)
    {
        Services.GetRequiredService<IUpdateService>().LauncherUpdateProgress += progress =>
        {
            try
            {
                var json = JsonSerializer.Serialize(progress, IpcServiceBase.JsonOpts);
                var typed = JsonSerializer.Deserialize<LauncherUpdateProgress>(json, IpcServiceBase.JsonOpts);
                if (typed != null) emit(typed);
            }
            catch { /* swallow */ }
        };
    }

    #endregion

    #region Config

    /// <summary>Returns the current application configuration snapshot needed by the renderer.</summary>
    [IpcInvoke("hyprism:config:get")]
    public AppConfig GetConfig()
    {
        var config = Services.GetRequiredService<IConfigService>().Configuration;
        return new AppConfig(config.Language, Services.GetRequiredService<AppPathConfiguration>().AppDir);
    }

    /// <summary>Flushes the current in-memory configuration to disk.</summary>
    [IpcInvoke("hyprism:config:save")]
    public SuccessResult SaveConfig()
    {
        Services.GetRequiredService<IConfigService>().SaveConfig();
        return new SuccessResult(true);
    }

    #endregion

    #region Game Session

    /// <summary>Starts the game download and/or launch sequence for the currently selected instance.</summary>
    [IpcSend("hyprism:game:launch")]
    public void LaunchGame(LaunchGameRequest? req)
        => _ = LaunchGameAsync(req);

    /// <summary>Cancels an in-progress game download.</summary>
    [IpcSend("hyprism:game:cancel")]
    public void CancelGame()
        => Services.GetRequiredService<IGameSessionService>().CancelDownload();

    /// <summary>Sends an exit signal to the running game process.</summary>
    [IpcInvoke("hyprism:game:stop")]
    public bool StopGame()
        => Services.GetRequiredService<IGameProcessService>().ExitGame();

    /// <summary>Returns a list of all locally installed game instances with validation status.</summary>
    [IpcInvoke("hyprism:game:instances")]
    public List<InstalledInstance> GetInstances()
        => Services.GetRequiredService<IInstanceService>().GetInstalledInstances();

    /// <summary>Returns whether the game process is currently running.</summary>
    [IpcInvoke("hyprism:game:isRunning")]
    public bool IsGameRunning()
        => Services.GetRequiredService<IGameProcessService>().CheckForRunningGame();

    /// <summary>Returns the list of available game version numbers for the given branch.</summary>
    [IpcInvoke("hyprism:game:versions")]
    public async Task<List<int>> GetVersions(GetVersionsRequest? req)
    {
#pragma warning disable CS0618
        var branch = Services.GetRequiredService<IConfigService>().Configuration.VersionType ?? "release";
#pragma warning restore CS0618
        if (!string.IsNullOrEmpty(req?.Branch)) branch = req.Branch;
        return await Services.GetRequiredService<IVersionService>().GetVersionListAsync(branch);
    }

    /// <summary>Returns available game versions enriched with download-source metadata (official and mirror).</summary>
    [IpcInvoke("hyprism:game:versionsWithSources")]
    public async Task<VersionListResponse> GetVersionsWithSources(GetVersionsRequest? req)
    {
#pragma warning disable CS0618
        var branch = Services.GetRequiredService<IConfigService>().Configuration.VersionType ?? "release";
#pragma warning restore CS0618
        if (!string.IsNullOrEmpty(req?.Branch)) branch = req.Branch;
        return await Services.GetRequiredService<IVersionService>().GetVersionListWithSourcesAsync(branch);
    }

    /// <summary>Subscribes the renderer to real-time download/install progress updates.</summary>
    [IpcEvent("hyprism:game:progress")]
    public void SubscribeGameProgress(Action<ProgressUpdate> emit)
    {
        Services.GetRequiredService<ProgressNotificationService>().DownloadProgressChanged += msg =>
        {
            try { emit(new ProgressUpdate(msg.State, msg.Progress, msg.MessageKey, msg.Args, msg.DownloadedBytes, msg.TotalBytes)); }
            catch { /* swallow */ }
        };
    }

    /// <summary>Subscribes the renderer to game lifecycle state change events (e.g. running, stopped).</summary>
    [IpcEvent("hyprism:game:state")]
    public void SubscribeGameState(Action<GameState> emit)
    {
        Services.GetRequiredService<ProgressNotificationService>().GameStateChanged += (state, exitCode) =>
        {
            try { emit(new GameState(state, exitCode)); } catch { /* swallow */ }
        };
    }

    /// <summary>Subscribes the renderer to game error events (download failures, launch errors).</summary>
    [IpcEvent("hyprism:game:error")]
    public void SubscribeGameError(Action<GameError> emit)
    {
        Services.GetRequiredService<ProgressNotificationService>().ErrorOccurred += (type, message, technical) =>
        {
            try { emit(new GameError(type, message, technical)); } catch { /* swallow */ }
        };
    }

    #endregion

    #region Instance Management

    /// <summary>Creates a new game instance metadata entry and returns its info record.</summary>
    [IpcInvoke("hyprism:instance:create")]
    public InstanceInfo? CreateInstance(CreateInstanceRequest req)
    {
        var meta = Services.GetRequiredService<IInstanceService>()
            .CreateInstanceMeta(req.Branch, req.Version, req.CustomName, req.IsLatest ?? false);
        return meta == null ? null : new InstanceInfo
        {
            Id = meta.Id, Name = meta.Name, Branch = meta.Branch, Version = meta.Version
        };
    }

    /// <summary>Deletes a game instance by ID, removing its directory and metadata.</summary>
    [IpcInvoke("hyprism:instance:delete")]
    public bool DeleteInstance(InstanceIdRequest req)
        => Services.GetRequiredService<IInstanceService>().DeleteGameById(req.InstanceId);

    /// <summary>Sets the specified instance as the currently selected instance to launch.</summary>
    [IpcInvoke("hyprism:instance:select")]
    public bool SelectInstance(SelectInstanceRequest req)
    {
        if (string.IsNullOrEmpty(req.Id)) return false;
        Services.GetRequiredService<IInstanceService>().SetSelectedInstance(req.Id);
        return true;
    }

    /// <summary>Returns the currently selected instance info, or null if none is selected.</summary>
    [IpcInvoke("hyprism:instance:getSelected")]
    public InstanceInfo? GetSelectedInstance()
        => Services.GetRequiredService<IInstanceService>().GetSelectedInstance();

    /// <summary>Returns a list of all known instances with lightweight info (name, branch, version, installed state).</summary>
    [IpcInvoke("hyprism:instance:list")]
    public List<InstanceInfo> ListInstances()
    {
        var svc = Services.GetRequiredService<IInstanceService>();
        svc.SyncInstancesWithConfig();
        return svc.GetCachedInstances().Select(i => new InstanceInfo
        {
            Id = i.Id,
            Name = i.Name,
            Branch = i.Branch,
            Version = i.Version,
            IsInstalled = svc.IsClientPresent(svc.GetInstancePathById(i.Id) ?? "")
        }).ToList();
    }

    /// <summary>Sets a custom display name for the specified instance.</summary>
    [IpcInvoke("hyprism:instance:rename")]
    public bool RenameInstance(RenameInstanceRequest req)
    {
        if (string.IsNullOrEmpty(req.InstanceId)) return false;
        Services.GetRequiredService<IInstanceService>().SetInstanceCustomNameById(req.InstanceId, req.CustomName);
        return true;
    }

    /// <summary>Changes the target version (or branch) of a "latest" instance.</summary>
    [IpcInvoke("hyprism:instance:changeVersion")]
    public bool ChangeVersion(ChangeVersionRequest req)
        => Services.GetRequiredService<IInstanceService>()
            .ChangeInstanceVersion(req.InstanceId, req.Branch, req.Version);

    /// <summary>Opens the instance directory in the system file manager.</summary>
    [IpcSend("hyprism:instance:openFolder")]
    public void OpenInstanceFolder(InstanceIdRequest req)
    {
        var path = Services.GetRequiredService<IInstanceService>().GetInstancePathById(req.InstanceId);
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            Services.GetRequiredService<IFileService>().OpenFolder(path);
    }

    /// <summary>Opens the instance's UserData/Mods directory in the system file manager, creating it if needed.</summary>
    [IpcSend("hyprism:instance:openModsFolder")]
    public void OpenModsFolder(InstanceIdRequest req)
    {
        var svc = Services.GetRequiredService<IInstanceService>();
        var path = svc.GetInstancePathById(req.InstanceId);
        if (string.IsNullOrEmpty(path)) return;
        var modsPath = Path.Combine(path, "UserData", "Mods");
        ModService.EnsureModsDirectory(modsPath);
        Services.GetRequiredService<IFileService>().OpenFolder(modsPath);
    }

    /// <summary>Exports the specified instance as a ZIP archive and returns the path to the archive.</summary>
    [IpcInvoke("hyprism:instance:export", 300_000)]
    public async Task<string> ExportInstance(InstanceIdRequest req)
        => await ExportInstanceAsync(req.InstanceId);

    /// <summary>Prompts the user to select an instance ZIP and imports it.</summary>
    [IpcInvoke("hyprism:instance:import", 300_000)]
    public async Task<bool> ImportInstance()
        => await ImportInstanceAsync();

    /// <summary>Returns a list of save folders found inside the instance's UserData/Saves directory.</summary>
    [IpcInvoke("hyprism:instance:saves")]
    public List<SaveInfo> GetSaves(InstanceIdRequest req)
        => GetInstanceSaves(req.InstanceId);

    /// <summary>Opens a specific save folder inside the instance in the system file manager.</summary>
    [IpcSend("hyprism:instance:openSaveFolder")]
    public void OpenSaveFolder(OpenSaveFolderRequest req)
    {
        var path = Services.GetRequiredService<IInstanceService>().GetInstancePathById(req.InstanceId);
        if (string.IsNullOrEmpty(path)) return;
        var savePath = Path.Combine(path, "UserData", "Saves", req.SaveName);
        if (Directory.Exists(savePath))
            Services.GetRequiredService<IFileService>().OpenFolder(savePath);
    }

    /// <summary>Returns a <c>file://</c> URL to the instance icon image with a cache-busting query parameter.</summary>
    [IpcInvoke("hyprism:instance:getIcon")]
    public string? GetInstanceIcon(InstanceIdRequest req)
    {
        var path = Services.GetRequiredService<IInstanceService>().GetInstancePathById(req.InstanceId);
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return null;
        var logo = Path.Combine(path, "logo.png");
        var icon = Path.Combine(path, "icon.png");
        var found = File.Exists(logo) ? logo : File.Exists(icon) ? icon : null;
        if (found == null) return null;
        var bust = File.GetLastWriteTimeUtc(found).Ticks;
        return $"file://{found.Replace("\\", "/")}?v={bust}";
    }

    /// <summary>Crops and saves a base64-encoded image as the instance icon (logo.png).</summary>
    [IpcInvoke("hyprism:instance:setIcon")]
    public async Task<bool> SetInstanceIcon(SetIconRequest req)
    {
        var path = Services.GetRequiredService<IInstanceService>().GetInstancePathById(req.InstanceId);
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return false;
        if (string.IsNullOrEmpty(req.IconBase64)) return false;

        var b64 = req.IconBase64.Contains(',')
            ? req.IconBase64[(req.IconBase64.IndexOf(',') + 1)..]
            : req.IconBase64;

        var bytes = Convert.FromBase64String(b64);
        using var ms = new MemoryStream(bytes);
        using var img = await Image.LoadAsync(ms);
        img.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new SixLabors.ImageSharp.Size(256, 256),
            Mode = ResizeMode.Crop
        }));
        await img.SaveAsPngAsync(Path.Combine(path, "logo.png"));
        return true;
    }

    #endregion

    #region News

    /// <summary>Fetches the latest 20 news items from the Hytale blog and launcher announcement feed.</summary>
    [IpcInvoke("hyprism:news:get")]
    public async Task<List<NewsItemResponse>> GetNews()
        => await Services.GetRequiredService<INewsService>().GetNewsAsync(count: 20);

    #endregion

    #region Profiles

    /// <summary>Returns the current active profile's nickname, UUID, and avatar preview.</summary>
    [IpcInvoke("hyprism:profile:get")]
    public ProfileSnapshot GetProfile()
    {
        var svc = Services.GetRequiredService<IProfileService>();
        return new ProfileSnapshot(svc.GetNick(), svc.GetUUID(), svc.GetAvatarPreview());
    }

    /// <summary>Returns a list of all saved profiles (id, name, uuid, isOfficial).</summary>
    [IpcInvoke("hyprism:profile:list")]
    public List<IpcProfile> ListProfiles()
        => Services.GetRequiredService<IProfileManagementService>().GetProfiles()
            .Select(p => new IpcProfile(p.Id, p.Name, p.UUID, p.IsOfficial))
            .ToList();

    /// <summary>Switches to the specified profile and reloads the Hytale auth session accordingly.</summary>
    [IpcInvoke("hyprism:profile:switch")]
    public SuccessResult SwitchProfile(SwitchProfileRequest req)
    {
        var ok = Services.GetRequiredService<IProfileManagementService>().SwitchProfile(req.Id);
        if (ok)
            Services.GetRequiredService<IHytaleAuthService>().ReloadSessionForCurrentProfile();
        return new SuccessResult(ok);
    }

    /// <summary>Updates the display name of the currently active profile.</summary>
    [IpcInvoke("hyprism:profile:setNick")]
    public SuccessResult SetNick(string nick)
        => new(Services.GetRequiredService<IProfileService>().SetNick(nick));

    /// <summary>Updates the UUID of the currently active profile.</summary>
    [IpcInvoke("hyprism:profile:setUuid")]
    public SuccessResult SetUuid(string uuid)
        => new(Services.GetRequiredService<IProfileService>().SetUUID(uuid));

    /// <summary>Creates a new profile with the given name and UUID, optionally marking it as an official Hytale account.</summary>
    [IpcInvoke("hyprism:profile:create")]
    public IpcProfile? CreateProfile(CreateProfileRequest req)
    {
        var mgmt = Services.GetRequiredService<IProfileManagementService>();
        var profile = mgmt.CreateProfile(req.Name, req.Uuid);
        if (profile == null) return null;
        profile.IsOfficial = req.IsOfficial ?? false;
        Services.GetRequiredService<ConfigService>().SaveConfig();
        if (profile.IsOfficial)
            Services.GetRequiredService<IHytaleAuthService>().SaveSessionToProfile(profile);
        return new IpcProfile(profile.Id, profile.Name, profile.UUID, profile.IsOfficial);
    }

    /// <summary>Deletes the profile with the specified ID.</summary>
    [IpcInvoke("hyprism:profile:delete")]
    public SuccessResult DeleteProfile(string id)
        => new(Services.GetRequiredService<IProfileManagementService>().DeleteProfile(id));

    /// <summary>Returns the zero-based index of the currently active profile in the profile list.</summary>
    [IpcInvoke("hyprism:profile:activeIndex")]
    public int GetActiveProfileIndex()
        => Services.GetRequiredService<IProfileManagementService>().GetActiveProfileIndex();

    /// <summary>Saves the current active profile state as a named profile entry.</summary>
    [IpcInvoke("hyprism:profile:save")]
    public SuccessResult SaveProfile()
        => new(Services.GetRequiredService<IProfileManagementService>().SaveCurrentAsProfile() != null);

    /// <summary>Creates a copy of the specified profile (without copying its user data).</summary>
    [IpcInvoke("hyprism:profile:duplicate")]
    public IpcProfile? DuplicateProfile(string id)
    {
        var p = Services.GetRequiredService<IProfileManagementService>().DuplicateProfileWithoutData(id);
        return p == null ? null : new IpcProfile(p.Id, p.Name, p.UUID, p.IsOfficial);
    }

    /// <summary>Opens the current profile's data directory in the system file manager.</summary>
    [IpcSend("hyprism:profile:openFolder")]
    public void OpenProfileFolder()
        => Services.GetRequiredService<IProfileManagementService>().OpenCurrentProfileFolder();

    /// <summary>Returns a base64-encoded avatar preview image for the given UUID.</summary>
    [IpcInvoke("hyprism:profile:avatarForUuid")]
    public string GetAvatarForUuid(string uuid)
        => Services.GetRequiredService<IProfileService>().GetAvatarPreviewForUUID(uuid) ?? "";

    #endregion

    #region Hytale Auth

    /// <summary>Returns the current Hytale account authentication status and linked profile info.</summary>
    [IpcInvoke("hyprism:auth:status")]
    public HytaleAuthStatus GetAuthStatus()
        => MapAuthStatus(Services.GetRequiredService<HytaleAuthService>().GetAuthStatus());

    /// <summary>Opens the Hytale OAuth login flow and returns the resulting auth status.</summary>
    [IpcInvoke("hyprism:auth:login")]
    public async Task<HytaleAuthStatus> Login()
    {
        var auth = Services.GetRequiredService<HytaleAuthService>();
        try
        {
            await auth.LoginAsync();
            return MapAuthStatus(auth.GetAuthStatus());
        }
        catch (HytaleNoProfileException)
        {
            return new HytaleAuthStatus(false, ErrorType: "no_profile", Error: "No game profiles found in this Hytale account");
        }
        catch (HytaleAuthException ex)
        {
            return new HytaleAuthStatus(false, ErrorType: ex.ErrorType, Error: ex.Message);
        }
        catch (Exception ex)
        {
            return new HytaleAuthStatus(false, ErrorType: "unknown", Error: ex.Message);
        }
    }

    /// <summary>Revokes the current Hytale session token and clears stored credentials.</summary>
    [IpcInvoke("hyprism:auth:logout")]
    public SuccessResult Logout()
    {
        Services.GetRequiredService<HytaleAuthService>().Logout();
        return new SuccessResult(true);
    }

    #endregion

    #region Settings

    /// <summary>Returns a complete settings snapshot for the renderer (all launcher settings in one call).</summary>
    [IpcInvoke("hyprism:settings:get")]
    public SettingsSnapshot GetSettings()
    {
        var s = Services.GetRequiredService<ISettingsService>();
        var appPath = Services.GetRequiredService<AppPathConfiguration>();
        return new SettingsSnapshot(
            Language: s.GetLanguage(),
            MusicEnabled: s.GetMusicEnabled(),
            LauncherBranch: s.GetLauncherBranch(),
            VersionType: s.GetVersionType(),
            SelectedVersion: s.GetSelectedVersion(),
            CloseAfterLaunch: s.GetCloseAfterLaunch(),
            LaunchAfterDownload: s.GetLaunchAfterDownload(),
            ShowDiscordAnnouncements: s.GetShowDiscordAnnouncements(),
            DisableNews: s.GetDisableNews(),
            BackgroundMode: s.GetBackgroundMode(),
            AvailableBackgrounds: s.GetAvailableBackgrounds(),
            AccentColor: s.GetAccentColor(),
            HasCompletedOnboarding: s.GetHasCompletedOnboarding(),
            OnlineMode: s.GetOnlineMode(),
            AuthDomain: s.GetAuthDomain(),
            DataDirectory: appPath.AppDir,
            InstanceDirectory: s.GetInstanceDirectory(),
            ShowAlphaMods: s.GetShowAlphaMods(),
            LauncherVersion: UpdateService.GetCurrentVersion(),
            JavaArguments: s.GetJavaArguments(),
            UseCustomJava: s.GetUseCustomJava(),
            CustomJavaPath: s.GetCustomJavaPath(),
            SystemMemoryMb: SystemInfoService.GetSystemMemoryMb(),
            GpuPreference: s.GetGpuPreference(),
            GameEnvironmentVariables: s.GetGameEnvironmentVariables(),
            UseDualAuth: s.GetUseDualAuth());
    }

    /// <summary>Applies a partial settings update from the renderer and saves to disk; triggers update check if branch changed.</summary>
    [IpcInvoke("hyprism:settings:update")]
    public SuccessResult UpdateSettings(UpdateSettingsRequest req)
    {
        var s = Services.GetRequiredService<ISettingsService>();
        var oldBranch = s.GetLauncherBranch();
        foreach (var (key, value) in req.Updates ?? new Dictionary<string, JsonElement>())
            ApplySetting(s, key, value);
        if (!string.Equals(oldBranch, s.GetLauncherBranch(), StringComparison.OrdinalIgnoreCase))
            _ = Task.Run(async () =>
            {
                try { await Services.GetRequiredService<IUpdateService>().CheckForLauncherUpdatesAsync(); }
                catch (Exception ex) { Logger.Warning("Update", $"Update check after channel switch failed: {ex.Message}"); }
            });
        return new SuccessResult(true);
    }

    /// <summary>Runs a speed test for the specified community mirror and returns the result.</summary>
    [IpcInvoke("hyprism:settings:testMirrorSpeed")]
    public async Task<MirrorSpeedTestResult> TestMirrorSpeed(TestMirrorSpeedRequest req)
        => await Services.GetRequiredService<IVersionService>()
            .TestMirrorSpeedAsync(req.MirrorId, req.ForceRefresh ?? false);

    /// <summary>Runs a speed test for the official Hytale download servers and returns the result.</summary>
    [IpcInvoke("hyprism:settings:testOfficialSpeed")]
    public async Task<MirrorSpeedTestResult> TestOfficialSpeed(TestOfficialSpeedRequest? req)
        => await Services.GetRequiredService<IVersionService>()
            .TestOfficialSpeedAsync(req?.ForceRefresh ?? false);

    /// <summary>Returns a summary of available download sources (official account state, enabled mirror count).</summary>
    [IpcInvoke("hyprism:settings:hasDownloadSources")]
    public DownloadSourcesSummary GetDownloadSources()
    {
        var vs = Services.GetRequiredService<IVersionService>();
        return new DownloadSourcesSummary(vs.HasDownloadSources(), vs.HasOfficialAccount, vs.EnabledMirrorCount);
    }

    /// <summary>Returns the list of all configured mirrors with their metadata and hostname.</summary>
    [IpcInvoke("hyprism:settings:getMirrors")]
    public List<MirrorInfo> GetMirrors()
    {
        var appPath = Services.GetRequiredService<AppPathConfiguration>();
        return MirrorLoaderService.GetAllMirrorMetas(appPath.AppDir)
            .Select(m => new MirrorInfo(m.Id, m.Name, m.Priority, m.Enabled, m.SourceType, GetMirrorHostname(m), m.Description))
            .ToList();
    }

    /// <summary>Discovers a mirror from a URL, saves it to disk, and reloads mirror sources.</summary>
    [IpcInvoke("hyprism:settings:addMirror", 0)]
    public async Task<AddMirrorResult> AddMirror(AddMirrorRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Url))
            return new AddMirrorResult(false, "URL is required");

        var appPath = Services.GetRequiredService<AppPathConfiguration>();
        var parsedHeaders = !string.IsNullOrWhiteSpace(req.Headers)
            ? ParseHeadersString(req.Headers)
            : null;
        var httpClient = Services.GetRequiredService<HttpClient>();
        var discovery = new MirrorDiscoveryService(httpClient);
        var result = await discovery.DiscoverMirrorAsync(req.Url, parsedHeaders);

        if (!result.Success || result.Mirror == null)
            return new AddMirrorResult(false, result.Error ?? "Discovery failed");

        if (parsedHeaders?.Count > 0)
            result.Mirror.Headers = parsedHeaders;

        if (MirrorLoaderService.MirrorExists(appPath.AppDir, result.Mirror.Id))
        {
            var baseId = result.Mirror.Id;
            var counter = 2;
            while (MirrorLoaderService.MirrorExists(appPath.AppDir, $"{baseId}-{counter}")) counter++;
            result.Mirror.Id = $"{baseId}-{counter}";
        }

        MirrorLoaderService.SaveMirror(appPath.AppDir, result.Mirror);
        Services.GetRequiredService<IVersionService>().ReloadMirrorSources();

        var mirrorDto = new MirrorInfo(result.Mirror.Id, result.Mirror.Name, result.Mirror.Priority,
            result.Mirror.Enabled, result.Mirror.SourceType, GetMirrorHostname(result.Mirror), result.Mirror.Description);
        return new AddMirrorResult(true, Mirror: mirrorDto);
    }

    /// <summary>Deletes the mirror with the given ID from disk and reloads mirror sources.</summary>
    [IpcInvoke("hyprism:settings:deleteMirror")]
    public SuccessResult DeleteMirror(MirrorIdRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.MirrorId))
            return new SuccessResult(false, "Mirror ID is required");
        var appPath = Services.GetRequiredService<AppPathConfiguration>();
        var deleted = MirrorLoaderService.DeleteMirror(appPath.AppDir, req.MirrorId);
        if (deleted) Services.GetRequiredService<IVersionService>().ReloadMirrorSources();
        return new SuccessResult(deleted);
    }

    /// <summary>Enables or disables the specified mirror and persists the change to disk.</summary>
    [IpcInvoke("hyprism:settings:toggleMirror")]
    public SuccessResult ToggleMirror(ToggleMirrorRequest req)
    {
        var appPath = Services.GetRequiredService<AppPathConfiguration>();
        var mirrors = MirrorLoaderService.GetAllMirrorMetas(appPath.AppDir);
        var mirror = mirrors.FirstOrDefault(m => m.Id == req.MirrorId);
        if (mirror == null) return new SuccessResult(false, "Mirror not found");
        mirror.Enabled = req.Enabled;
        MirrorLoaderService.SaveMirror(appPath.AppDir, mirror);
        Services.GetRequiredService<IVersionService>().ReloadMirrorSources();
        return new SuccessResult(true);
    }

    /// <summary>Returns the absolute path to the launcher data directory.</summary>
    [IpcInvoke("hyprism:settings:launcherPath")]
    public string GetLauncherPath()
        => Services.GetRequiredService<AppPathConfiguration>().AppDir;

    /// <summary>Returns the default path for game instances (AppDir/Instances).</summary>
    [IpcInvoke("hyprism:settings:defaultInstanceDir")]
    public string GetDefaultInstanceDir()
        => Path.Combine(Services.GetRequiredService<AppPathConfiguration>().AppDir, "Instances");

    /// <summary>Changes the root directory for game instances, migrating existing data if needed.</summary>
    [IpcInvoke("hyprism:settings:setInstanceDir", 300_000)]
    public async Task<SetInstanceDirResult> SetInstanceDir(string path)
        => await SetInstanceDirAsync(path);

    #endregion

    #region Network

    /// <summary>Pings the configured auth server and returns latency and reachability info.</summary>
    [IpcInvoke("hyprism:network:pingAuthServer")]
    public async Task<AuthServerPingResult> PingAuthServer(PingAuthServerRequest? req)
        => await PingAuthServerAsync(req?.AuthDomain);

    #endregion

    #region Localisation

    /// <summary>Returns the BCP 47 language code currently active in the launcher.</summary>
    [IpcInvoke("hyprism:i18n:current")]
    public string GetCurrentLanguage()
        => Services.GetRequiredService<LocalizationService>().CurrentLanguage;

    /// <summary>Sets the launcher UI language and persists the setting; returns the resolved language code.</summary>
    [IpcInvoke("hyprism:i18n:set")]
    public SetLanguageResult SetLanguage(string lang)
    {
        if (string.IsNullOrEmpty(lang)) lang = "en-US";
        var s = Services.GetRequiredService<ISettingsService>();
        var l = Services.GetRequiredService<LocalizationService>();
        var ok = s.SetLanguage(lang);
        return new SetLanguageResult(ok, ok ? lang : l.CurrentLanguage);
    }

    /// <summary>Returns the list of all supported UI languages with their display names.</summary>
    [IpcInvoke("hyprism:i18n:languages")]
    public List<LanguageInfo> GetLanguages()
        => LocalizationService.GetAvailableLanguages()
            .Select(l => new LanguageInfo(l.Key, l.Value))
            .ToList();

    #endregion

    #region Window Controls

    /// <summary>Minimizes the main browser window to the taskbar.</summary>
    [IpcSend("hyprism:window:minimize")]
    public void MinimizeWindow()
        => Electron.WindowManager.BrowserWindows.FirstOrDefault()?.Minimize();

    /// <summary>Maximizes or restores (un-maximizes) the main browser window.</summary>
    [IpcSend("hyprism:window:maximize")]
    public async void MaximizeWindow()
    {
        var win = Electron.WindowManager.BrowserWindows.FirstOrDefault();
        if (win == null) return;
        if (await win.IsMaximizedAsync()) win.Unmaximize();
        else win.Maximize();
    }

    /// <summary>Closes the main browser window.</summary>
    [IpcSend("hyprism:window:close")]
    public void CloseWindow()
        => Electron.WindowManager.BrowserWindows.FirstOrDefault()?.Close();

    /// <summary>Exits the Electron process, causing the OS to restart the app if configured to do so.</summary>
    [IpcSend("hyprism:window:restart")]
    public void RestartApp()
    {
        try { Electron.App.Exit(); }
        catch { Electron.WindowManager.BrowserWindows.FirstOrDefault()?.Close(); }
    }

    /// <summary>Opens an external URL in the system default browser.</summary>
    [IpcSend("hyprism:browser:open")]
    public void OpenBrowser(string url)
    {
        if (!string.IsNullOrEmpty(url))
            Electron.Shell.OpenExternalAsync(url);
    }

    #endregion

    #region Mods

    /// <summary>Returns installed mods for the currently selected instance.</summary>
    [IpcInvoke("hyprism:mods:list")]
    public List<InstalledMod> ListMods()
    {
        var path = ResolveModInstancePath(null);
        return string.IsNullOrEmpty(path)
            ? []
            : Services.GetRequiredService<IModService>().GetInstanceInstalledMods(path);
    }

    /// <summary>Searches CurseForge for mods matching the query, with pagination and filtering.</summary>
    [IpcInvoke("hyprism:mods:search", 30_000)]
    public async Task<ModSearchResult> SearchMods(ModSearchRequest req)
        => await Services.GetRequiredService<IModService>().SearchModsAsync(
            req.Query, req.Page, req.PageSize,
            req.Categories.ToArray(), req.SortField, req.SortOrder);

    /// <summary>Returns installed mods for the specified instance.</summary>
    [IpcInvoke("hyprism:mods:installed")]
    public List<InstalledMod> GetInstalledMods(ModInstalledRequest req)
    {
        var path = ResolveModInstancePath(req.InstanceId);
        return string.IsNullOrEmpty(path)
            ? []
            : Services.GetRequiredService<IModService>().GetInstanceInstalledMods(path);
    }

    /// <summary>Removes the specified mod from the instance's Mods directory.</summary>
    [IpcInvoke("hyprism:mods:uninstall")]
    public async Task<bool> UninstallMod(ModUninstallRequest req)
        => await UninstallModAsync(req.ModId, req.InstanceId);

    /// <summary>Checks CurseForge for updates to all installed mods in the specified instance and returns updated entries.</summary>
    [IpcInvoke("hyprism:mods:checkUpdates", 30_000)]
    public async Task<List<InstalledMod>> CheckModUpdates(ModCheckUpdatesRequest req)
    {
        var path = ResolveModInstancePath(req.InstanceId);
        return string.IsNullOrEmpty(path)
            ? []
            : await Services.GetRequiredService<IModService>().CheckInstanceModUpdatesAsync(path);
    }

    /// <summary>Downloads and installs a specific CurseForge mod file into the instance's Mods directory.</summary>
    [IpcInvoke("hyprism:mods:install", 300_000)]
    public async Task<bool> InstallMod(ModInstallRequest req)
    {
        var path = ResolveModInstancePath(req.InstanceId);
        return !string.IsNullOrEmpty(path) &&
               await Services.GetRequiredService<IModService>()
                   .InstallModFileToInstanceAsync(req.ModId, req.FileId, path);
    }

    /// <summary>Returns a paged list of files available for the specified CurseForge mod.</summary>
    [IpcInvoke("hyprism:mods:files")]
    public async Task<ModFilesResult> GetModFiles(ModFilesRequest req)
        => await Services.GetRequiredService<IModService>()
            .GetModFilesAsync(req.ModId, req.Page ?? 0, req.PageSize ?? 20);

    /// <summary>Returns detailed information about the specified CurseForge mod.</summary>
    [IpcInvoke("hyprism:mods:info", 30_000)]
    public async Task<ModInfo?> GetModInfo(ModInfoRequest req)
        => await Services.GetRequiredService<IModService>().GetModAsync(req.ModId);

    /// <summary>Returns the changelog text for the specified mod file.</summary>
    [IpcInvoke("hyprism:mods:changelog")]
    public async Task<string> GetModChangelog(ModChangelogRequest req)
        => await Services.GetRequiredService<IModService>()
               .GetModFileChangelogAsync(req.ModId, req.FileId) ?? "";

    /// <summary>Returns all available CurseForge mod categories for Hytale.</summary>
    [IpcInvoke("hyprism:mods:categories")]
    public async Task<List<ModCategory>> GetModCategories()
        => await Services.GetRequiredService<IModService>().GetModCategoriesAsync();

    /// <summary>Copies a local JAR/ZIP file into the instance's Mods directory.</summary>
    [IpcInvoke("hyprism:mods:installLocal")]
    public async Task<bool> InstallLocalMod(ModInstallLocalRequest req)
    {
        var path = ResolveModInstancePath(req.InstanceId);
        return !string.IsNullOrEmpty(path) &&
               await Services.GetRequiredService<IModService>().InstallLocalModFile(req.SourcePath, path);
    }

    /// <summary>Decodes a base64-encoded mod file and installs it into the instance's Mods directory.</summary>
    [IpcInvoke("hyprism:mods:installBase64")]
    public async Task<bool> InstallModFromBase64(ModInstallBase64Request req)
    {
        var path = ResolveModInstancePath(req.InstanceId);
        return !string.IsNullOrEmpty(path) &&
               await Services.GetRequiredService<IModService>().InstallModFromBase64(req.FileName, req.Base64Content, path);
    }

    /// <summary>Opens the instance's UserData/Mods directory in the system file manager.</summary>
    [IpcSend("hyprism:mods:openFolder")]
    public void OpenModsFolder(ModOpenFolderRequest req)
    {
        var path = ResolveModInstancePath(req.InstanceId);
        if (string.IsNullOrEmpty(path)) return;
        var modsPath = Path.Combine(path, "UserData", "Mods");
        ModService.EnsureModsDirectory(modsPath);
        Electron.Shell.OpenPathAsync(modsPath);
    }

    /// <summary>Toggles a mod's enabled state by adding or removing the <c>.disabled</c> extension.</summary>
    [IpcInvoke("hyprism:mods:toggle")]
    public async Task<bool> ToggleMod(ModToggleRequest req)
        => await ToggleModAsync(req.ModId, req.InstanceId);

    /// <summary>Exports the instance's mod list to a folder as JSON or individual JARs.</summary>
    [IpcInvoke("hyprism:mods:exportToFolder")]
    public async Task<string> ExportModsToFolder(ModExportRequest req)
        => await ExportModsAsync(req.InstanceId, req.ExportPath, req.ExportType ?? "modlist");

    /// <summary>Imports a mod list JSON and installs all listed mods into the instance.</summary>
    [IpcInvoke("hyprism:mods:importList")]
    public async Task<int> ImportModList(ModImportListRequest req)
        => await ImportModListAsync(req.ListPath, null);

    #endregion

    #region System Info

    /// <summary>Returns a list of detected GPU adapters with vendor and name information.</summary>
    [IpcInvoke("hyprism:system:gpuAdapters")]
    public List<GpuAdapterInfo> GetGpuAdapters()
        => Services.GetRequiredService<GpuDetectionService>().GetAdapters();

    /// <summary>Returns the current operating system platform information.</summary>
    [IpcInvoke("hyprism:system:platform")]
    public PlatformInfo GetPlatform()
    {
        var linux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        var windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var mac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        var os = linux ? "linux" : windows ? "windows" : mac ? "macos" : "unknown";
        return new PlatformInfo(os, linux, windows, mac);
    }

    #endregion

    #region Console / Logs

    /// <summary>Forwards an informational log message from the renderer to the .NET logger.</summary>
    [IpcSend("hyprism:console:log")]
    public void ConsoleLog(string msg) => Logger.Info("Renderer", msg);

    /// <summary>Forwards a warning log message from the renderer to the .NET logger.</summary>
    [IpcSend("hyprism:console:warn")]
    public void ConsoleWarn(string msg) => Logger.Warning("Renderer", msg);

    /// <summary>Forwards an error log message from the renderer to the .NET logger.</summary>
    [IpcSend("hyprism:console:error")]
    public void ConsoleError(string msg) => Logger.Error("Renderer", msg);

    /// <summary>Returns the most recent log lines from the .NET logger ring buffer.</summary>
    [IpcInvoke("hyprism:logs:get")]
    public List<string> GetLogs(GetLogsRequest? req)
        => Logger.GetRecentLogs(req?.Count ?? 100);

    #endregion

    #region File Dialogs

    /// <summary>Opens a native folder picker dialog and returns the selected path, or an empty string if cancelled.</summary>
    [IpcInvoke("hyprism:file:browseFolder", 300_000)]
    public async Task<string> BrowseFolder(string? initialPath)
        => await Services.GetRequiredService<IFileDialogService>()
               .BrowseFolderAsync(string.IsNullOrEmpty(initialPath) ? null : initialPath) ?? "";

    /// <summary>Opens a native file picker filtered to Java executables and returns the selected path.</summary>
    [IpcInvoke("hyprism:file:browseJavaExecutable", 300_000)]
    public async Task<string> BrowseJavaExecutable()
        => await Services.GetRequiredService<IFileDialogService>().BrowseJavaExecutableAsync() ?? "";

    /// <summary>Opens a native file picker for JAR/ZIP files and returns the selected paths.</summary>
    [IpcInvoke("hyprism:file:browseModFiles")]
    public async Task<string[]> BrowseModFiles()
        => await Services.GetRequiredService<IFileDialogService>().BrowseModFilesAsync()
           ?? Array.Empty<string>();

    /// <summary>Returns whether the file at the given absolute path exists on disk.</summary>
    [IpcInvoke("hyprism:file:exists")]
    public bool FileExists(string path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    #endregion

    #region Private helpers

    private async Task LaunchGameAsync(LaunchGameRequest? req)
    {
        var gameSession    = Services.GetRequiredService<IGameSessionService>();
        var processService = Services.GetRequiredService<IGameProcessService>();
        var instanceSvc    = Services.GetRequiredService<IInstanceService>();
        var configSvc      = Services.GetRequiredService<IConfigService>();
        var progressSvc    = Services.GetRequiredService<ProgressNotificationService>();

        if (processService.IsGameRunning())
        {
            Logger.Warning("IPC", "Game launch request ignored - game already running");
            return;
        }

        var launchAfterDownload = configSvc.Configuration.LaunchAfterDownload;

        if (req != null)
        {
            if (!string.IsNullOrWhiteSpace(req.InstanceId))
                instanceSvc.SetSelectedInstance(req.InstanceId);
            if (req.LaunchAfterDownload.HasValue)
                launchAfterDownload = req.LaunchAfterDownload.Value;
        }

        Logger.Info("IPC", "Game launch requested");
        try
        {
            var result = await gameSession.DownloadAndLaunchAsync(() => launchAfterDownload);

            if (result.Cancelled || string.Equals(result.Error, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                progressSvc.ReportGameStateChanged("stopped", 0);
                return;
            }

            if (result.Success && !launchAfterDownload)
            {
                progressSvc.ReportGameStateChanged("stopped", 0);
                return;
            }

            if (!result.Success)
            {
                progressSvc.ReportError("download", "Failed to install game", result.Error ?? "Unknown error");
                progressSvc.ReportGameStateChanged("stopped", 1);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("IPC", $"Game launch failed: {ex.Message}");
            progressSvc.ReportError("download", "Failed to install game", ex.ToString());
            progressSvc.ReportGameStateChanged("stopped", 1);
        }
    }

    private List<SaveInfo> GetInstanceSaves(string instanceId)
    {
        var svc = Services.GetRequiredService<IInstanceService>();
        var path = svc.GetInstancePathById(instanceId);
        if (string.IsNullOrEmpty(path)) return [];

        var savesPath = Path.Combine(path, "UserData", "Saves");
        if (!Directory.Exists(savesPath)) return [];

        return Directory.GetDirectories(savesPath).Select(saveDir =>
        {
            var di = new DirectoryInfo(saveDir);
            var preview = Path.Combine(saveDir, "preview.png");
            long size = 0;
            try { size = di.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); } catch { /* ignore */ }
            return new SaveInfo(
                di.Name,
                File.Exists(preview) ? $"file://{preview.Replace("\\", "/")}" : null,
                di.LastWriteTime.ToString("o"),
                size);
        }).ToList();
    }

    private async Task<string> ExportInstanceAsync(string instanceId)
    {
        var svc = Services.GetRequiredService<IInstanceService>();
        var fileDialog = Services.GetRequiredService<IFileDialogService>();
        var path = svc.GetInstancePathById(instanceId);
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return "";

        var defaultName = $"HyPrism-{instanceId[..Math.Min(8, instanceId.Length)]}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var savePath = await fileDialog.SaveFileAsync(defaultName, "Zip files|*.zip", desktop);

        if (string.IsNullOrEmpty(savePath)) return "";
        if (!savePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) savePath += ".zip";

        if (File.Exists(savePath)) File.Delete(savePath);
        ZipFile.CreateFromDirectory(path, savePath, CompressionLevel.Optimal, false);
        Logger.Success("IPC", $"Exported instance to: {savePath}");
        return savePath;
    }

    private async Task<bool> ImportInstanceAsync()
    {
        var svc = Services.GetRequiredService<IInstanceService>();
        var fileDialog = Services.GetRequiredService<IFileDialogService>();
        var filePath = await fileDialog.BrowseInstanceArchiveAsync();
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return false;

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".pwr")
            await ImportPwrFileAsync(filePath, svc);
        else
            await svc.ImportFromZipAsync(filePath);

        Logger.Success("IPC", "Instance imported successfully");
        return true;
    }

    private static HytaleAuthStatus MapAuthStatus(dynamic status)
    {
        // HytaleAuthService.GetAuthStatus() returns an anonymous/dynamic object
        // We cast it explicitly via JSON round-trip for safety
        var json = JsonSerializer.Serialize(status, IpcServiceBase.JsonOpts);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var loggedIn = root.TryGetProperty("loggedIn", out JsonElement li) && li.GetBoolean();
        var username  = root.TryGetProperty("username",  out JsonElement un)  ? un.GetString()  : null;
        var uuid      = root.TryGetProperty("uuid",      out JsonElement uid) ? uid.GetString() : null;
        var error     = root.TryGetProperty("error",     out JsonElement er)  ? er.GetString()  : null;
        var errorType = root.TryGetProperty("errorType", out JsonElement et)  ? et.GetString()  : null;
        return new HytaleAuthStatus(loggedIn, username, uuid, error, errorType);
    }

    private string? ResolveModInstancePath(string? instanceId)
    {
        var svc = Services.GetRequiredService<IInstanceService>();
        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            var byId = svc.GetInstancePathById(instanceId);
            if (!string.IsNullOrWhiteSpace(byId)) return byId;
        }
        var selected = svc.GetSelectedInstance();
        return selected != null ? svc.GetInstancePathById(selected.Id) : null;
    }

    private async Task<bool> UninstallModAsync(string modId, string? instanceId)
    {
        var modSvc = Services.GetRequiredService<IModService>();
        var instancePath = ResolveModInstancePath(instanceId);
        if (string.IsNullOrEmpty(instancePath)) return false;

        var mods = modSvc.GetInstanceInstalledMods(instancePath);
        var mod = mods.FirstOrDefault(m => m.Id == modId || m.Name == modId);
        if (mod == null) return false;

        mods.Remove(mod);

        if (!string.IsNullOrEmpty(mod.FileName))
        {
            var modsDir = Path.Combine(instancePath, "UserData", "Mods");
            var deleted = TryDeleteModFile(modsDir, mod.FileName);
            if (!deleted) Logger.Warning("IPC", $"Could not find mod file to delete: {mod.FileName}");
        }

        await modSvc.SaveInstanceModsAsync(instancePath, mods);
        return true;
    }

    private static bool TryDeleteModFile(string modsDir, string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(modsDir, fileName),
            Path.Combine(modsDir, fileName + ".disabled"),
        };

        foreach (var c in candidates)
        {
            if (!File.Exists(c)) continue;
            try { File.Delete(c); return true; } catch { /* ignore */ }
        }

        // Fuzzy stem search
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (stem.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
            stem.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            stem = Path.GetFileNameWithoutExtension(stem);

        if (!Directory.Exists(modsDir)) return false;

        try
        {
            foreach (var f in Directory.GetFiles(modsDir))
            {
                var fn = Path.GetFileName(f).ToLowerInvariant();
                var s = stem.ToLowerInvariant();
                if (fn is var n && (n == $"{s}.jar" || n == $"{s}.jar.disabled" ||
                                    n == $"{s}.zip" || n == $"{s}.zip.disabled" || n == $"{s}.disabled"))
                {
                    try { File.Delete(f); return true; } catch { /* ignore */ }
                }
            }
        }
        catch { /* ignore */ }

        return false;
    }

    private async Task<bool> ToggleModAsync(string modId, string? instanceId)
    {
        var modSvc = Services.GetRequiredService<IModService>();
        var instancePath = ResolveModInstancePath(instanceId);
        if (string.IsNullOrEmpty(instancePath)) return false;

        var mods = modSvc.GetInstanceInstalledMods(instancePath);
        var mod = mods.FirstOrDefault(m => m.Id == modId || m.Name == modId);
        if (mod == null || string.IsNullOrEmpty(mod.FileName)) return false;

        var modsDir = Path.Combine(instancePath, "UserData", "Mods");
        var currentPath = Path.Combine(modsDir, mod.FileName);

        if (!File.Exists(currentPath))
        {
            // Recover from stale manifest
            var stem = Path.GetFileNameWithoutExtension(mod.FileName);
            var probes = new[]
            {
                currentPath,
                Path.Combine(modsDir, $"{stem}.jar"),
                Path.Combine(modsDir, $"{stem}.zip"),
                Path.Combine(modsDir, $"{stem}.disabled"),
                Path.Combine(modsDir, $"{stem}.jar.disabled"),
                Path.Combine(modsDir, $"{stem}.zip.disabled"),
            };
            var found = probes.FirstOrDefault(File.Exists);
            if (found == null) return false;
            currentPath = found;
            mod.FileName = Path.GetFileName(found);
        }

        if (mod.Enabled)
        {
            var fn = Path.GetFileName(currentPath);
            var ext = Path.GetExtension(fn).ToLowerInvariant();
            if (ext is ".jar" or ".zip") mod.DisabledOriginalExtension = ext;
            var disabledName = $"{Path.GetFileNameWithoutExtension(fn)}.disabled";
            var disabledPath = Path.Combine(modsDir, disabledName);
            File.Move(currentPath, disabledPath, true);
            mod.FileName = disabledName;
            mod.Enabled = false;
        }
        else
        {
            var fn = Path.GetFileName(currentPath);
            var stem = fn.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? fn[..^".disabled".Length]
                : Path.GetFileNameWithoutExtension(fn);
            var restoreExt = !string.IsNullOrWhiteSpace(mod.DisabledOriginalExtension)
                ? (mod.DisabledOriginalExtension.StartsWith('.') ? mod.DisabledOriginalExtension : $".{mod.DisabledOriginalExtension}")
                : (stem.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || stem.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "" : ".jar");
            var enabledName = string.IsNullOrEmpty(restoreExt) ? stem : $"{stem}{restoreExt}";
            File.Move(currentPath, Path.Combine(modsDir, enabledName), true);
            mod.FileName = enabledName;
            mod.Enabled = true;
            mod.DisabledOriginalExtension = "";
        }

        await modSvc.SaveInstanceModsAsync(instancePath, mods);
        return true;
    }

    private async Task<string> ExportModsAsync(string? instanceId, string exportPath, string exportType)
    {
        var svc = Services.GetRequiredService<IInstanceService>();
        var modSvc = Services.GetRequiredService<IModService>();
        var config = Services.GetRequiredService<IConfigService>();

        if (string.IsNullOrEmpty(exportPath)) return "";

        var instancePath = !string.IsNullOrWhiteSpace(instanceId)
            ? svc.GetInstancePathById(instanceId)
            : svc.GetSelectedInstance() is { } sel ? svc.GetInstancePathById(sel.Id) : null;

        if (string.IsNullOrEmpty(instancePath)) return "";

        var meta = svc.GetInstanceMeta(instancePath);
        var branch = meta?.Branch ?? "release";
        var version = meta?.Version ?? 0;
        var mods = modSvc.GetInstanceInstalledMods(instancePath);
        if (mods.Count == 0) return "";

        config.Configuration.LastExportPath = exportPath;
        config.SaveConfig();

        if (exportType == "zip")
        {
            var modsDir = Path.Combine(instancePath, "UserData", "Mods");
            if (!Directory.Exists(modsDir)) return "";
            var zipPath = Path.Combine(exportPath, $"HyPrism-Mods-{branch}-v{version}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            ZipFile.CreateFromDirectory(modsDir, zipPath);
            return zipPath;
        }

        var modList = mods
            .Where(m => !string.IsNullOrEmpty(m.CurseForgeId))
            .Select(m => new ModListEntry { CurseForgeId = m.CurseForgeId, FileId = m.FileId, Name = m.Name, Version = m.Version })
            .ToList();
        if (modList.Count == 0) return "";

        var filePath = Path.Combine(exportPath, $"HyPrism-ModList-{branch}-v{version}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(modList, new JsonSerializerOptions { WriteIndented = true }));
        return filePath;
    }

    private async Task<int> ImportModListAsync(string filePath, string? instanceId)
    {
        var svc = Services.GetRequiredService<IInstanceService>();
        var modSvc = Services.GetRequiredService<IModService>();

        if (!File.Exists(filePath)) return 0;

        var instancePath = !string.IsNullOrWhiteSpace(instanceId)
            ? svc.GetInstancePathById(instanceId)
            : svc.GetSelectedInstance() is { } sel ? svc.GetInstancePathById(sel.Id) : null;
        if (string.IsNullOrEmpty(instancePath)) return 0;

        var content = await File.ReadAllTextAsync(filePath);
        var modList = JsonSerializer.Deserialize<List<ModListEntry>>(content) ?? [];
        var count = 0;
        foreach (var entry in modList)
        {
            if (string.IsNullOrEmpty(entry.CurseForgeId)) continue;
            try
            {
                if (await modSvc.InstallModFileToInstanceAsync(entry.CurseForgeId, entry.FileId ?? "", instancePath))
                    count++;
            }
            catch (Exception ex) { Logger.Warning("IPC", $"Failed to import mod {entry.Name}: {ex.Message}"); }
        }
        return count;
    }

    private async Task<SetInstanceDirResult> SetInstanceDirAsync(string path)
    {
        var config = Services.GetRequiredService<IConfigService>();
        var instanceSvc = Services.GetRequiredService<IInstanceService>();
        var appPath = Services.GetRequiredService<AppPathConfiguration>();
        var progressSvc = Services.GetRequiredService<ProgressNotificationService>();

        Logger.Info("IPC", $"Setting instance directory to: {path}");
        var resetToDefault = string.IsNullOrWhiteSpace(path);
        var newPath = resetToDefault
            ? Path.Combine(appPath.AppDir, "Instances")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));

        if (!Path.IsPathRooted(newPath))
            newPath = Path.GetFullPath(Path.Combine(appPath.AppDir, newPath));

        var currentRoot = Path.GetFullPath(instanceSvc.GetInstanceRoot());

        if (Path.GetFullPath(currentRoot).Equals(Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
            return new SetInstanceDirResult(true, newPath, Noop: true, Reason: "already-current-path");

        if (newPath.StartsWith(currentRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return new SetInstanceDirResult(false, "", Error: "Target directory cannot be inside current instance directory");

        Directory.CreateDirectory(newPath);

        var filesToMove = new List<(string source, string dest)>();
        if (Directory.Exists(currentRoot))
            await Task.Run(() => CollectFilesRecursive(currentRoot, newPath, currentRoot, filesToMove));

        filesToMove = filesToMove
            .OrderByDescending(f => { try { return new FileInfo(f.source).Length; } catch { return 0; } })
            .ToList();

        if (filesToMove.Count == 0)
        {
            if (resetToDefault) { config.Configuration.InstanceDirectory = string.Empty; config.SaveConfig(); }
            else await config.SetInstanceDirectoryAsync(newPath);
            return new SetInstanceDirResult(true, newPath);
        }

        long totalSize = filesToMove.Sum(f => { try { return new FileInfo(f.source).Length; } catch { return 0; } });
        long movedSize = 0;
        var movedCount = 0;

        progressSvc.SendProgress("moving-instances", 0, "settings.dataSettings.movingData", null, 0, totalSize);

        foreach (var (source, dest) in filesToMove)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                var prePct = totalSize > 0 ? (int)Math.Clamp(movedSize * 100 / totalSize, 0, 99) : movedCount * 100 / filesToMove.Count;
                progressSvc.SendProgress("moving-instances", prePct, "settings.dataSettings.movingDataHint",
                    new object[] { Path.GetFileName(source) }, movedSize, totalSize);
                File.Copy(source, dest, true);
                movedSize += new FileInfo(dest).Length;
                movedCount++;
                var pct = totalSize > 0 ? (int)(movedSize * 100 / totalSize) : movedCount * 100 / filesToMove.Count;
                progressSvc.SendProgress("moving-instances", pct, "settings.dataSettings.movingDataHint",
                    new object[] { Path.GetFileName(source) }, movedSize, totalSize);
            }
            catch (Exception ex) { Logger.Warning("IPC", $"Failed to copy {source}: {ex.Message}"); }
        }

        bool ok;
        if (resetToDefault) { config.Configuration.InstanceDirectory = string.Empty; config.SaveConfig(); ok = true; }
        else { ok = await config.SetInstanceDirectoryAsync(newPath) != null; }

        if (ok)
        {
            try
            {
                if (!currentRoot.Equals(Path.Combine(appPath.AppDir, "Instances"), StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(currentRoot, true);
                else
                {
                    foreach (var d in Directory.GetDirectories(currentRoot)) Directory.Delete(d, true);
                    foreach (var f in Directory.GetFiles(currentRoot)) File.Delete(f);
                }
            }
            catch (Exception ex) { Logger.Warning("IPC", $"Cleanup failed: {ex.Message}"); }
        }

        progressSvc.SendProgress("moving-instances-complete", 100, "settings.dataSettings.moveComplete", null, totalSize, totalSize);
        return new SetInstanceDirResult(ok, newPath);
    }

    private async Task<AuthServerPingResult> PingAuthServerAsync(string? authDomainOverride)
    {
        var s = Services.GetRequiredService<ISettingsService>();
        var authDomain = !string.IsNullOrWhiteSpace(authDomainOverride)
            ? authDomainOverride
            : s.GetAuthDomain();
        if (string.IsNullOrWhiteSpace(authDomain)) authDomain = "sessions.sanasol.ws";

        if (IsOfficialAuthDomain(authDomain))
            return new AuthServerPingResult(true, 0, authDomain, DateTime.UtcNow.ToString("o"), true);

        var pingUrl = BuildAuthPingUrl(authDomain);
        var http = Services.GetRequiredService<HttpClient>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await http.GetAsync(pingUrl, cts.Token);
            sw.Stop();
            var available = response.IsSuccessStatusCode ||
                (int)response.StatusCode is 404 or 401 or 403;
            return new AuthServerPingResult(available, sw.ElapsedMilliseconds, authDomain, DateTime.UtcNow.ToString("o"), false);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new AuthServerPingResult(false, sw.ElapsedMilliseconds, authDomain, DateTime.UtcNow.ToString("o"), false, ex.Message);
        }
    }

    private async Task ImportPwrFileAsync(string pwrPath, IInstanceService instanceService)
    {
        var butler = Services.GetRequiredService<IButlerService>();
        var fileName = Path.GetFileNameWithoutExtension(pwrPath);
        var version = InstanceService.TryParseVersionFromPwrFilename(fileName);
        var branch = "release";
        var newId = Guid.NewGuid().ToString();
        var targetPath = instanceService.CreateInstanceDirectory(branch, newId);
        Logger.Info("IPC", $"Importing PWR to new instance: {targetPath} (version: {version})");
        await butler.ApplyPwrAsync(pwrPath, targetPath, (p, m) => Logger.Debug("IPC", $"Import {p}% {m}"));
        var meta = new InstanceMeta
        {
            Id = newId,
            Name = $"Imported {(version > 0 ? $"v{version}" : "Game")}",
            Branch = branch,
            Version = version,
            InstalledVersion = version,
            CreatedAt = DateTime.UtcNow,
            IsLatest = false
        };
        instanceService.SaveInstanceMeta(targetPath, meta);
        Logger.Success("IPC", $"Imported PWR to: {targetPath}");
    }

    private static bool IsOfficialAuthDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return false;
        var n = domain.Trim().ToLowerInvariant();
        if (n.StartsWith("https://")) n = n[8..];
        if (n.StartsWith("http://")) n = n[7..];
        n = n.TrimEnd('/');
        return n == "sessions.hytale.com" || n.EndsWith(".hytale.com") || n == "hytale.com";
    }

    private static string BuildAuthPingUrl(string authDomain)
    {
        var n = authDomain.Trim().TrimEnd('/');
        if (!n.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !n.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            n = $"https://{n}";
        return $"{n}/health";
    }

    private static string GetMirrorHostname(MirrorMeta mirror)
    {
        try
        {
            if (mirror.SourceType == "json-index" && !string.IsNullOrEmpty(mirror.JsonIndex?.ApiUrl))
                return new Uri(mirror.JsonIndex.ApiUrl).Host;
            if (mirror.SourceType == "pattern" && !string.IsNullOrEmpty(mirror.Pattern?.BaseUrl))
                return new Uri(mirror.Pattern.BaseUrl).Host;
        }
        catch { /* ignore */ }
        return "";
    }

    private static void ApplySetting(ISettingsService s, string key, JsonElement val)
    {
        switch (key)
        {
            case "language":                 s.SetLanguage(val.GetString() ?? "en-US"); break;
            case "musicEnabled":             s.SetMusicEnabled(val.GetBoolean()); break;
            case "launcherBranch":           s.SetLauncherBranch(val.GetString() ?? "release"); break;
            case "versionType":              s.SetVersionType(val.GetString() ?? "release"); break;
            case "selectedVersion":          s.SetSelectedVersion(val.ValueKind == JsonValueKind.Number ? val.GetInt32() : 0); break;
            case "closeAfterLaunch":         s.SetCloseAfterLaunch(val.GetBoolean()); break;
            case "launchAfterDownload":      s.SetLaunchAfterDownload(val.GetBoolean()); break;
            case "showDiscordAnnouncements": s.SetShowDiscordAnnouncements(val.GetBoolean()); break;
            case "disableNews":              s.SetDisableNews(val.GetBoolean()); break;
            case "backgroundMode":           s.SetBackgroundMode(val.GetString() ?? "default"); break;
            case "accentColor":              s.SetAccentColor(val.GetString() ?? "#7C5CFC"); break;
            case "onlineMode":               s.SetOnlineMode(val.GetBoolean()); break;
            case "authDomain":               s.SetAuthDomain(val.GetString() ?? ""); break;
            case "javaArguments":            s.SetJavaArguments(val.GetString() ?? ""); break;
            case "useCustomJava":            s.SetUseCustomJava(val.GetBoolean()); break;
            case "customJavaPath":           s.SetCustomJavaPath(val.GetString() ?? ""); break;
            case "gpuPreference":            s.SetGpuPreference(val.GetString() ?? "dedicated"); break;
            case "gameEnvironmentVariables": s.SetGameEnvironmentVariables(val.GetString() ?? ""); break;
            case "useDualAuth":              s.SetUseDualAuth(val.GetBoolean()); break;
            case "hasCompletedOnboarding":   s.SetHasCompletedOnboarding(val.GetBoolean()); break;
            case "showAlphaMods":            s.SetShowAlphaMods(val.GetBoolean()); break;
            default: Logger.Warning("IPC", $"Unknown setting key: {key}"); break;
        }
    }

    private static void CollectFilesRecursive(string sourceDir, string destRoot, string originalRoot,
        List<(string source, string dest)> files)
    {
        try
        {
            foreach (var file in Directory.GetFiles(sourceDir))
                files.Add((file, Path.Combine(destRoot, Path.GetRelativePath(originalRoot, file))));
            foreach (var dir in Directory.GetDirectories(sourceDir))
                CollectFilesRecursive(dir, destRoot, originalRoot, files);
        }
        catch (Exception ex) { Logger.Warning("IPC", $"Failed to enumerate {sourceDir}: {ex.Message}"); }
    }

    private static Dictionary<string, string> ParseHeadersString(string headersString)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(headersString)) return headers;
        var rx = new Regex(@"([A-Za-z0-9_-]+)=(?:""([^""]*)""|(\S+))", RegexOptions.Compiled);
        foreach (Match m in rx.Matches(headersString))
        {
            var name = m.Groups[1].Value;
            var value = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            if (!string.IsNullOrEmpty(name)) headers[name] = value;
        }
        return headers;
    }
    #endregion
}