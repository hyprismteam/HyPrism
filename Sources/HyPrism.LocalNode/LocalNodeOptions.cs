// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Text.Json;
using HyPrism.Core.Game.Launch;

namespace HyPrism.LocalNode;

/// <summary>
/// Defines the loopback endpoint and storage used by a Local Node instance
/// </summary>
public sealed record LocalNodeOptions(
    string DataDirectory,
    string Hostname,
    int Port,
    string? AssetsPath = null,
    int? OwnerProcessId = null,
    string? ControlSecret = null,
    string? CertificateDirectory = null,
    string? AccountDataDirectory = null,
    string? LogFilePath = null,
    string? RequestJournalPath = null)
{
    /// <summary>
    /// Gets whether Local Node may update platform certificate trust settings
    /// </summary>
    public bool ConfigureSystemTrust { get; init; } = true;

    /// <summary>
    /// Gets local-network discovery and transport settings for the mesh host
    /// </summary>
    public MeshTransportOptions MeshTransport { get; init; } = new();

    /// <summary>
    /// Gets the canonical issuer URI emitted in OmniAuth tokens
    /// </summary>
    public string Issuer => $"https://{Hostname}:{Port}";

    /// <summary>
    /// Parses Local Node command line arguments
    /// </summary>
    /// <param name="args">Command line arguments supplied by the launcher or user</param>
    /// <returns>Validated Local Node options</returns>
    /// <exception cref="ArgumentException">Thrown when an argument is missing, duplicated, or invalid</exception>
    public static LocalNodeOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
            {
                throw new ArgumentException("Expected command line arguments in --name value pairs", nameof(args));
            }

            var name = args[index][2..];
            if (!values.TryAdd(name, args[index + 1]))
            {
                throw new ArgumentException($"Argument '--{name}' was specified more than once", nameof(args));
            }
        }

        var dataDirectory = values.TryGetValue("data-directory", out var configuredDirectory)
            ? configuredDirectory
            : Path.Combine(AppContext.BaseDirectory, "LocalNodeData");
        var hostname = values.TryGetValue("hostname", out var configuredHostname)
            ? configuredHostname
            : LocalNodeEndpoint.Hostname;
        var port = values.TryGetValue("port", out var configuredPort) && int.TryParse(configuredPort, out var parsedPort)
            ? parsedPort
            : LocalNodeEndpoint.Port;
        var assetsPath = values.TryGetValue("assets-path", out var configuredAssetsPath)
            ? Path.GetFullPath(configuredAssetsPath)
            : null;
        int? ownerProcessId = values.TryGetValue("owner-pid", out var configuredOwnerProcessId)
                              && int.TryParse(configuredOwnerProcessId, out var parsedOwnerProcessId)
            ? parsedOwnerProcessId
            : null;
        var controlSecret = values.TryGetValue("control-secret", out var configuredControlSecret)
            ? configuredControlSecret
            : null;
        var certificateDirectory = values.TryGetValue("certificate-directory", out var configuredCertificateDirectory)
            ? Path.GetFullPath(configuredCertificateDirectory)
            : null;
        var accountDataDirectory = values.TryGetValue("account-data-directory", out var configuredAccountDataDirectory)
            ? Path.GetFullPath(configuredAccountDataDirectory)
            : null;
        var logFilePath = values.TryGetValue("log-file", out var configuredLogFilePath)
            ? Path.GetFullPath(configuredLogFilePath)
            : null;
        var requestJournalPath = values.TryGetValue("request-journal", out var configuredRequestJournalPath)
            ? Path.GetFullPath(configuredRequestJournalPath)
            : null;
        var meshTransport = values.TryGetValue("mesh-options", out var configuredMeshOptions)
            ? DecodeMeshTransport(configuredMeshOptions)
            : new MeshTransportOptions();
        var unknownArgument = values.Keys.FirstOrDefault(name =>
            !string.Equals(name, "data-directory", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "hostname", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "port", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "assets-path", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "owner-pid", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "control-secret", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "certificate-directory", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "account-data-directory", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "log-file", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "request-journal", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "mesh-options", StringComparison.OrdinalIgnoreCase));
        if (unknownArgument is not null)
        {
            throw new ArgumentException($"Argument '--{unknownArgument}' is not supported", nameof(args));
        }

        if (!string.Equals(hostname, LocalNodeEndpoint.Hostname, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The Local Node is restricted to the h.localhost loopback hostname", nameof(args));
        }

        if (port is < 1024 or > 65535)
        {
            throw new ArgumentException("The Local Node port must be between 1024 and 65535", nameof(args));
        }

        if (values.ContainsKey("owner-pid") && ownerProcessId is null or <= 0)
        {
            throw new ArgumentException("The Local Node owner PID must be a positive integer", nameof(args));
        }

        if ((ownerProcessId is null) != string.IsNullOrWhiteSpace(controlSecret))
        {
            throw new ArgumentException(
                "Managed Local Node startup requires both --owner-pid and --control-secret",
                nameof(args));
        }

        return new LocalNodeOptions(
            Path.GetFullPath(dataDirectory),
            hostname,
            port,
            assetsPath,
            ownerProcessId,
            controlSecret,
            certificateDirectory,
            accountDataDirectory,
            logFilePath,
            requestJournalPath)
        {
            MeshTransport = meshTransport
        };
    }

    internal static string EncodeMeshTransport(MeshTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var wireOptions = new MeshTransportWireOptions(
            options.DiscoveryPort,
            options.TransportPort,
            options.EnableMulticast,
            options.EnableInternetDiscovery,
            options.DiscoveryTargets
                .Select(endpoint => new MeshEndpointWireOptions(endpoint.Address.ToString(), endpoint.Port))
                .ToArray(),
            options.DhtBootstrapHosts.ToArray(),
            options.DhtBootstrapPort,
            options.AnnouncementInterval.Ticks,
            options.DhtRefreshInterval.Ticks,
            options.PresenceTimeout.Ticks,
            options.EndpointLifetime.Ticks);
        return Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(wireOptions));
    }

    private static MeshTransportOptions DecodeMeshTransport(string encoded)
    {
        try
        {
            var wireOptions = JsonSerializer.Deserialize<MeshTransportWireOptions>(
                                  Convert.FromBase64String(encoded))
                              ?? throw new FormatException("Mesh options are empty");
            if (wireOptions.DiscoveryTargets is null || wireOptions.DhtBootstrapHosts is null)
                throw new FormatException("Mesh option collections are missing");
            var options = new MeshTransportOptions
            {
                DiscoveryPort = wireOptions.DiscoveryPort,
                TransportPort = wireOptions.TransportPort,
                EnableMulticast = wireOptions.EnableMulticast,
                EnableInternetDiscovery = wireOptions.EnableInternetDiscovery,
                DiscoveryTargets = wireOptions.DiscoveryTargets
                    .Select(endpoint => new IPEndPoint(IPAddress.Parse(endpoint.Address), endpoint.Port))
                    .ToArray(),
                DhtBootstrapHosts = wireOptions.DhtBootstrapHosts,
                DhtBootstrapPort = wireOptions.DhtBootstrapPort,
                AnnouncementInterval = TimeSpan.FromTicks(wireOptions.AnnouncementIntervalTicks),
                DhtRefreshInterval = TimeSpan.FromTicks(wireOptions.DhtRefreshIntervalTicks),
                PresenceTimeout = TimeSpan.FromTicks(wireOptions.PresenceTimeoutTicks),
                EndpointLifetime = TimeSpan.FromTicks(wireOptions.EndpointLifetimeTicks)
            };
            options.Validate();
            return options;
        }
        catch (Exception exception) when (exception is FormatException
                                          or JsonException
                                          or ArgumentException
                                          or OverflowException)
        {
            throw new ArgumentException("The encoded Mesh transport options are invalid", nameof(encoded), exception);
        }
    }

    private sealed record MeshEndpointWireOptions(string Address, int Port);

    private sealed record MeshTransportWireOptions(
        int DiscoveryPort,
        int TransportPort,
        bool EnableMulticast,
        bool EnableInternetDiscovery,
        MeshEndpointWireOptions[] DiscoveryTargets,
        string[] DhtBootstrapHosts,
        int DhtBootstrapPort,
        long AnnouncementIntervalTicks,
        long DhtRefreshIntervalTicks,
        long PresenceTimeoutTicks,
        long EndpointLifetimeTicks);
}
