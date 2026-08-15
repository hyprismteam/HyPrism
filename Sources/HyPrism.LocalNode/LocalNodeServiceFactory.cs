// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Net.Sockets;
using HyPrism.Core;
using HyPrism.Core.Game.Launch;
using HyPrism.Core.Infrastructure;

namespace HyPrism.LocalNode;

/// <summary>
/// Creates independent Local Node processes with unique loopback ports and state directories.
/// </summary>
public sealed class LocalNodeServiceFactory : ILocalNodeServiceFactory
{
    private const int FirstPort = LocalNodeEndpoint.Port;
    private const int LastPort = 9999;
    private readonly AppPathConfiguration _appPath;
    private readonly LogSessionPaths _logSession;
    private readonly object _portLock = new();
    private int _nextPort = FirstPort;

    public LocalNodeServiceFactory(AppPathConfiguration appPath)
        : this(appPath, new LogSessionPaths(appPath))
    {
    }

    public LocalNodeServiceFactory(AppPathConfiguration appPath, LogSessionPaths logSession)
    {
        _appPath = appPath;
        _logSession = logSession;
    }

    /// <inheritdoc/>
    public ILocalNodeService Create()
    {
        var port = FindAvailablePort();
        var sessionDirectory = Path.Combine(
            _appPath.AppDir,
            "LocalNode",
            "Sessions",
            Guid.NewGuid().ToString("N"));
        var certificateDirectory = Path.Combine(_appPath.AppDir, "LocalNode", "Certificate");
        var accountDataDirectory = Path.Combine(_appPath.AppDir, "LocalNode");
        Directory.CreateDirectory(sessionDirectory);

        return new LocalNodeHost(new LocalNodeOptions(
            sessionDirectory,
            LocalNodeEndpoint.Hostname,
            port,
            CertificateDirectory: certificateDirectory,
            AccountDataDirectory: accountDataDirectory,
            LogFilePath: _logSession.GetLocalNodeLogPath(port),
            RequestJournalPath: _logSession.GetLocalNodeRequestJournalPath(port)));
    }

    private int FindAvailablePort()
    {
        lock (_portLock)
        {
            for (var attempt = 0; attempt <= LastPort - FirstPort; attempt++)
            {
                var candidate = _nextPort;
                _nextPort = candidate == LastPort ? FirstPort : candidate + 1;
                if (CanBind(candidate))
                    return candidate;
            }
        }

        throw new InvalidOperationException("No Local Node port is available in the 8443-9999 range");
    }

    private static bool CanBind(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
