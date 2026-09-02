// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Mesh;
using System.Text.Json.Nodes;

namespace HyPrism.Mesh.Tests;

public sealed class MeshFriendServiceTests
{
    [Fact]
    public async Task Identity_ExposesStableCrockfordFriendId()
    {
        using var directory = new TemporaryDirectory();
        var service = new MeshFriendService(directory.Path);

        var first = await service.GetIdentityAsync("alice-profile");
        var second = await service.GetIdentityAsync("alice-profile");

        Assert.Equal(first.FriendId, second.FriendId);
        Assert.Equal(MeshFriendId.Length, first.FriendId.Length);
        Assert.True(MeshFriendId.TryNormalize(
            $"{first.FriendId[..4]}-{first.FriendId[4..8]}-{first.FriendId[8..12]}-{first.FriendId[12..]}",
            out var normalized));
        Assert.Equal(first.FriendId, normalized);
    }

    [Fact]
    public async Task PeerRecord_IsSignedAndBoundToRequestId()
    {
        using var directory = new TemporaryDirectory();
        var service = new MeshFriendService(directory.Path);
        var identity = await service.GetIdentityAsync("alice-profile");

        var created = await service.CreatePeerRecordAsync(
            "alice-profile",
            "Alice",
            "request_1",
            "ack",
            TimeSpan.FromMinutes(2));
        var verified = service.VerifyPeerRecord(created.Value.Token, "request_1", "ack");
        var wrongRequest = service.VerifyPeerRecord(created.Value.Token, "request_2", "ack");
        var wrongPurpose = service.VerifyPeerRecord(created.Value.Token, "request_1", "reject");

        Assert.True(created.IsSuccess);
        Assert.True(verified.IsSuccess);
        Assert.Equal(identity.FriendId, verified.Value.FriendId);
        Assert.Equal("Alice", verified.Value.DisplayName);
        Assert.False(wrongRequest.IsSuccess);
        Assert.False(wrongPurpose.IsSuccess);
    }

    [Fact]
    public async Task TargetedInvite_CannotBeAcceptedByAnotherFriendId()
    {
        using var directory = new TemporaryDirectory();
        var service = new MeshFriendService(directory.Path);
        var bob = await service.GetIdentityAsync("bob-profile");
        var invitation = await service.CreateInviteForFriendIdAsync(
            "alice-profile",
            "Alice",
            bob.FriendId,
            TimeSpan.FromMinutes(10));

        var wrongRecipient = await service.AcceptInviteAsync(
            "mallory-profile",
            "Mallory",
            invitation.Value.Token);
        var accepted = await service.AcceptInviteAsync(
            "bob-profile",
            "Bob",
            invitation.Value.Token);

        Assert.False(wrongRecipient.IsSuccess);
        Assert.Equal("wrong_recipient", wrongRecipient.Failure.Code);
        Assert.True(accepted.IsSuccess);
    }
    [Fact]
    public async Task GetIdentityAsync_PersistsStableIdentityPerProfile()
    {
        using var directory = new TemporaryDirectory();
        var firstService = new MeshFriendService(directory.Path);

        var first = await firstService.GetIdentityAsync("alice-profile");
        var reloaded = await new MeshFriendService(directory.Path).GetIdentityAsync("alice-profile");
        var other = await firstService.GetIdentityAsync("bob-profile");

        Assert.Equal(first, reloaded);
        Assert.NotEqual(first.PeerId, other.PeerId);
        Assert.StartsWith("hp1_", first.PeerId, StringComparison.Ordinal);
        Assert.NotEmpty(first.SigningPublicKey);
        Assert.NotEmpty(first.AgreementPublicKey);
    }

    [Fact]
    public async Task GetIdentityAsync_VersionOneIdentity_AddsAgreementKeyWithoutChangingPeerId()
    {
        using var directory = new TemporaryDirectory();
        var service = new MeshFriendService(directory.Path);
        var original = await service.GetIdentityAsync("alice-profile");
        var identityPath = Assert.Single(Directory.EnumerateFiles(
            System.IO.Path.Combine(directory.Path, "Mesh", "Identities"),
            "*.json"));
        var document = JsonNode.Parse(await File.ReadAllTextAsync(identityPath))!.AsObject();
        document["version"] = 1;
        document.Remove("agreementPublicKey");
        document.Remove("agreementPrivateKey");
        await File.WriteAllTextAsync(identityPath, document.ToJsonString());

        var migrated = await new MeshFriendService(directory.Path).GetIdentityAsync("alice-profile");

        Assert.Equal(original.PeerId, migrated.PeerId);
        Assert.Equal(original.SigningPublicKey, migrated.SigningPublicKey);
        Assert.NotEmpty(migrated.AgreementPublicKey);
        var migratedDocument = JsonNode.Parse(await File.ReadAllTextAsync(identityPath))!.AsObject();
        Assert.Equal(2, migratedDocument["version"]!.GetValue<int>());
        Assert.NotNull(migratedDocument["agreementPrivateKey"]);
    }

