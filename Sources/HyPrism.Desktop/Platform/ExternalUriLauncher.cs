// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HyPrism.Core.Infrastructure;

namespace HyPrism.Desktop.Platform;

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

    /// <inheritdoc/>
    public async Task<bool> LaunchDirectoryAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path))
        {
            Logger.Warning("Launcher", $"Rejected unavailable directory: {path}");
            return false;
        }

        try
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                var launchTask = await Dispatcher.UIThread.InvokeAsync(
                    () => LaunchDirectoryOnUiThreadAsync(path, cancellationToken));
                return launchTask;
            }

            return await LaunchDirectoryOnUiThreadAsync(path, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error("Launcher", $"Failed to open directory: {ex.Message}");
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

    private async Task<bool> LaunchDirectoryOnUiThreadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var topLevel = topLevelProvider();
        if (topLevel is null)
        {
            Logger.Warning("Launcher", "Cannot open a directory before the main window is available");
            return false;
        }

        Logger.Info("Launcher", $"Opening directory: {path}");
        return await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    }
}
