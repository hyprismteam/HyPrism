// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace HyPrism.Mesh;

internal static class MeshCryptography
{
    public const int PrivateKeyLength = 32;
    public const int PublicKeyLength = 32;
    public const int SignatureLength = 64;
    public const int CapabilityLength = 32;
    public const int AgreementKeyLength = 32;
    public const int PairwiseKeyLength = 32;

    public static byte[] CreatePrivateKey()
        => RandomNumberGenerator.GetBytes(PrivateKeyLength);

    public static byte[] GetPublicKey(ReadOnlySpan<byte> privateKey)
    {
        var keyBytes = privateKey.ToArray();
        try
        {
            return new Ed25519PrivateKeyParameters(keyBytes, 0).GeneratePublicKey().GetEncoded();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    public static byte[] CreateAgreementPrivateKey()
        => RandomNumberGenerator.GetBytes(AgreementKeyLength);

    public static byte[] GetAgreementPublicKey(ReadOnlySpan<byte> privateKey)
    {
        var keyBytes = privateKey.ToArray();
        try
        {
            return new X25519PrivateKeyParameters(keyBytes, 0).GeneratePublicKey().GetEncoded();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    public static byte[] DeriveSharedSecret(
        ReadOnlySpan<byte> privateKey,
        ReadOnlySpan<byte> remotePublicKey)
    {
        if (privateKey.Length != AgreementKeyLength || remotePublicKey.Length != AgreementKeyLength)
            throw new ArgumentException("X25519 keys must be 32 bytes long");

        var privateKeyBytes = privateKey.ToArray();
        var publicKeyBytes = remotePublicKey.ToArray();
        try
        {
            var secret = new byte[AgreementKeyLength];
            var agreement = new X25519PrivateKeyParameters(privateKeyBytes, 0);
            agreement.GenerateSecret(new X25519PublicKeyParameters(publicKeyBytes, 0), secret, 0);
            return secret;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKeyBytes);
            CryptographicOperations.ZeroMemory(publicKeyBytes);
        }
    }

    public static byte[] DerivePairwiseKey(
        ReadOnlySpan<byte> sharedSecret,
        string firstPeerId,
        string secondPeerId)
    {
        var ordered = string.CompareOrdinal(firstPeerId, secondPeerId) <= 0
            ? (First: firstPeerId, Second: secondPeerId)
            : (First: secondPeerId, Second: firstPeerId);
        var context = Encoding.UTF8.GetBytes($"HyPrism Mesh Pairwise v1\0{ordered.First}\0{ordered.Second}");
        var salt = SHA256.HashData(context);
        var pseudoRandomKey = HMACSHA256.HashData(salt, sharedSecret);
        var info = Encoding.UTF8.GetBytes("HyPrism pairwise envelope key v1\u0001");
        try
        {
            return HMACSHA256.HashData(pseudoRandomKey, info);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pseudoRandomKey);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(context);
            CryptographicOperations.ZeroMemory(info);
        }
    }

    public static byte[] Sign(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> message)
    {
        var keyBytes = privateKey.ToArray();
        try
        {
            var signer = new Ed25519Signer();
            signer.Init(true, new Ed25519PrivateKeyParameters(keyBytes, 0));
            var messageBytes = message.ToArray();
            signer.BlockUpdate(messageBytes, 0, messageBytes.Length);
            return signer.GenerateSignature();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    public static bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
    {
        if (publicKey.Length != PublicKeyLength || signature.Length != SignatureLength)
            return false;

        try
        {
            var verifier = new Ed25519Signer();
            verifier.Init(false, new Ed25519PublicKeyParameters(publicKey.ToArray(), 0));
            var messageBytes = message.ToArray();
            verifier.BlockUpdate(messageBytes, 0, messageBytes.Length);
            return verifier.VerifySignature(signature.ToArray());
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static string GetPeerId(ReadOnlySpan<byte> publicKey)
        => "hp1_" + Base64UrlEncode(SHA256.HashData(publicKey));

    public static string HashCapability(ReadOnlySpan<byte> capability)
        => Base64UrlEncode(SHA256.HashData(capability));

    public static string Base64UrlEncode(ReadOnlySpan<byte> value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryBase64UrlDecode(string? value, int expectedLength, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512)
            return false;

        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized += (normalized.Length % 4) switch
            {
                2 => "==",
                3 => "=",
                _ => string.Empty
            };
            bytes = Convert.FromBase64String(normalized);
            if (bytes.Length == expectedLength)
                return true;
            CryptographicOperations.ZeroMemory(bytes);
            bytes = [];
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
