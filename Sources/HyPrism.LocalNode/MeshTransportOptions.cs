// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;

namespace HyPrism.LocalNode;

/// <summary>
/// Configures the local-network mesh transport owned by one Local Node process
/// </summary>
public sealed record MeshTransportOptions
{
    public static readonly IPAddress DefaultMulticastAddress = IPAddress.Parse("239.255.72.80");

    public int DiscoveryPort { get; init; } = 47831;
    public int TransportPort { get; init; }
    public bool EnableMulticast { get; init; } = true;
    public IReadOnlyList<IPEndPoint> DiscoveryTargets { get; init; } = [];
    public TimeSpan AnnouncementInterval { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan PresenceTimeout { get; init; } = TimeSpan.FromSeconds(35);
    public TimeSpan EndpointLifetime { get; init; } = TimeSpan.FromSeconds(45);

    internal void Validate()
    {
        if (DiscoveryPort is < 1 or > ushort.MaxValue
            || TransportPort is < 0 or > ushort.MaxValue
            || AnnouncementInterval <= TimeSpan.Zero
            || PresenceTimeout <= AnnouncementInterval
            || EndpointLifetime <= AnnouncementInterval
            || (!EnableMulticast && DiscoveryTargets.Count == 0)
            || DiscoveryTargets.Any(endpoint => endpoint.Port is < 1 or > ushort.MaxValue))
        {
            throw new ArgumentOutOfRangeException(
                nameof(MeshTransportOptions),
                "Mesh transport options are invalid");
        }
    }

    internal IReadOnlyList<IPEndPoint> ResolveDiscoveryTargets()
        => DiscoveryTargets.Count > 0
            ? DiscoveryTargets
            : [new IPEndPoint(DefaultMulticastAddress, DiscoveryPort)];
}
