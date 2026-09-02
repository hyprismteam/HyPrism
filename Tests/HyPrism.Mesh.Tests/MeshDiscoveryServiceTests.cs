// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text;

namespace HyPrism.Mesh.Tests;

using HyPrism.Mesh;

public sealed class MeshDiscoveryServiceTests
{
    [Fact]
    public async Task VerifyAnnouncement_ConfirmedFriend_BindsEncryptedTransportPort()
    {
        using var directory = new TemporaryDirectory();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var friends = new MeshFriendService(directory.Path, time);
        var (alice, _) = await PairAsync(friends);
        using var aliceDiscovery = new MeshDiscoveryService(friends, time);
        using var bobDiscovery = new MeshDiscoveryService(friends, time);
        var aliceCycle = await aliceDiscovery.CreateCycleAsync("alice-profile", 45123);
        var bobCycle = await bobDiscovery.CreateCycleAsync("bob-profile", 45124);
        var packet = Assert.Single(aliceCycle.Announcements);
        var route = FindRoute(packet, bobCycle);

        var result = bobDiscovery.VerifyAnnouncement(packet, route);

        Assert.True(result.IsSuccess);
        Assert.Equal(alice.PeerId, result.Value.PeerId);
        Assert.Equal(45123, result.Value.TransportPort);
        Assert.Equal(time.GetUtcNow(), result.Value.IssuedAt);
        Assert.True(packet.AsSpan().IndexOf(Encoding.UTF8.GetBytes(alice.PeerId)) < 0);
    }

    [Fact]
    public async Task VerifyAnnouncement_TamperedCiphertext_IsRejected()
    {
        using var directory = new TemporaryDirectory();
        var friends = new MeshFriendService(directory.Path);
        await PairAsync(friends);
        using var aliceDiscovery = new MeshDiscoveryService(friends);
        using var bobDiscovery = new MeshDiscoveryService(friends);
        var aliceCycle = await aliceDiscovery.CreateCycleAsync("alice-profile", 45123);
        var bobCycle = await bobDiscovery.CreateCycleAsync("bob-profile", 45124);
        var packet = Assert.Single(aliceCycle.Announcements);
        var route = FindRoute(packet, bobCycle);
        packet[50] ^= 0x01;

        var result = bobDiscovery.VerifyAnnouncement(packet, route);

        Assert.False(result.IsSuccess);
        Assert.Equal("invalid_signature", result.Failure.Code);
    }

    [Fact]
    public async Task VerifyAnnouncement_ExpiredPacket_IsRejected()
    {
        using var directory = new TemporaryDirectory();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var friends = new MeshFriendService(directory.Path, time);
        await PairAsync(friends);
        using var aliceDiscovery = new MeshDiscoveryService(friends, time);
        using var bobDiscovery = new MeshDiscoveryService(friends, time);
        var aliceCycle = await aliceDiscovery.CreateCycleAsync("alice-profile", 45123);
        var bobCycle = await bobDiscovery.CreateCycleAsync("bob-profile", 45124);
        var packet = Assert.Single(aliceCycle.Announcements);
        var route = FindRoute(packet, bobCycle);
        time.Advance(TimeSpan.FromSeconds(31));

        var result = bobDiscovery.VerifyAnnouncement(packet, route);

        Assert.False(result.IsSuccess);
        Assert.Equal("expired", result.Failure.Code);
    }

    [Fact]
    public async Task CreateCycle_AfterRotation_ChangesOpaqueRoute()
    {
        using var directory = new TemporaryDirectory();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var friends = new MeshFriendService(directory.Path, time);
        await PairAsync(friends);
        using var discovery = new MeshDiscoveryService(friends, time);
        var first = await discovery.CreateCycleAsync("bob-profile", 45124);
        var firstPacket = Assert.Single(first.Announcements);
        Assert.True(MeshDiscoveryService.TryReadRoute(firstPacket, out var firstRoute));
        time.Advance(TimeSpan.FromSeconds(61));

        var second = await discovery.CreateCycleAsync("bob-profile", 45124);
        var secondPacket = Assert.Single(second.Announcements);
        Assert.True(MeshDiscoveryService.TryReadRoute(secondPacket, out var secondRoute));

        Assert.NotEqual(firstRoute, secondRoute);
    }

    [Fact]
    public async Task CreateCycle_MultipleFriends_UsesPersonalizedRoutes()
    {
        using var directory = new TemporaryDirectory();
        var friends = new MeshFriendService(directory.Path);
        await PairAsync(friends);
        await friends.GetIdentityAsync("charlie-profile");
        var invite = await friends.CreateInviteAsync(
            "alice-profile",
            "Alice",
            TimeSpan.FromMinutes(10));
        var acceptance = await friends.AcceptInviteAsync(
            "charlie-profile",
            "Charlie",
            invite.Value.Token);
        var completion = await friends.CompleteInviteAsync(
            "alice-profile",
            acceptance.Value.AcceptanceToken);
        Assert.True(completion.IsSuccess);

        using var aliceDiscovery = new MeshDiscoveryService(friends);
        using var bobDiscovery = new MeshDiscoveryService(friends);
        using var charlieDiscovery = new MeshDiscoveryService(friends);
        var aliceCycle = await aliceDiscovery.CreateCycleAsync("alice-profile", 45123);
        var bobCycle = await bobDiscovery.CreateCycleAsync("bob-profile", 45124);
        var charlieCycle = await charlieDiscovery.CreateCycleAsync("charlie-profile", 45125);
        var packetRoutes = aliceCycle.Announcements.Select(packet =>
        {
            Assert.True(MeshDiscoveryService.TryReadRoute(packet, out var route));
            return route;
        }).ToArray();

        Assert.Equal(2, packetRoutes.Length);
        Assert.Single(packetRoutes, key => bobCycle.InboundRoutes.Any(route => route.Key == key));
        Assert.Single(packetRoutes, key => charlieCycle.InboundRoutes.Any(route => route.Key == key));
        Assert.NotEqual(packetRoutes[0], packetRoutes[1]);
    }

    private static MeshDiscoveryRoute FindRoute(byte[] packet, MeshDiscoveryCycle cycle)
    {
        Assert.True(MeshDiscoveryService.TryReadRoute(packet, out var routeKey));
        return Assert.Single(cycle.InboundRoutes, route => route.Key == routeKey);
    }

    private static async Task<(MeshPublicIdentity Alice, MeshPublicIdentity Bob)> PairAsync(
        MeshFriendService friends)
    {
        var alice = await friends.GetIdentityAsync("alice-profile");
        var bob = await friends.GetIdentityAsync("bob-profile");
        var invite = await friends.CreateInviteAsync(
            "alice-profile",
            "Alice",
            TimeSpan.FromMinutes(10));
        var acceptance = await friends.AcceptInviteAsync(
            "bob-profile",
            "Bob",
            invite.Value.Token);
        var completion = await friends.CompleteInviteAsync(
            "alice-profile",
            acceptance.Value.AcceptanceToken);
        Assert.True(invite.IsSuccess);
        Assert.True(acceptance.IsSuccess);
        Assert.True(completion.IsSuccess);
        return (alice, bob);
    }

    private sealed class MutableTimeProvider(DateTimeOffset value) : TimeProvider
    {
        private DateTimeOffset _value = value;

        public override DateTimeOffset GetUtcNow() => _value;
        public void Advance(TimeSpan value) => _value = _value.Add(value);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "HyPrism-MeshDiscoveryTests",
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
