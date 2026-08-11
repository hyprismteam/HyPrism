// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HyPrism.Desktop.Localization;
using HyPrism.Desktop.Services;
using HyPrism.Desktop.ViewModels;
using HyPrism.Desktop.Views;
using HyPrism.Services.Core.App;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Core.Integration;
using HyPrism.Services.Core.Platform;
using HyPrism.Services.Game;
using HyPrism.Services.Game.Instance;
using HyPrism.Services.Game.Launch;
using HyPrism.Services.User;
using Microsoft.Extensions.DependencyInjection;

namespace HyPrism.Desktop;

public sealed partial class App : Application
{
    private MainWindowViewModel? _mainWindowViewModel;

    public override void Initialize()
        => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = DesktopRuntime.Services;
            var settings = services.GetRequiredService<ISettingsService>();

            var localizer = new LocalizationService(settings.GetLanguage());
            if (!string.Equals(settings.GetLanguage(), localizer.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
                settings.SetLanguage(localizer.CurrentLanguage);

            var mainWindow = new MainWindow();
            var uriLauncher = new ExternalUriLauncher(() => mainWindow);
            _mainWindowViewModel = new MainWindowViewModel(
                services.GetRequiredService<IInstanceService>(),
                services.GetRequiredService<IProfileService>(),
                services.GetRequiredService<IProfileManagementService>(),
                services.GetRequiredService<IGameLaunchCoordinator>(),
                services.GetRequiredService<IGameSessionService>(),
                services.GetRequiredService<IGameProcessService>(),
                services.GetRequiredService<IProgressNotificationService>(),
                settings,
                services.GetRequiredService<INewsService>(),
                uriLauncher,
                services.GetRequiredService<HttpClient>(),
                localizer,
                services.GetRequiredService<IFileDialogService>(),
                services.GetRequiredService<IGitHubService>());

            mainWindow.DataContext = _mainWindowViewModel;
            desktop.MainWindow = mainWindow;

            desktop.Exit += OnDesktopExit;
            _ = Task.Run(() => Bootstrapper.InitializeAsync(services));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _mainWindowViewModel?.Dispose();
        (DesktopRuntime.Services as IDisposable)?.Dispose();
    }
}
