// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using HyPrism.Mesh;

namespace HyPrism.LocalNode;

/// <summary>
/// A bounded token bucket used by one UDP receive loop before packet parsing
/// </summary>
internal sealed class MeshIpRateLimiter
{
    private readonly Dictionary<IPAddress, Bucket> _buckets = [];
    private readonly MeshSecurityLimits _limits;
    private readonly TimeProvider _timeProvider;

    public MeshIpRateLimiter(MeshSecurityLimits limits, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        _limits = limits;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool TryConsume(IPAddress address, int cost = 1)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (cost <= 0 || cost > _limits.NetworkPacketBurst)
            return false;

        var normalizedAddress = Normalize(address);
        var now = _timeProvider.GetUtcNow();
        if (!_buckets.TryGetValue(normalizedAddress, out var bucket))
        {
            Prune(now);
            if (_buckets.Count >= _limits.MaximumTrackedNetworkSources)
                return false;
            bucket = new Bucket(_limits.NetworkPacketBurst, now);
        }

        var elapsedSeconds = Math.Max(0, (now - bucket.UpdatedAt).TotalSeconds);
        var available = Math.Min(
            _limits.NetworkPacketBurst,
            bucket.Tokens + elapsedSeconds * _limits.NetworkPacketsPerSecond);
        _buckets[normalizedAddress] = available >= cost
            ? new Bucket(available - cost, now)
            : new Bucket(available, now);
        return available >= cost;
    }

    private void Prune(DateTimeOffset now)
    {
        if (_buckets.Count < _limits.MaximumTrackedNetworkSources)
            return;

        foreach (var address in _buckets
                     .Where(pair => now - pair.Value.UpdatedAt >= _limits.NetworkSourceIdleLifetime)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _buckets.Remove(address);
        }
    }

    private static IPAddress Normalize(IPAddress address)
        => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private readonly record struct Bucket(double Tokens, DateTimeOffset UpdatedAt);
}
