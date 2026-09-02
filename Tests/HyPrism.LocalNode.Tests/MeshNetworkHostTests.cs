// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Net.Sockets;
using HyPrism.Mesh;
using HyPrism.LocalNode;

namespace HyPrism.LocalNode.Tests;

public sealed class MeshNetworkHostTests
{
    [Fact]
    public async Task Hosts_ConfirmedFriends_DiscoverAndExchangeEncryptedPresence()
    {
        using var aliceDirectory = new TemporaryDirectory("Alice");
        using var bobDirectory = new TemporaryDirectory("Bob");
        var aliceFriends = new MeshFriendService(aliceDirectory.Path);
        var bobFriends = new MeshFriendService(bobDirectory.Path);
        var (alice, bob) = await PairAsync(aliceFriends, bobFriends);
        var aliceDiscoveryPort = GetAvailableUdpPort();
        var bobDiscoveryPort = GetAvailableUdpPort();
        using var aliceHost = new MeshNetworkHost(
            aliceDirectory.Path,
            CreateOptions(aliceDiscoveryPort, bobDiscoveryPort),
            friends: aliceFriends);
        using var bobHost = new MeshNetworkHost(
            bobDirectory.Path,
            CreateOptions(bobDiscoveryPort, aliceDiscoveryPort),
            friends: bobFriends);

        await aliceHost.StartAsync(CancellationToken.None);
        await bobHost.StartAsync(CancellationToken.None);
        try
        {
            await aliceHost.ActivateProfileAsync("alice-profile", "Alice");
            await bobHost.ActivateProfileAsync("bob-profile", "Bob");

            var aliceSawBob = aliceHost.WaitForPresenceAsync(
                "alice-profile",
                bob.PeerId,
                TimeSpan.FromSeconds(5));
            var bobSawAlice = bobHost.WaitForPresenceAsync(
                "bob-profile",
                alice.PeerId,
                TimeSpan.FromSeconds(5));
            var presence = await Task.WhenAll(aliceSawBob, bobSawAlice);

            Assert.All(presence, item => Assert.True(item.IsOnline));
            Assert.Equal("Bob", presence[0].DisplayName);
            Assert.Equal("Alice", presence[1].DisplayName);
            Assert.InRange(aliceHost.TransportPort, 1, ushort.MaxValue);
            Assert.InRange(bobHost.TransportPort, 1, ushort.MaxValue);
        }
        finally
        {
            await aliceHost.StopAsync(CancellationToken.None);
            await bobHost.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void RateLimiter_BurstIsExhausted_RefillsFromTimeProvider()
    {
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var limits = new MeshSecurityLimits
        {
            NetworkPacketsPerSecond = 2,
            NetworkPacketBurst = 2
        };
        var limiter = new MeshIpRateLimiter(limits, time);
        var address = IPAddress.Parse("192.0.2.10");

        Assert.True(limiter.TryConsume(address));
        Assert.True(limiter.TryConsume(address));
        Assert.False(limiter.TryConsume(address));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(limiter.TryConsume(address));
    }

    private static MeshTransportOptions CreateOptions(int discoveryPort, int targetPort)
        => new()
        {
            DiscoveryPort = discoveryPort,
            EnableMulticast = false,
            DiscoveryTargets = [new IPEndPoint(IPAddress.Loopback, targetPort)],
            AnnouncementInterval = TimeSpan.FromMilliseconds(100),
            PresenceTimeout = TimeSpan.FromSeconds(2),
            EndpointLifetime = TimeSpan.FromSeconds(2)
        };

    private static async Task<(MeshPublicIdentity Alice, MeshPublicIdentity Bob)> PairAsync(
        MeshFriendService aliceFriends,
        MeshFriendService bobFriends)
    {
        var alice = await aliceFriends.GetIdentityAsync("alice-profile");
        var bob = await bobFriends.GetIdentityAsync("bob-profile");
        var invite = await aliceFriends.CreateInviteAsync(
            "alice-profile",
            "Alice",
            TimeSpan.FromMinutes(10));
        var acceptance = await bobFriends.AcceptInviteAsync(
            "bob-profile",
            "Bob",
            invite.Value.Token);
        var completion = await aliceFriends.CompleteInviteAsync(
            "alice-profile",
            acceptance.Value.AcceptanceToken);
        Assert.True(invite.IsSuccess);
        Assert.True(acceptance.IsSuccess);
        Assert.True(completion.IsSuccess);
        return (alice, bob);
    }

    private static int GetAvailableUdpPort()
    {
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)client.Client.LocalEndPoint!).Port;
    }

    private sealed class MutableTimeProvider(DateTimeOffset value) : TimeProvider
    {
        private DateTimeOffset _value = value;

        public override DateTimeOffset GetUtcNow() => _value;
        public void Advance(TimeSpan value) => _value = _value.Add(value);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory(string name)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"HyPrism-MeshNetwork{name}Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
