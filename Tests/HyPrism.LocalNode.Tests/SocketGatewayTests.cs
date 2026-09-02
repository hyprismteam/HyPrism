// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using HyPrism.Mesh;

namespace HyPrism.LocalNode.Tests;

public sealed class SocketGatewayTests
{
    [Fact]
    public async Task PeerSession_ExchangesOpaquePayloadAcrossMesh()
    {
        using var root = new TemporaryDirectory();
        var aliceUuid = "550e8400-e29b-41d4-a716-446655440001";
        var bobUuid = "550e8400-e29b-41d4-a716-446655440002";
        var aliceDirectory = Path.Combine(root.Path, "Alice");
        var bobDirectory = Path.Combine(root.Path, "Bob");
        var aliceFriends = new MeshFriendService(aliceDirectory);
        var bobFriends = new MeshFriendService(bobDirectory);
        var (aliceIdentity, bobIdentity) = await PairAsync(
            aliceFriends,
            bobFriends,
            aliceUuid,
            bobUuid);
        var aliceDiscoveryPort = GetAvailableUdpPort();
        var bobDiscoveryPort = GetAvailableUdpPort();
        var aliceOptions = CreateNodeOptions(
            aliceDirectory,
            GetAvailableTcpPort(),
            aliceDiscoveryPort,
            bobDiscoveryPort);
        var bobOptions = CreateNodeOptions(
            bobDirectory,
            GetAvailableTcpPort(),
            bobDiscoveryPort,
            aliceDiscoveryPort);
        var aliceSessions = new LocalSessionRegistry(aliceOptions.Issuer);
        var bobSessions = new LocalSessionRegistry(bobOptions.Issuer);
        var aliceSession = aliceSessions.Renew(aliceUuid, "Alice");
        var bobSession = bobSessions.Renew(bobUuid, "Bob");
        using var aliceCertificate = LocalNodeCertificateStore.LoadOrCreate(aliceOptions);
        using var bobCertificate = LocalNodeCertificateStore.LoadOrCreate(bobOptions);
        using var aliceMesh = new MeshNetworkHost(
            aliceDirectory,
            aliceOptions.MeshTransport,
            friends: aliceFriends);
        using var bobMesh = new MeshNetworkHost(
            bobDirectory,
            bobOptions.MeshTransport,
            friends: bobFriends);
        await using var aliceApp = LocalNodeApplication.Build(
            aliceOptions,
            aliceCertificate,
            aliceSessions,
            meshFriends: aliceFriends,
            meshNetwork: aliceMesh);
        await using var bobApp = LocalNodeApplication.Build(
            bobOptions,
            bobCertificate,
            bobSessions,
            meshFriends: bobFriends,
            meshNetwork: bobMesh);

        await aliceApp.StartAsync();
        await bobApp.StartAsync();
        try
        {
            using (var unauthorizedSocket = CreateSocket(aliceCertificate, "invalid-token"))
            {
                await Assert.ThrowsAsync<WebSocketException>(() => unauthorizedSocket.ConnectAsync(
                    new Uri($"wss://127.0.0.1:{aliceOptions.Port}/ws"),
                    CancellationToken.None));
            }

            using var aliceSocket = CreateSocket(aliceCertificate, aliceSession.SessionToken);
            using var bobSocket = CreateSocket(bobCertificate, bobSession.SessionToken);
            await aliceSocket.ConnectAsync(
                new Uri($"wss://127.0.0.1:{aliceOptions.Port}/ws"),
                CancellationToken.None);
            await bobSocket.ConnectAsync(
                new Uri($"wss://127.0.0.1:{bobOptions.Port}/ws"),
                CancellationToken.None);

            using var aliceConnected = await ReceiveJsonAsync(aliceSocket);
            using var bobConnected = await ReceiveJsonAsync(bobSocket);
            Assert.Equal("gateway.connected", ReadType(aliceConnected));
            Assert.Equal("gateway.connected", ReadType(bobConnected));

            await Task.WhenAll(
                aliceMesh.WaitForPresenceAsync(aliceUuid, bobIdentity.PeerId, TimeSpan.FromSeconds(5)),
                bobMesh.WaitForPresenceAsync(bobUuid, aliceIdentity.PeerId, TimeSpan.FromSeconds(5)));

            using var aliceHttp = CreateHttpClient(aliceOptions, aliceCertificate, aliceSession.SessionToken);
            using var bobHttp = CreateHttpClient(bobOptions, bobCertificate, bobSession.SessionToken);
            var acceptedInvite = await SendWorldInviteAsync(aliceHttp, bobUuid, "p2p-accepted");
            using var receivedNotification = await ReceiveJsonAsync(bobSocket);
            AssertWorldNotification(receivedNotification, "world.invite.received", acceptedInvite);
            Assert.Equal(acceptedInvite, await GetOnlyInviteUuidAsync(bobHttp, "/world-invites"));
            Assert.Equal(acceptedInvite, await GetOnlyInviteUuidAsync(aliceHttp, "/world-invites/sent"));
            Assert.True(await GetOnlyFriendCanJoinAsync(bobHttp));

            using (var joinResponse = await bobHttp.PostAsJsonAsync("/presence/join-world", new
            {
                player_uuid = aliceUuid
            }))
            {
                joinResponse.EnsureSuccessStatusCode();
                using var join = JsonDocument.Parse(await joinResponse.Content.ReadAsStringAsync());
                Assert.Equal("p2p-accepted", join.RootElement.GetProperty("invite_code").GetString());
                Assert.True(join.RootElement.GetProperty("is_p2p").GetBoolean());
            }

            using (var acceptResponse = await bobHttp.PostAsJsonAsync("/world-invites/accept", new
            {
                invite_uuid = acceptedInvite
            }))
            {
                acceptResponse.EnsureSuccessStatusCode();
                using var join = JsonDocument.Parse(await acceptResponse.Content.ReadAsStringAsync());
                Assert.Equal("p2p-accepted", join.RootElement.GetProperty("invite_code").GetString());
            }
            using var acceptedNotification = await ReceiveJsonAsync(aliceSocket);
            AssertWorldNotification(acceptedNotification, "world.invite.accepted", acceptedInvite);
            Assert.Null(await GetOnlyInviteUuidAsync(bobHttp, "/world-invites"));
            Assert.Null(await GetOnlyInviteUuidAsync(aliceHttp, "/world-invites/sent"));
            Assert.False(await GetOnlyFriendCanJoinAsync(bobHttp));

            var rejectedInvite = await SendWorldInviteAsync(aliceHttp, bobUuid, "p2p-rejected");
            using var rejectedReceived = await ReceiveJsonAsync(bobSocket);
            AssertWorldNotification(rejectedReceived, "world.invite.received", rejectedInvite);
            using (var rejectResponse = await bobHttp.PostAsJsonAsync("/world-invites/reject", new
            {
                invite_uuid = rejectedInvite
            }))
            {
                Assert.Equal(HttpStatusCode.NoContent, rejectResponse.StatusCode);
            }
            using var rejectedNotification = await ReceiveJsonAsync(aliceSocket);
            AssertWorldNotification(rejectedNotification, "world.invite.rejected", rejectedInvite);

            var canceledInvite = await SendWorldInviteAsync(aliceHttp, bobUuid, "p2p-canceled");
            using var canceledReceived = await ReceiveJsonAsync(bobSocket);
            AssertWorldNotification(canceledReceived, "world.invite.received", canceledInvite);
            using (var cancelResponse = await aliceHttp.PostAsJsonAsync("/world-invites/cancel", new
            {
                invite_uuid = canceledInvite
            }))
            {
                Assert.Equal(HttpStatusCode.NoContent, cancelResponse.StatusCode);
            }
            using var canceledNotification = await ReceiveJsonAsync(bobSocket);
            AssertWorldNotification(canceledNotification, "world.invite.canceled", canceledInvite);
            Assert.Null(await GetOnlyInviteUuidAsync(bobHttp, "/world-invites"));

            await SendJsonAsync(aliceSocket, new
            {
                type = "peer.session.open",
                data = new
                {
                    peer_uuid = bobUuid,
                    kind = "hytale-p2p",
                    client_ref = "open-1"
                }
            });
            using var bobOpened = await ReceiveJsonAsync(bobSocket);
            using var aliceAcknowledged = await ReceiveJsonAsync(aliceSocket);
            Assert.Equal("peer.session.opened", ReadType(bobOpened));
            Assert.Equal(aliceUuid, ReadData(bobOpened).GetProperty("from_uuid").GetString());
            Assert.Equal("peer.session.ack", ReadType(aliceAcknowledged));
            var acknowledgement = ReadData(aliceAcknowledged);
            Assert.True(acknowledgement.GetProperty("peer_online").GetBoolean());
            Assert.Equal("open-1", acknowledgement.GetProperty("client_ref").GetString());
            var sessionId = acknowledgement.GetProperty("session_id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(sessionId));
            Assert.Equal(sessionId, ReadData(bobOpened).GetProperty("session_id").GetString());

            await SendJsonAsync(aliceSocket, new
            {
                type = "peer.send",
                data = new
                {
                    session_id = sessionId,
                    seq = 7,
                    payload = new { session = "offer", candidate = "opaque" }
                }
            });
            using var bobMessage = await ReceiveJsonAsync(bobSocket);
            Assert.Equal("peer.message", ReadType(bobMessage));
            var forwarded = ReadData(bobMessage);
            Assert.Equal(7, forwarded.GetProperty("seq").GetInt64());
            Assert.Equal("offer", forwarded.GetProperty("payload").GetProperty("session").GetString());
            Assert.Equal(aliceUuid, forwarded.GetProperty("from_uuid").GetString());

            await SendJsonAsync(aliceSocket, new
            {
                type = "peer.session.close",
                data = new { session_id = sessionId }
            });
            using var bobClosed = await ReceiveJsonAsync(bobSocket);
            Assert.Equal("peer.session.closed", ReadType(bobClosed));
            Assert.Equal(sessionId, ReadData(bobClosed).GetProperty("session_id").GetString());
        }
        finally
        {
            await bobApp.StopAsync();
            await aliceApp.StopAsync();
        }
    }

    private static LocalNodeOptions CreateNodeOptions(
        string dataDirectory,
        int port,
        int discoveryPort,
        int targetPort)
        => new(dataDirectory, "h.localhost", port)
        {
            ConfigureSystemTrust = false,
            MeshTransport = new MeshTransportOptions
            {
                DiscoveryPort = discoveryPort,
                EnableMulticast = false,
                DiscoveryTargets = [new IPEndPoint(IPAddress.Loopback, targetPort)],
                AnnouncementInterval = TimeSpan.FromMilliseconds(100),
                PresenceTimeout = TimeSpan.FromSeconds(5),
                EndpointLifetime = TimeSpan.FromSeconds(5)
            }
        };

    private static async Task<(MeshPublicIdentity Alice, MeshPublicIdentity Bob)> PairAsync(
        MeshFriendService aliceFriends,
        MeshFriendService bobFriends,
        string aliceUuid,
        string bobUuid)
    {
        var alice = await aliceFriends.GetIdentityAsync(aliceUuid);
        var bob = await bobFriends.GetIdentityAsync(bobUuid);
        var invite = await aliceFriends.CreateInviteAsync(
            aliceUuid,
            "Alice",
            TimeSpan.FromMinutes(10));
        var acceptance = await bobFriends.AcceptInviteAsync(
            bobUuid,
            "Bob",
            invite.Value.Token);
        var completion = await aliceFriends.CompleteInviteAsync(
            aliceUuid,
            acceptance.Value.AcceptanceToken);
        Assert.True(invite.IsSuccess);
        Assert.True(acceptance.IsSuccess);
        Assert.True(completion.IsSuccess);
        return (alice, bob);
    }

    private static ClientWebSocket CreateSocket(X509Certificate2 certificate, string token)
    {
        var expectedCertificate = certificate.RawData;
        var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = (_, presented, _, _) =>
            presented is not null && presented.GetRawCertData().AsSpan().SequenceEqual(expectedCertificate);
        socket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
        return socket;
    }

    private static HttpClient CreateHttpClient(
        LocalNodeOptions options,
        X509Certificate2 certificate,
        string token)
    {
        var expectedCertificate = certificate.RawData;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, presented, _, _) =>
                presented is not null && presented.RawData.AsSpan().SequenceEqual(expectedCertificate)
        };
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://127.0.0.1:{options.Port}"),
            Timeout = TimeSpan.FromSeconds(8)
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> SendWorldInviteAsync(
        HttpClient client,
        string playerUuid,
        string inviteCode)
    {
        using var response = await client.PostAsJsonAsync("/world-invite", new
        {
            player_uuid = playerUuid,
            invite_code = inviteCode
        });
        response.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return result.RootElement.GetProperty("invite_uuid").GetGuid();
    }

