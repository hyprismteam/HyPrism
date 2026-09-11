// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using HyPrism.Core.Game.Launch;

namespace HyPrism.LocalNode;

/// <summary>
/// Prepares narrowly scoped client trust for the Local Node certificate
/// </summary>
public sealed class LocalNodeTrustStore
{
    private const string JavaTrustStorePassword = "hyprism-local";
    private readonly string? _bundlePath;
    private readonly string _javaTrustStorePath;

    private LocalNodeTrustStore(string? bundlePath, string javaTrustStorePath)
    {
        _bundlePath = bundlePath;
        _javaTrustStorePath = javaTrustStorePath;
    }

    /// <summary>
    /// Prepares platform-specific trust material
    /// </summary>
    public static LocalNodeTrustStore Prepare(
        LocalNodeOptions options,
        X509Certificate2 serverCertificate,
        X509Certificate2 rootCertificate)
    {
        var javaTrustStorePath = CreateJavaTrustStore(options, rootCertificate);
        if (OperatingSystem.IsWindows())
        {
            if (options.ConfigureSystemTrust)
                InstallForCurrentWindowsUser(rootCertificate);
            return new LocalNodeTrustStore(null, javaTrustStorePath);
        }

        if (OperatingSystem.IsMacOS() && options.ConfigureSystemTrust)
            MacOsCertificateTrust.EnsureTrusted(options, serverCertificate, rootCertificate);

        var bundlePath = Path.Combine(options.DataDirectory, "client-ca-bundle.pem");
        var systemBundle = FindSystemBundle();
        using var output = new FileStream(bundlePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        if (systemBundle is not null)
        {
            using var input = File.OpenRead(systemBundle);
            input.CopyTo(output);
            if (output.Position > 0)
                output.WriteByte((byte)'\n');
        }

        using var writer = new StreamWriter(output, leaveOpen: true);
        writer.Write(rootCertificate.ExportCertificatePem());
        writer.Flush();
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(bundlePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return new LocalNodeTrustStore(bundlePath, javaTrustStorePath);
    }

    /// <summary>
    /// Adds process-only trust environment variables where supported
    /// </summary>
    public void Apply(ProcessStartInfo startInfo)
    {
        if (_bundlePath is not null)
        {
            startInfo.Environment["SSL_CERT_FILE"] = _bundlePath;
            startInfo.Environment["HYPRISM_LOCAL_NODE_CA_BUNDLE"] = _bundlePath;
        }

        var javaTrustArguments = $"-Djavax.net.ssl.trustStore=\"{_javaTrustStorePath}\" "
            + "-Djavax.net.ssl.trustStoreType=PKCS12 "
            + $"-Djavax.net.ssl.trustStorePassword={JavaTrustStorePassword}";
        startInfo.Environment["HYPRISM_LOCAL_NODE_JAVA_OPTIONS"] = javaTrustArguments;
        startInfo.Environment.TryGetValue("JAVA_TOOL_OPTIONS", out var currentJavaOptions);
        startInfo.Environment["JAVA_TOOL_OPTIONS"] = JvmArgumentBuilder.MergeToolOptions(
            currentJavaOptions,
            javaTrustArguments);
    }

    private static void InstallForCurrentWindowsUser(X509Certificate2 certificate)
    {
        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        var existing = store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false);
        if (existing.Count == 0)
        {
            using var trustedCertificate = X509CertificateLoader.LoadCertificate(
                certificate.Export(X509ContentType.Cert));
            store.Add(trustedCertificate);
        }

        var obsoleteCertificates = FindObsoleteHyPrismRootCertificates(store.Certificates, certificate);
        if (obsoleteCertificates.Count > 0)
            store.RemoveRange(obsoleteCertificates);
    }

    internal static X509Certificate2Collection FindObsoleteHyPrismRootCertificates(
        X509Certificate2Collection certificates,
        X509Certificate2 currentCertificate)
    {
        var obsoleteCertificates = new X509Certificate2Collection();
        foreach (var certificate in certificates)
        {
            if (string.Equals(certificate.Thumbprint, currentCertificate.Thumbprint, StringComparison.OrdinalIgnoreCase)
                || !certificate.SubjectName.RawData.AsSpan().SequenceEqual(currentCertificate.SubjectName.RawData)
                || !certificate.IssuerName.RawData.AsSpan().SequenceEqual(certificate.SubjectName.RawData)
                || certificate.Extensions.OfType<X509BasicConstraintsExtension>()
                    .All(extension => !extension.CertificateAuthority))
            {
                continue;
            }

            obsoleteCertificates.Add(certificate);
        }

        return obsoleteCertificates;
    }

    private static string CreateJavaTrustStore(LocalNodeOptions options, X509Certificate2 certificate)
    {
        var trustStorePath = Path.Combine(options.DataDirectory, "client-trust.p12");
        using var publicCertificate = X509CertificateLoader.LoadCertificate(certificate.RawData);
        File.WriteAllBytes(
            trustStorePath,
            publicCertificate.Export(X509ContentType.Pkcs12, JavaTrustStorePassword));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(trustStorePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return trustStorePath;
    }

    private static string? FindSystemBundle()
    {
        var configured = Environment.GetEnvironmentVariable("SSL_CERT_FILE");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        string[] candidates =
        [
            "/etc/ssl/cert.pem",
            "/etc/ssl/certs/ca-certificates.crt",
            "/etc/pki/tls/certs/ca-bundle.crt",
            "/etc/ssl/ca-bundle.pem",
            "/etc/pki/ca-trust/extracted/pem/tls-ca-bundle.pem"
        ];
        return candidates.FirstOrDefault(File.Exists);
    }
}
