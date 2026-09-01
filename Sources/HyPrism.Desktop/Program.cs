// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Skia;
using HyPrism.Desktop.Features.About;
using HyPrism.Desktop.Features.News;
using HyPrism.Desktop.Features.Settings;
using HyPrism.Desktop.Integrations.Discord;
using HyPrism.Desktop.Platform;
using HyPrism.Core;
using HyPrism.Core.Accounts;
using HyPrism.Core.Infrastructure;
using HyPrism.Core.Application.Ports;
using HyPrism.Core.Game.Launch;
using HyPrism.LocalNode;
using Microsoft.Extensions.DependencyInjection;

namespace HyPrism.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Logger.CaptureOriginalConsole();
        DesktopGpuPreference.ConfigureBeforeAvalonia();
        LauncherUserAgent.ConfigureVersion(DesktopApplicationInfo.Version);
        DesktopRuntime.Services = Bootstrapper.Initialize(services =>
        {
            services.AddSingleton<DesktopSettingsStore>();
            services.AddSingleton<IDesktopSettingsStore>(provider =>
                provider.GetRequiredService<DesktopSettingsStore>());
            services.AddSingleton<HytaleNewsClient>();
            services.AddSingleton<IHytaleNewsClient>(provider =>
                provider.GetRequiredService<HytaleNewsClient>());
            services.AddSingleton<GitHubClient>();
            services.AddSingleton<IGitHubClient>(provider =>
                provider.GetRequiredService<GitHubClient>());
            services.AddSingleton<RemoteImageCache>();
            services.AddSingleton<DiscordPresence>();
            services.AddSingleton<IDiscordPresence>(provider =>
                provider.GetRequiredService<DiscordPresence>());
            services.AddSingleton<GpuProvider>();
            services.AddSingleton<IGpuProvider>(provider =>
                provider.GetRequiredService<GpuProvider>());
            services.AddSingleton<LocalNodeServiceFactory>();
            services.AddSingleton<ILocalNodeServiceFactory>(provider =>
                provider.GetRequiredService<LocalNodeServiceFactory>());
            services.AddSingleton<IOAuthCallbackPageRenderer>(
                provider => new OAuthCallbackPageRenderer(
                    provider.GetRequiredService<IDesktopSettingsStore>()));
        });

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(DesktopGpuPreference.CreateWin32Options())
            .With(new SkiaOptions
            {
                MaxGpuResourceSizeBytes = 512 * 1024 * 1024
            });
}

internal static class DesktopRuntime
{
    public static IServiceProvider Services { get; set; } = null!;
}
