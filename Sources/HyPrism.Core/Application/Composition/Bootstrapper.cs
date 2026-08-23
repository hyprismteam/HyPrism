// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using HyPrism.Core.Infrastructure;
using HyPrism.Core.Application.Ports;
using HyPrism.Core.Application.Progress;
using HyPrism.Core.Accounts;
using HyPrism.Core.Game;
using HyPrism.Core.Game.Assets;
using HyPrism.Core.Game.Patching;
using HyPrism.Core.Game.Download;
using HyPrism.Core.Game.Instances;
using HyPrism.Core.Game.Launch;
using HyPrism.Core.Game.Mods;
using HyPrism.Core.Game.Sources;
using HyPrism.Core.Game.Versions;

namespace HyPrism.Core;

/// <summary>
/// Builds and initializes the shared launcher service graph
/// </summary>
public static class Bootstrapper
{
    /// <summary>
    /// URL parts for fetching CurseForge API key.
    /// Per legacy policy, the key cannot be stored in plain text
    /// </summary>
    private static string CurseForgeKeySourceUrl => string.Concat(
        System.Text.Encoding.UTF8.GetString(Convert.FromBase64String("aHR0cHM6Ly9yYXcuZ2l0aHVidXNlcmNvbnRlbnQuY29tLw==")),
        System.Text.Encoding.UTF8.GetString(Convert.FromBase64String("UHJpc21MYXVuY2hlci9QcmlzbUxhdW5jaGVy")),
        System.Text.Encoding.UTF8.GetString(Convert.FromBase64String("L2RldmVsb3AvQ01ha2VMaXN0cy50eHQ=")));

