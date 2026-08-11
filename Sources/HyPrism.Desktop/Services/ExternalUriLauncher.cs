// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Controls;
using Avalonia.Threading;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Desktop.Services;

/// <summary>
/// Opens external URIs with Avalonia's native platform launcher
/// </summary>
/// <param name="topLevelProvider">Resolves the active top-level window when a URI is opened</param>
public sealed class ExternalUriLauncher(Func<TopLevel?> topLevelProvider) : IExternalUriLauncher
{
    /// <inheritdoc/>
    public async Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();

        if (!uri.IsAbsoluteUri ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            Logger.Warning("Launcher", $"Rejected unsupported external URI: {uri}");
            return false;
        }

        try
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                var launchTask = await Dispatcher.UIThread.InvokeAsync(
                    () => LaunchOnUiThreadAsync(uri, cancellationToken));
                return launchTask;
            }

            return await LaunchOnUiThreadAsync(uri, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error("Launcher", $"Failed to open external URI: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> LaunchOnUiThreadAsync(Uri uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var topLevel = topLevelProvider();
        if (topLevel is null)
        {
            Logger.Warning("Launcher", "Cannot open an external URI before the main window is available");
            return false;
        }

        Logger.Info("Launcher", $"Opening external URI: {uri}");
        return await topLevel.Launcher.LaunchUriAsync(uri);
    }
}
