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
    public async Task Hosts_FriendIdRequest_IsAcceptedThroughPairingTransport()
    {
        using var aliceDirectory = new TemporaryDirectory("AlicePairing");
        using var bobDirectory = new TemporaryDirectory("BobPairing");
        var aliceFriends = new MeshFriendService(aliceDirectory.Path);
        var bobFriends = new MeshFriendService(bobDirectory.Path);
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
        const string aliceProfile = "550e8400-e29b-41d4-a716-446655440000";
        const string bobProfile = "660e8400-e29b-41d4-a716-446655440000";

        await aliceHost.StartAsync(CancellationToken.None);
        await bobHost.StartAsync(CancellationToken.None);
        try
        {
            await aliceHost.ActivateProfileAsync(aliceProfile, "Alice");
            var bobIdentity = await bobHost.ActivateProfileAsync(bobProfile, "Bob");

            var sent = await aliceHost.SendFriendRequestAsync(aliceProfile, bobIdentity.FriendId);
            Assert.True(sent.IsSuccess);
            var incoming = await WaitForAsync(
                () => bobHost.GetIncomingFriendRequests(bobProfile).SingleOrDefault(),
                request => request is not null,
                TimeSpan.FromSeconds(5));
            Assert.Equal(Guid.Parse(aliceProfile), incoming!.RequesterUuid);
            Assert.Equal("Alice", incoming.Username);

            var accepted = await bobHost.AcceptFriendRequestAsync(bobProfile, incoming.RequesterUuid);
            Assert.True(accepted.IsSuccess);
            await WaitForAsync(
                () => aliceFriends.GetFriendsAsync(aliceProfile).GetAwaiter().GetResult().SingleOrDefault(),
                friend => friend is not null,
                TimeSpan.FromSeconds(5));

            Assert.Equal("Bob", (await aliceFriends.GetFriendsAsync(aliceProfile)).Single().DisplayName);
            Assert.Equal("Alice", (await bobFriends.GetFriendsAsync(bobProfile)).Single().DisplayName);
        }
        finally
        {
            await aliceHost.StopAsync(CancellationToken.None);
            await bobHost.StopAsync(CancellationToken.None);
        }
    }
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

            Assert.True(await aliceHost.SendAsync(
                "alice-profile",
                bob.PeerId,
                MeshMessageKind.IceSignal,
                "candidate"u8.ToArray()));
            using var deliveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var deliveries = bobHost
                .ReadApplicationMessagesAsync(deliveryTimeout.Token)
                .GetAsyncEnumerator(deliveryTimeout.Token);
            Assert.True(await deliveries.MoveNextAsync());
            Assert.Equal("bob-profile", deliveries.Current.ProfileId);
            Assert.Equal(alice.PeerId, deliveries.Current.SenderPeerId);
            Assert.Equal(MeshMessageKind.IceSignal, deliveries.Current.Kind);
            Assert.Equal("candidate"u8.ToArray(), deliveries.Current.Payload.ToArray());
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

    [Fact]
    public async Task Discovery_UnknownRoutes_DoNotExhaustKnownPeerRateLimit()
    {
        using var aliceDirectory = new TemporaryDirectory("AliceKnownRoute");
        using var bobDirectory = new TemporaryDirectory("BobKnownRoute");
        using var malloryDirectory = new TemporaryDirectory("MalloryUnknownRoute");
        using var trentDirectory = new TemporaryDirectory("TrentUnknownRoute");
        var aliceFriends = new MeshFriendService(aliceDirectory.Path);
        var bobFriends = new MeshFriendService(bobDirectory.Path);
        var malloryFriends = new MeshFriendService(malloryDirectory.Path);
        var trentFriends = new MeshFriendService(trentDirectory.Path);
        var (alice, bob) = await PairAsync(aliceFriends, bobFriends);
        await PairAsync(
            malloryFriends,
            trentFriends,
            "mallory-profile",
            "trent-profile",
            "Mallory",
            "Trent");

        var aliceDiscoveryPort = GetAvailableUdpPort();
        var bobDiscoveryPort = GetAvailableUdpPort();
        var aliceTargetPort = GetAvailableUdpPort();
        var bobTargetPort = GetAvailableUdpPort();
        var aliceOptions = CreateOptions(aliceDiscoveryPort, aliceTargetPort) with
        {
            AnnouncementInterval = TimeSpan.FromSeconds(30),
            PresenceTimeout = TimeSpan.FromSeconds(60),
            EndpointLifetime = TimeSpan.FromSeconds(60)
        };
        var bobOptions = CreateOptions(bobDiscoveryPort, bobTargetPort) with
        {
            AnnouncementInterval = TimeSpan.FromSeconds(30),
            PresenceTimeout = TimeSpan.FromSeconds(60),
            EndpointLifetime = TimeSpan.FromSeconds(60)
        };
        var limits = new MeshSecurityLimits
        {
            NetworkPacketsPerSecond = 1,
            NetworkPacketBurst = 1
        };
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        using var aliceHost = new MeshNetworkHost(
            aliceDirectory.Path,
            aliceOptions,
            timeProvider: time,
            limits: limits,
            friends: aliceFriends);
        using var bobHost = new MeshNetworkHost(
            bobDirectory.Path,
            bobOptions,
            timeProvider: time,
            friends: bobFriends);

        await bobHost.StartAsync(CancellationToken.None);
        await bobHost.ActivateProfileAsync("bob-profile", "Bob");
        await aliceHost.StartAsync(CancellationToken.None);
        await aliceHost.ActivateProfileAsync("alice-profile", "Alice");
        try
        {
            using (var unknownDiscovery = new MeshDiscoveryService(malloryFriends, time))
            using (var aliceDiscovery = new MeshDiscoveryService(aliceFriends, time))
            using (var bobDiscovery = new MeshDiscoveryService(bobFriends, time))
            using (var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                var unknownCycle = await unknownDiscovery.CreateCycleAsync(
                    "mallory-profile",
                    12345,
                    CancellationToken.None);
                await sender.SendAsync(
                    Assert.Single(unknownCycle.Announcements),
                    new IPEndPoint(IPAddress.Loopback, aliceDiscoveryPort));
                var bobCycle = await bobDiscovery.CreateCycleAsync(
                    "bob-profile",
                    bobHost.TransportPort,
                    CancellationToken.None);
                await sender.SendAsync(
                    Assert.Single(bobCycle.Announcements),
                    new IPEndPoint(IPAddress.Loopback, aliceDiscoveryPort));
                var aliceCycle = await aliceDiscovery.CreateCycleAsync(
                    "alice-profile",
                    aliceHost.TransportPort,
                    CancellationToken.None);
                await sender.SendAsync(
                    Assert.Single(aliceCycle.Announcements),
                    new IPEndPoint(IPAddress.Loopback, bobDiscoveryPort));
            }
            await Task.Delay(TimeSpan.FromSeconds(2));

            await bobHost.ActivateProfileAsync("bob-profile", "Bob");
            await aliceHost.ActivateProfileAsync("alice-profile", "Alice");
            var presence = await aliceHost.WaitForPresenceAsync(
                "alice-profile",
                bob.PeerId,
                TimeSpan.FromSeconds(5));

            Assert.True(presence.IsOnline);
            Assert.Equal(alice.PeerId, (await bobFriends.GetFriendsAsync("bob-profile")).Single().PeerId);
        }
        finally
        {
            await aliceHost.StopAsync(CancellationToken.None);
            await bobHost.StopAsync(CancellationToken.None);
        }
    }

    private static MeshTransportOptions CreateOptions(int discoveryPort, int targetPort)
        => new()
        {
            DiscoveryPort = discoveryPort,
            EnableMulticast = false,
            EnableInternetDiscovery = false,
            DiscoveryTargets = [new IPEndPoint(IPAddress.Loopback, targetPort)],
            AnnouncementInterval = TimeSpan.FromMilliseconds(100),
            PresenceTimeout = TimeSpan.FromSeconds(2),
            EndpointLifetime = TimeSpan.FromSeconds(2)
        };

    private static async Task<T> WaitForAsync<T>(
        Func<T> read,
        Func<T, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var value = read();
            if (predicate(value))
                return value;
            await Task.Delay(25);
        }
        throw new TimeoutException("The expected Mesh state was not observed");
    }

    private static async Task<(MeshPublicIdentity Alice, MeshPublicIdentity Bob)> PairAsync(
        MeshFriendService aliceFriends,
        MeshFriendService bobFriends,
        string aliceProfile = "alice-profile",
        string bobProfile = "bob-profile",
        string aliceName = "Alice",
        string bobName = "Bob")
    {
        var alice = await aliceFriends.GetIdentityAsync(aliceProfile);
        var bob = await bobFriends.GetIdentityAsync(bobProfile);
        var invite = await aliceFriends.CreateInviteAsync(
            aliceProfile,
            aliceName,
            TimeSpan.FromMinutes(10));
        var acceptance = await bobFriends.AcceptInviteAsync(
            bobProfile,
            bobName,
            invite.Value.Token);
        var completion = await aliceFriends.CompleteInviteAsync(
            aliceProfile,
            acceptance.Value.AcceptanceToken);
        Assert.True(invite.IsSuccess);
        Assert.True(acceptance.IsSuccess);
        Assert.True(completion.IsSuccess);
        return (alice, bob);
    }

    private static int GetAvailableUdpPort()
        => TestNetworkPortAllocator.ReserveUdpPort();

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
