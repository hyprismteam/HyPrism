// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;

namespace HyPrism.Mesh;

/// <summary>
/// Creates the short, stable identifier entered through Hytale's friend dialog
/// </summary>
public static class MeshFriendId
{
    public const int Length = 16;
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private static ReadOnlySpan<byte> Domain => "HyPrism Friend ID v1\0"u8;

    public static string FromIdentity(MeshPublicIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return FromSigningPublicKey(identity.SigningPublicKey);
    }

    public static string FromSigningPublicKey(string signingPublicKey)
    {
        if (!MeshCryptography.TryBase64UrlDecode(
                signingPublicKey,
                MeshCryptography.PublicKeyLength,
                out var publicKey))
        {
            throw new ArgumentException("A valid Mesh signing public key is required", nameof(signingPublicKey));
        }

        try
        {
            var input = new byte[Domain.Length + publicKey.Length];
            Domain.CopyTo(input);
            publicKey.CopyTo(input, Domain.Length);
            var hash = SHA256.HashData(input);
            return Encode80Bits(hash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(publicKey);
        }
    }

    public static bool TryNormalize(string? value, out string friendId)
    {
        friendId = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        Span<char> normalized = stackalloc char[Length];
        var written = 0;
        foreach (var source in value)
        {
            if (source is '-' or ' ')
                continue;
            if (written == normalized.Length)
                return false;

            var character = char.ToUpperInvariant(source) switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                var other => other
            };
            if (!Alphabet.Contains(character, StringComparison.Ordinal))
                return false;
            normalized[written++] = character;
        }

        if (written != Length)
            return false;
        friendId = new string(normalized);
        return true;
    }

    private static string Encode80Bits(ReadOnlySpan<byte> hash)
    {
        Span<char> result = stackalloc char[Length];
        for (var index = 0; index < result.Length; index++)
        {
            var bitOffset = index * 5;
            var byteOffset = bitOffset / 8;
            var shift = 11 - bitOffset % 8;
            var window = (hash[byteOffset] << 8)
                         | (byteOffset + 1 < hash.Length ? hash[byteOffset + 1] : 0);
            result[index] = Alphabet[(window >> shift) & 31];
        }
        return new string(result);
    }
}