    private static async Task<Guid?> GetOnlyInviteUuidAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var invites = result.RootElement.GetProperty("invites").EnumerateArray().ToArray();
        return invites.Length == 0 ? null : Assert.Single(invites).GetProperty("invite_uuid").GetGuid();
    }

    private static async Task<bool> GetOnlyFriendCanJoinAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/friends");
        response.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return Assert.Single(result.RootElement.GetProperty("friends").EnumerateArray())
            .GetProperty("canJoin")
            .GetBoolean();
    }

    private static void AssertWorldNotification(JsonDocument message, string type, Guid inviteUuid)
    {
        Assert.Equal("gateway.notification", ReadType(message));
        var notification = ReadData(message);
        Assert.Equal(type, notification.GetProperty("type").GetString());
        Assert.Equal(
            inviteUuid,
            notification.GetProperty("data").GetProperty("invite_uuid").GetGuid());
    }

    private static async Task SendJsonAsync(ClientWebSocket socket, object value)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value);
        await socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<JsonDocument> ReceiveJsonAsync(ClientWebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        ValueWebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(chunk.AsMemory(), timeout.Token);
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            buffer.Write(chunk, 0, result.Count);
        }
        while (!result.EndOfMessage);
        return JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)));
    }

    private static string? ReadType(JsonDocument document)
        => document.RootElement.GetProperty("type").GetString();

    private static JsonElement ReadData(JsonDocument document)
        => document.RootElement.GetProperty("data");

    private static int GetAvailableTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static int GetAvailableUdpPort()
    {
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)client.Client.LocalEndPoint!).Port;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "HyPrism-SocketGatewayTests",
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