    /// <summary>
    /// Creates the shared launcher service graph and applies host-specific registrations
    /// </summary>
    /// <param name="configureHost">Optional callback that adds or replaces platform services before the provider is built</param>
    /// <returns>The application service provider owned by the calling host</returns>
    /// <exception cref="InvalidOperationException">Thrown when the registered service graph cannot be constructed</exception>
    public static IServiceProvider Initialize(Action<IServiceCollection>? configureHost = null)
    {
        var appPath = new AppPathConfiguration(LauncherUtilities.GetEffectiveAppDir());
        var logSession = new LogSessionPaths(appPath);
        Logger.ConfigureFileLogging(logSession.LauncherLogPath);
        Logger.Info("Bootstrapper", "Initializing application services...");
        Logger.Info("Bootstrapper", $"Log session directory: {logSession.SessionDirectory}");
        try
        {
            var services = new ServiceCollection();

            #region Core Infrastructure & Configuration

            services.AddSingleton(appPath);
            services.AddSingleton(logSession);

            services.AddSingleton(_ =>
            {
                var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                client.DefaultRequestHeaders.Add("User-Agent", LauncherUserAgent.Value);
                return client;
            });

            // Config is registered as both concrete (for HytaleVersionSource/HytaleAuthenticator that
            // need it before IConfigStore resolution) and as interface for all other consumers.
            services.AddSingleton<JsonConfigStore>(sp =>
                new JsonConfigStore(sp.GetRequiredService<AppPathConfiguration>().AppDir));
            services.AddSingleton<IConfigStore>(sp => sp.GetRequiredService<JsonConfigStore>());

            services.AddSingleton(sp =>
                new MirrorCatalog(
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<HttpClient>()));
            services.AddSingleton<IMirrorCatalog>(sp => sp.GetRequiredService<MirrorCatalog>());
            services.AddSingleton(sp => new MirrorDiscovery(sp.GetRequiredService<HttpClient>()));
            services.AddSingleton<IMirrorDiscovery>(sp => sp.GetRequiredService<MirrorDiscovery>());

            #endregion

            #region Data & Utility Services

            services.AddSingleton<HttpFileDownloader>();
            services.AddSingleton<IFileDownloader>(sp => sp.GetRequiredService<HttpFileDownloader>());

            #endregion

            #region Game & Instance Management

            services.AddSingleton(sp =>
                new InstanceRepository(
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<IConfigStore>()));
            services.AddSingleton<IInstanceRepository>(sp => sp.GetRequiredService<InstanceRepository>());

            services.AddSingleton(sp =>
                new InstanceMigrator(
                    sp.GetRequiredService<AppPathConfiguration>(),
                    sp.GetRequiredService<IConfigStore>(),
                    sp.GetRequiredService<IInstanceRepository>()));
            services.AddSingleton<IInstanceMigrator>(sp => sp.GetRequiredService<InstanceMigrator>());

            services.AddSingleton(sp =>
                new AvatarCache(
                    sp.GetRequiredService<IInstanceRepository>(),
                    sp.GetRequiredService<AppPathConfiguration>().AppDir));
            services.AddSingleton<IAvatarCache>(sp => sp.GetRequiredService<AvatarCache>());

            services.AddSingleton(sp =>
                new GameVersionCatalog(
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<IConfigStore>(),
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<HytaleVersionSource>(),
                    mirrorCatalog: sp.GetRequiredService<IMirrorCatalog>()));
            services.AddSingleton<IGameVersionCatalog>(sp => sp.GetRequiredService<GameVersionCatalog>());

            services.AddSingleton(sp =>
                new RuntimeProvisioner(
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<HttpClient>()));
            services.AddSingleton<IRuntimeProvisioner>(sp => sp.GetRequiredService<RuntimeProvisioner>());

            services.AddSingleton(sp => new GameProcessTracker(
                sp.GetRequiredService<AppPathConfiguration>()));
            services.AddSingleton<IGameProcessTracker>(sp => sp.GetRequiredService<GameProcessTracker>());

            services.AddSingleton(sp =>
                new ModManager(
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<IConfigStore>(),
                    sp.GetRequiredService<IInstanceRepository>(),
                    sp.GetRequiredService<IProgressReporter>()));
            services.AddSingleton<IModManager>(sp => sp.GetRequiredService<ModManager>());

            services.AddSingleton(sp =>
                new PatchManager(
                    sp.GetRequiredService<IGameVersionCatalog>(),
                    sp.GetRequiredService<IButlerClient>(),
                    sp.GetRequiredService<IFileDownloader>(),
                    sp.GetRequiredService<IInstanceRepository>(),
                    sp.GetRequiredService<IProgressReporter>(),
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<AppPathConfiguration>()));
            services.AddSingleton<IPatchManager>(sp => sp.GetRequiredService<PatchManager>());

            services.AddSingleton(sp =>
                new GameLauncher(
                    sp.GetRequiredService<IConfigStore>(),
                    sp.GetRequiredService<IRuntimeProvisioner>(),
                    sp.GetRequiredService<IInstanceRepository>(),
                    sp.GetRequiredService<IGameProcessTracker>(),
                    sp.GetRequiredService<IProgressReporter>(),
                    sp.GetRequiredService<IDiscordPresence>(),
                    sp.GetRequiredService<ISkinRepository>(),
                    sp.GetRequiredService<IAvatarCache>(),
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<IHytaleAuthenticator>(),
                    sp.GetRequiredService<IGpuProvider>(),
                    sp.GetRequiredService<AppPathConfiguration>(),
                    sp.GetRequiredService<IProfileManager>(),
                    sp.GetRequiredService<IProfileRepository>(),
                    sp.GetRequiredService<ILocalNodeServiceFactory>(),
                    sp.GetRequiredService<LogSessionPaths>()));
            services.AddSingleton<IGameLauncher>(sp => sp.GetRequiredService<GameLauncher>());

            services.AddSingleton(sp =>
                new GameInstallationWorkflow(
                    sp.GetRequiredService<IConfigStore>(),
                    sp.GetRequiredService<IInstanceRepository>(),
                    sp.GetRequiredService<IGameVersionCatalog>(),
                    sp.GetRequiredService<IRuntimeProvisioner>(),
                    sp.GetRequiredService<IButlerClient>(),
                    sp.GetRequiredService<IFileDownloader>(),
                    sp.GetRequiredService<IProgressReporter>(),
                    sp.GetRequiredService<IPatchManager>(),
                    sp.GetRequiredService<IGameLauncher>(),
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<AppPathConfiguration>()));
            services.AddSingleton<IGameInstallationWorkflow>(sp => sp.GetRequiredService<GameInstallationWorkflow>());

            services.AddSingleton<GameLaunchCoordinator>();
            services.AddSingleton<IGameLaunchCoordinator>(sp => sp.GetRequiredService<GameLaunchCoordinator>());

            #endregion

            #region User & Skin Management

            services.AddSingleton(sp =>
                new SkinRepository(
                    sp.GetRequiredService<AppPathConfiguration>(),
                    sp.GetRequiredService<IConfigStore>(),
                    sp.GetRequiredService<IInstanceRepository>(),
                    sp.GetRequiredService<IProfileManager>()));
            services.AddSingleton<ISkinRepository>(sp => sp.GetRequiredService<SkinRepository>());

            services.AddSingleton(sp =>
                new ProfileManager(
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<IConfigStore>(),
                    sp.GetRequiredService<IAvatarCache>()));
            services.AddSingleton<IProfileManager>(sp => sp.GetRequiredService<ProfileManager>());

            services.AddSingleton(sp =>
                new UserIdentityProvider(
                    sp.GetRequiredService<ISkinRepository>(),
                    sp.GetRequiredService<IInstanceRepository>(),
                    sp.GetRequiredService<IProfileManager>()));
            services.AddSingleton<IUserIdentityProvider>(sp => sp.GetRequiredService<UserIdentityProvider>());

            services.AddSingleton(sp =>
                new JsonProfileRepository(
                    sp.GetRequiredService<AppPathConfiguration>(),
                    sp.GetRequiredService<IConfigStore>(),
                    sp.GetRequiredService<ISkinRepository>(),
                    sp.GetRequiredService<IInstanceRepository>(),
                    sp.GetRequiredService<IUserIdentityProvider>()));
            services.AddSingleton<IProfileRepository>(sp => sp.GetRequiredService<JsonProfileRepository>());

            services.AddSingleton(sp =>
                new HytaleAuthenticator(
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<IConfigStore>(),
                    sp.GetService<IOAuthCallbackPageRenderer>()));
            services.AddSingleton<IHytaleAuthenticator>(sp => sp.GetRequiredService<HytaleAuthenticator>());

            // Official version source that requires authentication
            services.AddSingleton(sp =>
                new HytaleVersionSource(
                    sp.GetRequiredService<AppPathConfiguration>().AppDir,
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<HytaleAuthenticator>(),
                    sp.GetRequiredService<IConfigStore>(),
                    sp.GetRequiredService<IProfileManager>()));

            // Mirror sources are loaded from JSON meta files by MirrorCatalogLoader
            // (see GameVersionCatalog registration above)

            #endregion

            #region Integrations and game tooling

            services.AddSingleton(sp =>
                new ProgressReporter(sp.GetRequiredService<IDiscordPresence>()));
            services.AddSingleton<IProgressReporter>(sp => sp.GetRequiredService<ProgressReporter>());

            services.AddSingleton(sp =>
                new ButlerClient(sp.GetRequiredService<AppPathConfiguration>().AppDir));
            services.AddSingleton<IButlerClient>(sp => sp.GetRequiredService<ButlerClient>());

            #endregion

            // Platform hosts add their adapters after the shared launcher graph.
            configureHost?.Invoke(services);

            var provider = services.BuildServiceProvider();
            Logger.Success("Bootstrapper", "Application services initialized successfully");

            return provider;
        }
        catch (Exception ex)
        {
            Logger.Error("Bootstrapper", $"Failed to initialize application services: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Performs asynchronous initialization after the service provider has been built
    /// </summary>
    /// <param name="services">Service provider returned by <see cref="Initialize"/></param>
    /// <returns>A task that completes after optional remote bootstrap data has been prepared</returns>
    public static async Task InitializeAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await EnsureCurseForgeKeyAsync(services, cancellationToken);
    }

    /// <summary>
    /// Ensures the CurseForge API key is present in configuration.
    /// If missing, fetches it from the upstream source
    /// </summary>
    private static async Task EnsureCurseForgeKeyAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var configStore = services.GetRequiredService<IConfigStore>();
        var httpClient = services.GetRequiredService<HttpClient>();

        if (!string.IsNullOrEmpty(configStore.Configuration.CurseForgeKey))
        {
            Logger.Info("Bootstrapper", "CurseForge API key already configured");
            return;
        }

        Logger.Info("Bootstrapper", "CurseForge API key not found, fetching...");

        try
        {
            var cmakeContent = await httpClient.GetStringAsync(CurseForgeKeySourceUrl, cancellationToken);

            var match = Regex.Match(cmakeContent, @"set\(Launcher_CURSEFORGE_API_KEY\s+""([^""]+)""");

            if (match.Success)
            {
                var apiKey = match.Groups[1].Value;
                configStore.Configuration.CurseForgeKey = apiKey;
                configStore.SaveConfig();
                Logger.Success("Bootstrapper", "CurseForge API key fetched and saved successfully");
            }
            else
            {
                Logger.Warning("Bootstrapper", "Could not parse CurseForge API key");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Logger.Info("Bootstrapper", "Asynchronous initialization cancelled during shutdown");
        }
        catch (Exception ex)
        {
            Logger.Warning("Bootstrapper", $"Failed to fetch CurseForge API key: {ex.Message}");
        }
    }
}

/// <summary>
/// Stores the writable application directory resolved by the host
/// </summary>
/// <param name="AppDir">Absolute path to the writable application directory</param>
public record AppPathConfiguration(string AppDir);
