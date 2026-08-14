// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace HyPrism.LocalNode;

/// <summary>
/// Persists the self-signed loopback certificate used by the Local Node
/// </summary>
public static class LocalNodeCertificateStore
{
    private const string CertificateFileName = "h.localhost.pfx";
    private const string PublicCertificateFileName = "h.localhost.crt";
    private static readonly object CertificateLock = new();

    /// <summary>
    /// Loads the persistent loopback certificate or creates it on first use
    /// </summary>
    /// <param name="options">Endpoint and storage options for the Local Node</param>
    /// <returns>A certificate containing an exportable private key for Kestrel</returns>
    /// <exception cref="CryptographicException">Thrown when the certificate cannot be generated or loaded</exception>
    public static X509Certificate2 LoadOrCreate(LocalNodeOptions options)
    {
        lock (CertificateLock)
        {
            var certificatePath = GetCertificatePath(options);
            Directory.CreateDirectory(GetCertificateDirectory(options));

            if (TryLoadCurrentCertificate(certificatePath, options.Hostname, out var currentCertificate))
            {
                EnsurePublicCertificate(options, currentCertificate);
                return currentCertificate;
            }

            PreserveInvalidCertificate(certificatePath);

            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest(
                $"CN={options.Hostname}",
                key,
                HashAlgorithmName.SHA256);

            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.1")],
                false));

            var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
            subjectAlternativeNames.AddDnsName(options.Hostname);
            subjectAlternativeNames.AddDnsName("localhost");
            request.CertificateExtensions.Add(subjectAlternativeNames.Build());

            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
            File.WriteAllBytes(certificatePath, generated.Export(X509ContentType.Pfx));
            ProtectPrivateKey(certificatePath);
            EnsurePublicCertificate(options, generated);

            return X509CertificateLoader.LoadPkcs12FromFile(
                certificatePath,
                password: null,
                X509KeyStorageFlags.Exportable);
        }
    }

    /// <summary>
    /// Gets the persistent certificate path for the Local Node certificate store
    /// </summary>
    /// <param name="options">Endpoint and storage options for the Local Node</param>
    /// <returns>Absolute PFX file path</returns>
    public static string GetCertificatePath(LocalNodeOptions options)
        => Path.Combine(GetCertificateDirectory(options), CertificateFileName);

    /// <summary>
    /// Gets the PEM-encoded public certificate path for user-store trust installation
    /// </summary>
    /// <param name="options">Endpoint and storage options for the Local Node</param>
    /// <returns>Absolute public certificate path</returns>
    public static string GetPublicCertificatePath(LocalNodeOptions options)
        => Path.Combine(GetCertificateDirectory(options), PublicCertificateFileName);

    /// <summary>
    /// Gets the directory shared by Local Nodes that use the same loopback certificate
    /// </summary>
    public static string GetCertificateDirectory(LocalNodeOptions options)
        => string.IsNullOrWhiteSpace(options.CertificateDirectory)
            ? options.DataDirectory
            : options.CertificateDirectory;

    private static void EnsurePublicCertificate(LocalNodeOptions options, X509Certificate2 certificate)
    {
        var publicCertificatePath = GetPublicCertificatePath(options);
        File.WriteAllText(publicCertificatePath, certificate.ExportCertificatePem());
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(publicCertificatePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static bool TryLoadCurrentCertificate(
        string certificatePath,
        string hostname,
        out X509Certificate2 certificate)
    {
        certificate = null!;
        if (!File.Exists(certificatePath))
            return false;

        try
        {
            certificate = X509CertificateLoader.LoadPkcs12FromFile(
                certificatePath,
                password: null,
                X509KeyStorageFlags.Exportable);
            var expectedDnsName = certificate.GetNameInfo(X509NameType.DnsName, forIssuer: false);
            var reusable = certificate.HasPrivateKey
                && certificate.NotBefore.ToUniversalTime() <= DateTime.UtcNow
                && certificate.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(7)
                && string.Equals(expectedDnsName, hostname, StringComparison.OrdinalIgnoreCase);
            if (reusable)
                return true;

            certificate.Dispose();
            certificate = null!;
        }
        catch (CryptographicException)
        {
        }
        catch (IOException)
        {
        }

        return false;
    }

    private static void PreserveInvalidCertificate(string certificatePath)
    {
        if (!File.Exists(certificatePath))
            return;

        var invalidPath = certificatePath + $".invalid-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        File.Move(certificatePath, invalidPath, overwrite: true);
        ProtectPrivateKey(invalidPath);
    }

    private static void ProtectPrivateKey(string certificatePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(certificatePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
