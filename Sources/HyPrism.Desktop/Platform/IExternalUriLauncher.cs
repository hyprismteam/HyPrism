// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Desktop.Platform;

/// <summary>
/// Opens external URIs through the platform services owned by the desktop host
/// </summary>
public interface IExternalUriLauncher
{
    /// <summary>
    /// Requests that the operating system open an absolute HTTP or HTTPS URI
    /// </summary>
    /// <param name="uri">The absolute URI to open</param>
    /// <param name="cancellationToken">Token checked before the platform request is dispatched</param>
    /// <returns><see langword="true"/> when the operating system accepts the request; otherwise, <see langword="false"/></returns>
    Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default);
}
