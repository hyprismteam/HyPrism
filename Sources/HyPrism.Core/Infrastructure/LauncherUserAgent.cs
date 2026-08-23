// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Reflection;

namespace HyPrism.Core.Infrastructure;

/// <summary>
/// Provides the launcher identity used by outgoing HTTP requests
/// </summary>
public static class LauncherUserAgent
{
    private const string FallbackVersion = "0.0.0";
    private static string _value = Create(FallbackVersion);

    /// <summary>
    /// Gets the configured launcher User-Agent value
    /// </summary>
    public static string Value => Volatile.Read(ref _value);

    /// <summary>
    /// Extracts the product version generated for an assembly by MSBuild
    /// </summary>
    public static string GetVersion(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var metadataIndex = informationalVersion.IndexOf('+');
            return metadataIndex > 0
                ? informationalVersion[..metadataIndex]
                : informationalVersion;
        }

        var version = assembly.GetName().Version;
        return version is null
            ? FallbackVersion
            : $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }

    /// <summary>
    /// Creates a launcher User-Agent from a product version
    /// </summary>
    public static string Create(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var metadataIndex = version.IndexOf('+');
        var productVersion = (metadataIndex > 0 ? version[..metadataIndex] : version).Trim();
        if (productVersion.Length == 0 || productVersion.Any(char.IsWhiteSpace))
            throw new ArgumentException("The launcher version must be a valid HTTP product token", nameof(version));

        return $"HyPrism/{productVersion}";
    }

    /// <summary>
    /// Configures outgoing requests with the active host version
    /// </summary>
    public static void ConfigureVersion(string version)
        => Volatile.Write(ref _value, Create(version));
}
