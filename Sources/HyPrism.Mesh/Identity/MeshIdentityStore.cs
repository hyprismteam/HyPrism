// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text.Json;

namespace HyPrism.Mesh;

/// <summary>
/// Persists stable signing and key-agreement identities per autonomous launcher profile
/// </summary>
public sealed class MeshIdentityStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _identityDirectory;

    public MeshIdentityStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _identityDirectory = Path.Combine(Path.GetFullPath(dataDirectory), "Mesh", "Identities");
        Directory.CreateDirectory(_identityDirectory);
    }

    public async Task<MeshPublicIdentity> GetPublicIdentityAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var identity = await LoadOrCreateAsync(profileId, cancellationToken).ConfigureAwait(false);
        return identity.PublicIdentity;
    }

    internal async Task<byte[]> SignAsync(
        string profileId,
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken = default)
    {
        var identity = await LoadOrCreateAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (!MeshCryptography.TryBase64UrlDecode(
                identity.PrivateKey,
                MeshCryptography.PrivateKeyLength,
                out var privateKey))
        {
            throw new InvalidDataException("The persisted mesh private key is malformed");
        }

        try
        {
            return MeshCryptography.Sign(privateKey, message.Span);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    internal async Task<IReadOnlyList<byte[]>> SignManyAsync(
        string profileId,
        IReadOnlyList<ReadOnlyMemory<byte>> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
            return [];

        var identity = await LoadOrCreateAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (!MeshCryptography.TryBase64UrlDecode(
                identity.PrivateKey,
                MeshCryptography.PrivateKeyLength,
                out var privateKey))
        {
            throw new InvalidDataException("The persisted mesh private key is malformed");
        }

        try
        {
            var signatures = new byte[messages.Count][];
            for (var index = 0; index < messages.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                signatures[index] = MeshCryptography.Sign(privateKey, messages[index].Span);
            }
            return signatures;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    internal async Task<byte[]> DerivePairwiseKeyAsync(
        string profileId,
        string remotePeerId,
        string remoteAgreementPublicKey,
        CancellationToken cancellationToken = default)
    {
        var identity = await LoadOrCreateAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (!MeshCryptography.TryBase64UrlDecode(
                identity.AgreementPrivateKey,
                MeshCryptography.AgreementKeyLength,
                out var privateKey)
            || !MeshCryptography.TryBase64UrlDecode(
                remoteAgreementPublicKey,
                MeshCryptography.AgreementKeyLength,
                out var remotePublicKey))
        {
            throw new InvalidDataException("A mesh agreement key is malformed");
        }

        try
        {
            var sharedSecret = MeshCryptography.DeriveSharedSecret(privateKey, remotePublicKey);
            try
            {
                return MeshCryptography.DerivePairwiseKey(sharedSecret, identity.PeerId, remotePeerId);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(sharedSecret);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
            CryptographicOperations.ZeroMemory(remotePublicKey);
        }
    }

    internal async Task<(MeshPublicIdentity Identity, IReadOnlyList<byte[]> Keys)> DerivePairwiseKeysAsync(
        string profileId,
        IReadOnlyList<MeshFriend> friends,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(friends);
        var identity = await LoadOrCreateAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (!MeshCryptography.TryBase64UrlDecode(
                identity.AgreementPrivateKey,
                MeshCryptography.AgreementKeyLength,
                out var privateKey))
        {
            throw new InvalidDataException("The persisted mesh agreement private key is malformed");
        }

        var keys = new List<byte[]>(friends.Count);
        try
        {
            foreach (var friend in friends)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!MeshCryptography.TryBase64UrlDecode(
                        friend.AgreementPublicKey,
                        MeshCryptography.AgreementKeyLength,
                        out var remotePublicKey))
                {
                    throw new InvalidDataException("A mesh agreement public key is malformed");
                }

                try
                {
                    var sharedSecret = MeshCryptography.DeriveSharedSecret(privateKey, remotePublicKey);
                    try
                    {
                        keys.Add(MeshCryptography.DerivePairwiseKey(
                            sharedSecret,
                            identity.PeerId,
                            friend.PeerId));
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(sharedSecret);
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(remotePublicKey);
                }
            }

            return (identity.PublicIdentity, keys);
        }
        catch
        {
            foreach (var key in keys)
                CryptographicOperations.ZeroMemory(key);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    private async Task<PersistedMeshIdentity> LoadOrCreateAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        ValidateProfileId(profileId);
        var profileHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(profileId)))
            .ToLowerInvariant();
        var path = Path.Combine(_identityDirectory, profileHash + ".json");
        var lockPath = Path.Combine(_identityDirectory, profileHash + ".lock");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var fileLock = await AcquireFileLockAsync(lockPath, cancellationToken).ConfigureAwait(false);
            if (File.Exists(path))
            {
                var existing = await LoadAndValidateAsync(path, cancellationToken).ConfigureAwait(false);
                if (existing.Version == 1)
                {
                    AddAgreementIdentity(existing);
                    await SaveAsync(path, existing, overwrite: true, cancellationToken).ConfigureAwait(false);
                }
                return existing;
            }

            var privateKey = MeshCryptography.CreatePrivateKey();
            var agreementPrivateKey = MeshCryptography.CreateAgreementPrivateKey();
            try
            {
                var publicKey = MeshCryptography.GetPublicKey(privateKey);
                var agreementPublicKey = MeshCryptography.GetAgreementPublicKey(agreementPrivateKey);
                var identity = new PersistedMeshIdentity
                {
                    Version = 2,
                    PeerId = MeshCryptography.GetPeerId(publicKey),
                    PublicKey = MeshCryptography.Base64UrlEncode(publicKey),
                    PrivateKey = MeshCryptography.Base64UrlEncode(privateKey),
                    AgreementPublicKey = MeshCryptography.Base64UrlEncode(agreementPublicKey),
                    AgreementPrivateKey = MeshCryptography.Base64UrlEncode(agreementPrivateKey),
                    CreatedAt = DateTimeOffset.UtcNow
                };
                await SaveAsync(path, identity, overwrite: false, cancellationToken).ConfigureAwait(false);
                return identity;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(privateKey);
                CryptographicOperations.ZeroMemory(agreementPrivateKey);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<PersistedMeshIdentity> LoadAndValidateAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var identity = await JsonSerializer.DeserializeAsync<PersistedMeshIdentity>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The persisted mesh identity is empty");

        if (identity.Version is not (1 or 2)
            || !MeshCryptography.TryBase64UrlDecode(
                identity.PrivateKey,
                MeshCryptography.PrivateKeyLength,
                out var privateKey)
            || !MeshCryptography.TryBase64UrlDecode(
                identity.PublicKey,
                MeshCryptography.PublicKeyLength,
                out var publicKey))
        {
            throw new InvalidDataException("The persisted mesh identity is malformed");
        }

        try
        {
            var derivedPublicKey = MeshCryptography.GetPublicKey(privateKey);
            if (!CryptographicOperations.FixedTimeEquals(derivedPublicKey, publicKey)
                || !string.Equals(
                    identity.PeerId,
                    MeshCryptography.GetPeerId(publicKey),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("The persisted mesh identity failed integrity validation");
            }

            if (identity.Version == 2)
                ValidateAgreementIdentity(identity);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
            CryptographicOperations.ZeroMemory(publicKey);
        }

        return identity;
    }

    private static void AddAgreementIdentity(PersistedMeshIdentity identity)
    {
        var agreementPrivateKey = MeshCryptography.CreateAgreementPrivateKey();
        try
        {
            identity.Version = 2;
            identity.AgreementPrivateKey = MeshCryptography.Base64UrlEncode(agreementPrivateKey);
            identity.AgreementPublicKey = MeshCryptography.Base64UrlEncode(
                MeshCryptography.GetAgreementPublicKey(agreementPrivateKey));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(agreementPrivateKey);
        }
    }

    private static void ValidateAgreementIdentity(PersistedMeshIdentity identity)
    {
        if (!MeshCryptography.TryBase64UrlDecode(
                identity.AgreementPrivateKey,
                MeshCryptography.AgreementKeyLength,
                out var privateKey)
            || !MeshCryptography.TryBase64UrlDecode(
                identity.AgreementPublicKey,
                MeshCryptography.AgreementKeyLength,
                out var publicKey))
        {
            throw new InvalidDataException("The persisted mesh agreement identity is malformed");
        }

        try
        {
            var derivedPublicKey = MeshCryptography.GetAgreementPublicKey(privateKey);
            if (!CryptographicOperations.FixedTimeEquals(derivedPublicKey, publicKey))
                throw new InvalidDataException("The persisted mesh agreement identity failed integrity validation");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
            CryptographicOperations.ZeroMemory(publicKey);
        }
    }

    private static async Task SaveAsync(
        string path,
        PersistedMeshIdentity identity,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, identity, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            ProtectPrivateFile(temporaryPath);
            File.Move(temporaryPath, path, overwrite);
            ProtectPrivateFile(path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static async Task<FileStream> AcquireFileLockAsync(
        string lockPath,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static void ProtectPrivateFile(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void ValidateProfileId(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId)
            || profileId.Length > 128
            || profileId.Any(char.IsControl))
        {
            throw new ArgumentException("A valid launcher profile ID is required", nameof(profileId));
        }
    }

    private sealed class PersistedMeshIdentity
    {
        public int Version { get; set; }
        public string PeerId { get; set; } = string.Empty;
        public string PublicKey { get; set; } = string.Empty;
        public string PrivateKey { get; set; } = string.Empty;
        public string AgreementPublicKey { get; set; } = string.Empty;
        public string AgreementPrivateKey { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }

        public MeshPublicIdentity PublicIdentity => new(PeerId, PublicKey, AgreementPublicKey, CreatedAt);
    }
}
