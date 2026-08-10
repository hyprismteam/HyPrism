// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Logger.CaptureOriginalConsole();
        DesktopRuntime.Services = Bootstrapper.Initialize();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect();
}

internal static class DesktopRuntime
{
    public static IServiceProvider Services { get; set; } = null!;
}
