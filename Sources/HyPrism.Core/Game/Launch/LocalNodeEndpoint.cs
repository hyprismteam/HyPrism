// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Core.Game.Launch;

/// <summary>
/// Defines the loopback route used by the autonomous Local Node
/// </summary>
public static class LocalNodeEndpoint
{
    /// <summary>
    /// Gets the loopback address embedded into the client patch. A literal IP
    /// keeps the client off DNS entirely, so VPN clients with a tunnel DNS
    /// interceptor cannot break the resolution of the Local Node endpoint
    /// </summary>
    public const string Hostname = "127.0.0.1";

    /// <summary>
    /// Gets the non-privileged HTTPS port used by the Local Node
    /// </summary>
    public const int Port = 8443;

    /// <summary>
    /// Gets the canonical issuer URI used in Local Node OmniAuth tokens
    /// </summary>
    public static string Issuer => $"https://{Hostname}:{Port}";
}
