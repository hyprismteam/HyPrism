// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Net.Sockets;

namespace HyPrism.LocalNode.Tests;

internal static class TestNetworkPortAllocator
{
    private static readonly object Gate = new();
    private static readonly HashSet<int> TcpPorts = [];
    private static readonly HashSet<int> UdpPorts = [];

    public static int ReserveTcpPort()
    {
        lock (Gate)
        {
            for (var attempt = 0; attempt < 128; attempt++)
            {
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                if (TcpPorts.Add(port))
                    return port;
            }
        }
        throw new InvalidOperationException("Could not reserve a unique TCP test port");
    }

    public static int ReserveUdpPort()
    {
        lock (Gate)
        {
            for (var attempt = 0; attempt < 128; attempt++)
            {
                using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                var port = ((IPEndPoint)client.Client.LocalEndPoint!).Port;
                if (UdpPorts.Add(port))
                    return port;
            }
        }
        throw new InvalidOperationException("Could not reserve a unique UDP test port");
    }
}