    [Fact]
    public async Task FriendshipRoundTrip_AddsSignedFriendToBothProfiles()
    {
        using var directory = new TemporaryDirectory();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var service = new MeshFriendService(directory.Path, time);
        const string aliceProfile = "550e8400-e29b-41d4-a716-446655440000";
        const string bobProfile = "660e8400-e29b-41d4-a716-446655440000";
        var aliceIdentity = await service.GetIdentityAsync(aliceProfile);
        var bobIdentity = await service.GetIdentityAsync(bobProfile);

        var invitation = await service.CreateInviteAsync(
            aliceProfile,
            "Alice",
            TimeSpan.FromMinutes(10));
        var acceptance = await service.AcceptInviteAsync(
            bobProfile,
            "Bob",
            invitation.Value.Token);
        var completion = await service.CompleteInviteAsync(
            aliceProfile,
            acceptance.Value.AcceptanceToken);

        Assert.True(invitation.IsSuccess);
        Assert.True(acceptance.IsSuccess);
        Assert.True(completion.IsSuccess);
        Assert.Equal(bobIdentity.PeerId, completion.Value.PeerId);

        var aliceFriends = await new MeshFriendService(directory.Path, time).GetFriendsAsync(aliceProfile);
        var bobFriends = await new MeshFriendService(directory.Path, time).GetFriendsAsync(bobProfile);
        var aliceFriend = Assert.Single(aliceFriends);
        var bobFriend = Assert.Single(bobFriends);
        Assert.Equal(bobIdentity.PeerId, aliceFriend.PeerId);
        Assert.Equal(bobProfile, aliceFriend.PlayerUuid);
        Assert.Equal(aliceIdentity.PeerId, bobFriend.PeerId);
        Assert.Equal(aliceProfile, bobFriend.PlayerUuid);
    }

    [Fact]
    public async Task AcceptInviteAsync_TamperedPayload_IsRejected()
    {
        using var directory = new TemporaryDirectory();
        var service = new MeshFriendService(directory.Path);
        var invitation = await service.CreateInviteAsync(
            "alice-profile",
            "Alice",
            TimeSpan.FromMinutes(10));
        var token = invitation.Value.Token;
        var payloadStart = token.LastIndexOf('/') + 1;
        var replacement = token[payloadStart] == 'A' ? 'B' : 'A';
        var tampered = token[..payloadStart] + replacement + token[(payloadStart + 1)..];

        var result = await service.AcceptInviteAsync("bob-profile", "Bob", tampered);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Failure.Code, new[] { "invalid_invite", "invalid_signature" });
        Assert.Empty(await service.GetFriendsAsync("bob-profile"));
    }

    [Fact]
    public async Task AcceptInviteAsync_ExpiredInvite_IsRejected()
    {
        using var directory = new TemporaryDirectory();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var service = new MeshFriendService(directory.Path, time);
        var invitation = await service.CreateInviteAsync(
            "alice-profile",
            "Alice",
            TimeSpan.FromMinutes(1));
        time.Advance(TimeSpan.FromMinutes(2));

        var result = await service.AcceptInviteAsync("bob-profile", "Bob", invitation.Value.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal("expired", result.Failure.Code);
    }

    [Fact]
    public async Task AcceptInviteAsync_ReplayedInvite_IsRejected()
    {
        using var directory = new TemporaryDirectory();
        var service = new MeshFriendService(directory.Path);
        var invitation = await service.CreateInviteAsync(
            "alice-profile",
            "Alice",
            TimeSpan.FromMinutes(10));
        var first = await service.AcceptInviteAsync("bob-profile", "Bob", invitation.Value.Token);

        var replay = await service.AcceptInviteAsync("bob-profile", "Bob", invitation.Value.Token);

        Assert.True(first.IsSuccess);
        Assert.False(replay.IsSuccess);
        Assert.Equal("replayed_invite", replay.Failure.Code);
    }

    [Fact]
    public async Task CompleteInviteAsync_WrongRecipient_IsRejected()
    {
        using var directory = new TemporaryDirectory();
        var service = new MeshFriendService(directory.Path);
        var invitation = await service.CreateInviteAsync(
            "alice-profile",
            "Alice",
            TimeSpan.FromMinutes(10));
        var acceptance = await service.AcceptInviteAsync(
            "bob-profile",
            "Bob",
            invitation.Value.Token);

        var result = await service.CompleteInviteAsync(
            "mallory-profile",
            acceptance.Value.AcceptanceToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("wrong_recipient", result.Failure.Code);
        Assert.Empty(await service.GetFriendsAsync("mallory-profile"));
    }

    [Fact]
    public async Task CreateInviteAsync_ActiveInviteLimit_IsEnforced()
    {
        using var directory = new TemporaryDirectory();
        var limits = new MeshSecurityLimits { MaximumIssuedInvitesPerProfile = 1 };
        var service = new MeshFriendService(directory.Path, limits: limits);
        var first = await service.CreateInviteAsync(
            "alice-profile",
            "Alice",
            TimeSpan.FromMinutes(10));

        var second = await service.CreateInviteAsync(
            "alice-profile",
            "Alice",
            TimeSpan.FromMinutes(10));

        Assert.True(first.IsSuccess);
        Assert.False(second.IsSuccess);
        Assert.Equal("invite_limit_reached", second.Failure.Code);
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
                "HyPrism-MeshTests",
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
