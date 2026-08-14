// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace HyPrism.LocalNode;

/// <summary>
/// Maintains the Local Node certificate in the current macOS user's trust settings
/// </summary>
internal static class MacOsCertificateTrust
{
    internal const string SkipEnvironmentVariable = "HYPRISM_LOCAL_NODE_SKIP_MACOS_TRUST";
    private const string TrustedCertificateFileName = "macos-trusted.crt";
    private const string SecurityExecutable = "/usr/bin/security";

    /// <summary>
    /// Ensures Apple Security trusts the local certificate authority for the Local Node hostname
    /// </summary>
    internal static void EnsureTrusted(
        LocalNodeOptions options,
        X509Certificate2 serverCertificate,
        X509Certificate2 rootCertificate)
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable(SkipEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        EnsureTrusted(options, serverCertificate, rootCertificate, RunSecurityCommand);
    }

    /// <summary>
    /// Ensures trust with an injectable command runner for deterministic tests
    /// </summary>
    internal static void EnsureTrusted(
        LocalNodeOptions options,
        X509Certificate2 serverCertificate,
        X509Certificate2 rootCertificate,
        Func<IReadOnlyList<string>, MacOsTrustCommandResult> runCommand)
    {
        ArgumentNullException.ThrowIfNull(runCommand);
        var serverCertificatePath = LocalNodeCertificateStore.GetPublicCertificatePath(options);
        var currentCertificatePath = LocalNodeCertificateStore.GetRootPublicCertificatePath(options);
        var trustedCertificatePath = Path.Combine(
            LocalNodeCertificateStore.GetCertificateDirectory(options),
            TrustedCertificateFileName);
        using var publicServerCertificate = X509CertificateLoader.LoadCertificateFromFile(serverCertificatePath);
        using var publicCertificate = X509CertificateLoader.LoadCertificateFromFile(currentCertificatePath);
        if (!publicServerCertificate.RawData.AsSpan().SequenceEqual(serverCertificate.RawData)
            || !publicCertificate.RawData.AsSpan().SequenceEqual(rootCertificate.RawData))
        {
            throw new InvalidOperationException(
                "The public Local Node certificate chain does not match the active HTTPS certificate");
        }

        if (Verify(runCommand, serverCertificatePath, options.Hostname).ExitCode == 0)
        {
            PreserveTrustedCertificate(currentCertificatePath, trustedCertificatePath);
            return;
        }

        RemoveRotatedCertificate(
            runCommand,
            currentCertificatePath,
            trustedCertificatePath);

        var install = runCommand(
        [
            "add-trusted-cert",
            "-r", "trustRoot",
            "-p", "ssl",
            "-s", options.Hostname,
            currentCertificatePath
        ]);
        if (install.ExitCode != 0)
        {
            throw CreateTrustException(
                "macOS did not install the Local Node certificate. Allow the security prompt and retry",
                install);
        }

        var verification = Verify(runCommand, serverCertificatePath, options.Hostname);
        if (verification.ExitCode != 0)
        {
            throw CreateTrustException(
                "macOS installed the Local Node certificate but did not trust it for TLS",
                verification);
        }

        PreserveTrustedCertificate(currentCertificatePath, trustedCertificatePath);
    }

    private static void RemoveRotatedCertificate(
        Func<IReadOnlyList<string>, MacOsTrustCommandResult> runCommand,
        string currentCertificatePath,
        string trustedCertificatePath)
    {
        if (!File.Exists(trustedCertificatePath)
            || File.ReadAllBytes(trustedCertificatePath).AsSpan()
                .SequenceEqual(File.ReadAllBytes(currentCertificatePath)))
        {
            return;
        }

        using var oldCertificate = X509CertificateLoader.LoadCertificateFromFile(trustedCertificatePath);
        var removeTrust = runCommand(["remove-trusted-cert", trustedCertificatePath]);
        if (removeTrust.ExitCode != 0)
        {
            throw CreateTrustException(
                "macOS did not remove the trust settings for the rotated Local Node certificate",
                removeTrust);
        }

        runCommand(["delete-certificate", "-Z", oldCertificate.Thumbprint]);
    }

    private static MacOsTrustCommandResult Verify(
        Func<IReadOnlyList<string>, MacOsTrustCommandResult> runCommand,
        string certificatePath,
        string hostname)
        => runCommand(
        [
            "verify-cert",
            "-c", certificatePath,
            "-p", "ssl",
            "-s", hostname
        ]);

    private static void PreserveTrustedCertificate(string sourcePath, string destinationPath)
    {
        File.Copy(sourcePath, destinationPath, overwrite: true);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(destinationPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static InvalidOperationException CreateTrustException(
        string message,
        MacOsTrustCommandResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        return new InvalidOperationException(
            string.IsNullOrWhiteSpace(detail) ? message : $"{message}: {detail.Trim()}");
    }

    private static MacOsTrustCommandResult RunSecurityCommand(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = SecurityExecutable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Could not start the macOS security tool");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new MacOsTrustCommandResult(
            process.ExitCode,
            standardOutput.GetAwaiter().GetResult(),
            standardError.GetAwaiter().GetResult());
    }
}

internal readonly record struct MacOsTrustCommandResult(
    int ExitCode,
    string StandardOutput = "",
    string StandardError = "");
