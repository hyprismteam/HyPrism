// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using HyPrism.Desktop.Features.About;
using HyPrism.Desktop.Features.News;
using HyPrism.Desktop.Features.Settings;
using HyPrism.Desktop.Platform;
using HyPrism.Desktop.Localization;
using HyPrism.Desktop.Shell;
using HyPrism.Core;
using HyPrism.Core.Application.Ports;
using HyPrism.Core.Application.Progress;
using HyPrism.Core.Game;
using HyPrism.Core.Game.Instances;
using HyPrism.Core.Game.Launch;
using HyPrism.Core.Game.Mods;
using HyPrism.Core.Game.Sources;
using HyPrism.Core.Game.Versions;
using HyPrism.Core.Accounts;
using HyPrism.Core.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HyPrism.Desktop;

public sealed partial class App : Application
{
    private MainWindowViewModel? _mainWindowViewModel;
    private readonly CancellationTokenSource _bootstrapCancellation = new();
    private Task? _bootstrapTask;

    public override void Initialize()
        => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = DesktopRuntime.Services;
            var settings = services.GetRequiredService<IDesktopSettingsStore>();
            services.GetRequiredService<IDiscordPresence>().Initialize();

            var localizer = new StringLocalizer(settings.Language);
            if (!string.Equals(settings.Language, localizer.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
                settings.Language = localizer.CurrentLanguage;

            var mainWindow = new MainWindow();
            var uriLauncher = new ExternalUriLauncher(() => mainWindow);
            var filePicker = new FilePicker(() => mainWindow);
            _mainWindowViewModel = new MainWindowViewModel(
                services.GetRequiredService<IInstanceRepository>(),
                services.GetRequiredService<IProfileManager>(),
                services.GetRequiredService<IProfileRepository>(),
                services.GetRequiredService<IGameLaunchCoordinator>(),
                services.GetRequiredService<IGameInstallationWorkflow>(),
                services.GetRequiredService<IGameProcessTracker>(),
                services.GetRequiredService<IProgressReporter>(),
                settings,
                services.GetRequiredService<IHytaleNewsClient>(),
                uriLauncher,
                services.GetRequiredService<HttpClient>(),
                localizer,
                filePicker,
                services.GetRequiredService<IGitHubClient>(),
                services.GetRequiredService<IMirrorCatalog>(),
                services.GetRequiredService<IMirrorDiscovery>(),
                services.GetRequiredService<IGameVersionCatalog>(),
                services.GetRequiredService<IModManager>(),
                services.GetRequiredService<IHytaleAuthenticator>(),
                services.GetRequiredService<RemoteImageCache>(),
                services.GetRequiredService<IGameConsoleService>());

            _mainWindowViewModel.BeginStartupLoading();
            mainWindow.DataContext = _mainWindowViewModel;
            desktop.MainWindow = mainWindow;

            desktop.Exit += OnDesktopExit;
            _bootstrapTask = InitializeAsync(
                services,
                _mainWindowViewModel,
                _bootstrapCancellation.Token);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _bootstrapCancellation.Cancel();
        _mainWindowViewModel?.Dispose();
        (DesktopRuntime.Services as IDisposable)?.Dispose();
        Logger.Shutdown();
    }

    private static async Task InitializeAsync(
        IServiceProvider services,
        MainWindowViewModel viewModel,
        CancellationToken cancellationToken)
    {
        var minimumVisibleTime = Task.Delay(TimeSpan.FromMilliseconds(950), cancellationToken);
        try
        {
            await Task.WhenAll(
                InitializeCoreAsync(services, cancellationToken),
                PreloadDynamicContentAsync(viewModel, cancellationToken),
                minimumVisibleTime);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(viewModel.CompleteStartupLoading);
    }

    private static async Task InitializeCoreAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        try
        {
            await Bootstrapper.InitializeAsync(services, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.Error("Bootstrapper", $"Asynchronous initialization failed: {exception}");
        }
    }

    private static async Task PreloadDynamicContentAsync(
        MainWindowViewModel viewModel,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            await viewModel.PreloadStartupDataAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.Warning("Startup", "Dynamic content preload exceeded the startup time limit");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.Warning("Startup", $"Dynamic content preload failed: {exception.Message}");
        }
    }
}
