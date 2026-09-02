// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using HyPrism.LocalNode;

namespace HyPrism.LocalNode.Tests;

public sealed class MeshControlEndpointTests
{
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
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
