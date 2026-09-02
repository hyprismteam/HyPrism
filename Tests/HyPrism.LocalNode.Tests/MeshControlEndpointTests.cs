// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using HyPrism.Mesh;
using HyPrism.LocalNode;

namespace HyPrism.LocalNode.Tests;

public sealed class MeshControlEndpointTests
{
    [Fact]
    public async Task SocialEndpoints_ReturnAuthenticatedMeshFriendsInHytaleShape()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "HyPrism-MeshSocialEndpointTests",
            Guid.NewGuid().ToString("N"));
        const string aliceProfile = "550e8400-e29b-41d4-a716-446655440000";
        const string bobProfile = "660e8400-e29b-41d4-a716-446655440000";
        var meshFriends = new MeshFriendService(directory);
        var invite = await meshFriends.CreateInviteAsync(
            aliceProfile,
            "Alice",
            TimeSpan.FromMinutes(10));
        var acceptance = await meshFriends.AcceptInviteAsync(
            bobProfile,
            "Bob",
            invite.Value.Token);
        var completion = await meshFriends.CompleteInviteAsync(
            aliceProfile,
            acceptance.Value.AcceptanceToken);
        Assert.True(invite.IsSuccess);
        Assert.True(acceptance.IsSuccess);
        Assert.True(completion.IsSuccess);

        var options = new LocalNodeOptions(
            directory,
            "h.localhost",
            GetAvailablePort(),
            AccountDataDirectory: directory)
        {
            ConfigureSystemTrust = false
        };
        await using var host = new LocalNodeHost(options);

        try
        {
            await host.EnsureReadyAsync();
            var session = await host.CreateSessionAsync(aliceProfile, "Alice");
            using var client = CreatePinnedClient(options);

            using var unauthorized = await client.GetAsync("/friends");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                session.SessionToken);
            using var friendsResponse = await client.GetAsync("/friends");
            friendsResponse.EnsureSuccessStatusCode();
            using var friendsDocument = JsonDocument.Parse(
                await friendsResponse.Content.ReadAsStringAsync());
            var friend = Assert.Single(
                friendsDocument.RootElement.GetProperty("friends").EnumerateArray());
            Assert.Equal(bobProfile, friend.GetProperty("uuid").GetString());
            Assert.Equal("Bob", friend.GetProperty("username").GetString());
            Assert.False(friend.GetProperty("isOnline").GetBoolean());
            Assert.False(friend.GetProperty("canJoin").GetBoolean());
            Assert.False(friendsDocument.RootElement.GetProperty("truncated").GetBoolean());

            using var presenceResponse = await client.GetAsync("/presence/friends");
            presenceResponse.EnsureSuccessStatusCode();
            using var presenceDocument = JsonDocument.Parse(
                await presenceResponse.Content.ReadAsStringAsync());
            var presence = Assert.Single(
                presenceDocument.RootElement.GetProperty("friends").EnumerateArray());
            Assert.Equal(bobProfile, presence.GetProperty("playerUuid").GetString());
            Assert.Equal("Bob", presence.GetProperty("username").GetString());
            Assert.Equal("offline", presence.GetProperty("status").GetString());
            Assert.False(presence.GetProperty("canJoin").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PairingEndpoints_RequireControlSecretAndPersistFriendship()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "HyPrism-MeshEndpointTests",
            Guid.NewGuid().ToString("N"));
        const string controlSecret = "A6F99F2C6E41B1EBBFEB2705D69A0D499364163024060A7A3809D2E8022E1E9F";
        var options = new LocalNodeOptions(
            directory,
            "h.localhost",
            GetAvailablePort(),
            ControlSecret: controlSecret,
            AccountDataDirectory: directory)
        {
            ConfigureSystemTrust = false
        };
        await using var host = new LocalNodeHost(options);

        try
        {
            await host.EnsureReadyAsync();
            using var client = CreatePinnedClient(options);

            using var unauthorized = await client.GetAsync("/_hyprism/v1/mesh/profiles/alice/identity");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

            client.DefaultRequestHeaders.Add("X-HyPrism-Control", controlSecret);
            using var inviteResponse = await client.PostAsJsonAsync(
                "/_hyprism/v1/mesh/profiles/alice/invites",
                new { displayName = "Alice", lifetimeMinutes = 10 });
            inviteResponse.EnsureSuccessStatusCode();
            using var invite = JsonDocument.Parse(await inviteResponse.Content.ReadAsStringAsync());

            using var acceptResponse = await client.PostAsJsonAsync(
                "/_hyprism/v1/mesh/profiles/bob/accept",
                new
                {
                    displayName = "Bob",
                    inviteToken = invite.RootElement.GetProperty("token").GetString()
                });
            acceptResponse.EnsureSuccessStatusCode();
            using var acceptance = JsonDocument.Parse(await acceptResponse.Content.ReadAsStringAsync());

            using var completeResponse = await client.PostAsJsonAsync(
                "/_hyprism/v1/mesh/profiles/alice/complete",
                new
                {
                    acceptanceToken = acceptance.RootElement.GetProperty("acceptanceToken").GetString()
                });
            completeResponse.EnsureSuccessStatusCode();

            using var friendsResponse = await client.GetAsync("/_hyprism/v1/mesh/profiles/alice/friends");
            friendsResponse.EnsureSuccessStatusCode();
            using var friends = JsonDocument.Parse(await friendsResponse.Content.ReadAsStringAsync());
            var friend = Assert.Single(friends.RootElement.GetProperty("friends").EnumerateArray());
            Assert.Equal("Bob", friend.GetProperty("displayName").GetString());
            Assert.StartsWith("hp1_", friend.GetProperty("peerId").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static HttpClient CreatePinnedClient(LocalNodeOptions options)
    {
        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            LocalNodeCertificateStore.GetCertificatePath(options),
            password: null,
            X509KeyStorageFlags.Exportable);
        var expectedCertificate = certificate.RawData;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, presented, _, _) =>
                presented is not null && presented.RawData.AsSpan().SequenceEqual(expectedCertificate)
        };
        return new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://127.0.0.1:{options.Port}"),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    private static int GetAvailablePort()
        => TestNetworkPortAllocator.ReserveTcpPort();
}
