// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HyPrism.LocalNode.Tests;

public sealed class MainlineDhtPeerLocatorTests
{
    [Fact]
    public async Task LookupAndAnnounce_UsesCompactPeerAndReturnedToken()
    {
        using var bootstrap = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var transport = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var bootstrapPort = ((IPEndPoint)bootstrap.Client.LocalEndPoint!).Port;
        var options = new MeshTransportOptions
        {
            DhtBootstrapHosts = ["127.0.0.1"],
            DhtBootstrapPort = bootstrapPort
        };
        var locator = new MainlineDhtPeerLocator(options);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var receive = ReceiveResponsesAsync(locator, transport, cancellation.Token);
        var server = ServeDhtAsync(bootstrap, cancellation.Token);

        var peers = await locator.LookupAndAnnounceAsync(
            "0123456789ABCDEF",
            ((IPEndPoint)transport.Client.LocalEndPoint!).Port,
            async (packet, endpoint, token) =>
            {
                await transport.SendAsync(packet, endpoint, token);
            },
            cancellation.Token);

        var peer = Assert.Single(peers);
        Assert.Equal(IPAddress.Parse("8.8.8.8"), peer.Address);
        Assert.Equal(45678, peer.Port);
        await server;
        await cancellation.CancelAsync();
        await receive;
    }

    private static async Task ServeDhtAsync(UdpClient server, CancellationToken cancellationToken)
    {
        var getPeers = await server.ReceiveAsync(cancellationToken);
        Assert.Contains("get_peers", Encoding.ASCII.GetString(getPeers.Buffer));
        var transaction = ReadTransaction(getPeers.Buffer);
        var compactPeer = new byte[6];
        IPAddress.Parse("8.8.8.8").GetAddressBytes().CopyTo(compactPeer, 0);
        BinaryPrimitives.WriteUInt16BigEndian(compactPeer.AsSpan(4), 45678);
        var response = Combine(
            "d1:rd2:id20:"u8.ToArray(),
            new byte[20],
            "5:token3:tok6:valuesl6:"u8.ToArray(),
            compactPeer,
            "ee1:t4:"u8.ToArray(),
            transaction,
            "1:y1:re"u8.ToArray());
        await server.SendAsync(response, getPeers.RemoteEndPoint, cancellationToken);

        var announce = await server.ReceiveAsync(cancellationToken);
        Assert.Contains("announce_peer", Encoding.ASCII.GetString(announce.Buffer));
        Assert.Contains("3:tok", Encoding.ASCII.GetString(announce.Buffer));
        transaction = ReadTransaction(announce.Buffer);
        response = Combine(
            "d1:rd2:id20:"u8.ToArray(),
            new byte[20],
            "e1:t4:"u8.ToArray(),
            transaction,
            "1:y1:re"u8.ToArray());
        await server.SendAsync(response, announce.RemoteEndPoint, cancellationToken);
    }

    private static async Task ReceiveResponsesAsync(
        MainlineDhtPeerLocator locator,
        UdpClient transport,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var response = await transport.ReceiveAsync(cancellationToken);
                locator.TryHandleResponse(response.Buffer, response.RemoteEndPoint);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
    }

    private static byte[] ReadTransaction(byte[] packet)
    {
        var marker = "1:t4:"u8;
        var offset = packet.AsSpan().IndexOf(marker);
        Assert.True(offset >= 0);
        return packet.AsSpan(offset + marker.Length, 4).ToArray();
    }

    private static byte[] Combine(params byte[][] parts)
    {
        var result = new byte[parts.Sum(part => part.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }
        return result;
    }
}
