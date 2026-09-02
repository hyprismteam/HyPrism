// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using HyPrism.Mesh;

namespace HyPrism.Mesh.Tests;

public sealed class MeshEnvelopeServiceTests
{
    [Fact]
    public async Task SealAndOpenAsync_ConfirmedFriends_ProtectsMessageEndToEnd()
    {
        using var directory = new TemporaryDirectory();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var friends = new MeshFriendService(directory.Path, time);
        var identities = await PairAsync(friends);
        var envelopes = new MeshEnvelopeService(friends, time);
        var payload = Encoding.UTF8.GetBytes("world-invite");

        var sealedResult = await envelopes.SealAsync(
            "alice-profile",
            identities.Bob.PeerId,
            MeshMessageKind.WorldInvite,
            payload);
        var openedResult = await envelopes.OpenAsync(
            "bob-profile",
            identities.Alice.PeerId,
            sealedResult.Value);

        Assert.True(sealedResult.IsSuccess);
        Assert.True(openedResult.IsSuccess);
        Assert.Equal(identities.Alice.PeerId, openedResult.Value.SenderPeerId);
        Assert.Equal(identities.Bob.PeerId, openedResult.Value.RecipientPeerId);
        Assert.Equal(MeshMessageKind.WorldInvite, openedResult.Value.Kind);
        Assert.Equal(payload, openedResult.Value.Payload.ToArray());
    }

    [Fact]
    public async Task OpenAsync_TamperedCiphertext_IsRejected()
    {
        using var directory = new TemporaryDirectory();
        var friends = new MeshFriendService(directory.Path);
        var identities = await PairAsync(friends);
        var envelopes = new MeshEnvelopeService(friends);
        var sealedResult = await envelopes.SealAsync(
            "alice-profile",
            identities.Bob.PeerId,
            MeshMessageKind.Presence,
            Encoding.UTF8.GetBytes("online"));
        sealedResult.Value[40] ^= 0x01;

        var openedResult = await envelopes.OpenAsync(
            "bob-profile",
            identities.Alice.PeerId,
            sealedResult.Value);

        Assert.False(openedResult.IsSuccess);
        Assert.Equal("invalid_signature", openedResult.Failure.Code);
    }

    [Fact]
    public async Task OpenAsync_ExpiredEnvelope_IsRejectedBeforeDecryption()
    {
        using var directory = new TemporaryDirectory();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var friends = new MeshFriendService(directory.Path, time);
        var identities = await PairAsync(friends);
        var envelopes = new MeshEnvelopeService(friends, time);
        var sealedResult = await envelopes.SealAsync(
            "alice-profile",
            identities.Bob.PeerId,
            MeshMessageKind.Presence,
            Encoding.UTF8.GetBytes("online"));
        time.Advance(TimeSpan.FromMinutes(3));

        var openedResult = await envelopes.OpenAsync(
            "bob-profile",
            identities.Alice.PeerId,
            sealedResult.Value);

        Assert.False(openedResult.IsSuccess);
        Assert.Equal("expired", openedResult.Failure.Code);
    }

    [Fact]
    public async Task InboundPipeline_DuplicateEnvelope_IsDeliveredOnce()
    {
        using var directory = new TemporaryDirectory();
        var friends = new MeshFriendService(directory.Path);
        var identities = await PairAsync(friends);
        var envelopes = new MeshEnvelopeService(friends);
        var sealedResult = await envelopes.SealAsync(
            "alice-profile",
            identities.Bob.PeerId,
            MeshMessageKind.Presence,
            Encoding.UTF8.GetBytes("online"));
        var pipeline = new MeshInboundPipeline(envelopes);

        Assert.True(pipeline.TryEnqueue(identities.Alice.PeerId, sealedResult.Value));
        Assert.True(pipeline.TryEnqueue(identities.Alice.PeerId, sealedResult.Value));
        pipeline.Complete();
        var delivered = new List<MeshMessage>();
        await foreach (var message in pipeline.ReadAllAsync("bob-profile"))
            delivered.Add(message);

        Assert.Single(delivered);
        Assert.Equal(1, pipeline.AcceptedCount);
        Assert.Equal(1, pipeline.RejectedCount);
        Assert.Equal(0, pipeline.DroppedCount);
    }

    [Fact]
    public async Task InboundPipeline_FullQueue_RejectsBeforeAllocatingAnotherSlot()
    {
        using var directory = new TemporaryDirectory();
        var limits = new MeshSecurityLimits { MaximumInboundQueueDepth = 1 };
        var friends = new MeshFriendService(directory.Path, limits: limits);
        var identities = await PairAsync(friends);
        var envelopes = new MeshEnvelopeService(friends, limits: limits);
        var sealedResult = await envelopes.SealAsync(
            "alice-profile",
            identities.Bob.PeerId,
            MeshMessageKind.Presence,
            Encoding.UTF8.GetBytes("online"));
        var pipeline = new MeshInboundPipeline(envelopes, limits: limits);

        Assert.True(pipeline.TryEnqueue(identities.Alice.PeerId, sealedResult.Value));
        Assert.False(pipeline.TryEnqueue(identities.Alice.PeerId, sealedResult.Value));
        Assert.Equal(1, pipeline.DroppedCount);
    }

    private static async Task<(MeshPublicIdentity Alice, MeshPublicIdentity Bob)> PairAsync(
        MeshFriendService friends)
    {
        var alice = await friends.GetIdentityAsync("alice-profile");
        var bob = await friends.GetIdentityAsync("bob-profile");
        var invitation = await friends.CreateInviteAsync(
            "alice-profile",
            "Alice",
            TimeSpan.FromMinutes(10));
        var acceptance = await friends.AcceptInviteAsync(
            "bob-profile",
            "Bob",
            invitation.Value.Token);
        var completion = await friends.CompleteInviteAsync(
            "alice-profile",
            acceptance.Value.AcceptanceToken);
        Assert.True(invitation.IsSuccess);
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
                "HyPrism-MeshEnvelopeTests",
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
