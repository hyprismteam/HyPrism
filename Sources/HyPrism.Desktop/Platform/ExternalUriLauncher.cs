// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HyPrism.Core.Infrastructure;

namespace HyPrism.Desktop.Platform;

/// <summary>
/// Opens external URIs with the operating system's default browser
/// </summary>
/// <param name="topLevelProvider">Resolves the active top-level window when a URI is opened</param>
public sealed class ExternalUriLauncher(Func<TopLevel?> topLevelProvider) : IExternalUriLauncher
{
    /// <inheritdoc/>
    public Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();

        if (!uri.IsAbsoluteUri ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            Logger.Warning("Launcher", $"Rejected unsupported external URI: {uri}");
            return Task.FromResult(false);
        }

        try
        {
            Logger.Info("Launcher", $"Opening external URI: {uri.AbsoluteUri}");
            var process = Process.Start(CreateBrowserStartInfo(uri));
            if (process is null)
                return Task.FromResult(false);

            if (process.StartInfo.RedirectStandardOutput || process.StartInfo.RedirectStandardError)
                _ = DrainAndDisposeAsync(process);
            else
                process.Dispose();

            return Task.FromResult(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error("Launcher", $"Failed to open external URI: {ex.Message}");
            return Task.FromResult(false);
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

    internal static ProcessStartInfo CreateBrowserStartInfo(Uri uri)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            };
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(uri.AbsoluteUri);
        return startInfo;
    }

    private static async Task DrainAndDisposeAsync(Process process)
    {
        try
        {
            await Task.WhenAll(
                process.StandardOutput.ReadToEndAsync(),
                process.StandardError.ReadToEndAsync());
        }
        catch
        {
            // The browser process is detached from the launcher lifecycle
        }
        finally
        {
            process.Dispose();
        }
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
