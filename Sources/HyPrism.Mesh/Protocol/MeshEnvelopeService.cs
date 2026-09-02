// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HyPrism.Mesh;

/// <summary>
/// Seals and opens authenticated pairwise messages exchanged by confirmed friends
/// </summary>
public sealed class MeshEnvelopeService
{
    private static ReadOnlySpan<byte> Magic => "HPM1"u8;
    private const byte EnvelopeVersion = 1;
    private const int MessageIdLength = 16;
    private const int NonceLength = 12;
    private const int AuthenticationTagLength = 16;
    private const int HeaderLength = 4 + 1 + 8 + NonceLength + 4;
    private const int TrailerLength = AuthenticationTagLength + MeshCryptography.SignatureLength;

    private readonly MeshFriendService _friends;
    private readonly MeshSecurityLimits _limits;
    private readonly TimeProvider _timeProvider;

    public MeshEnvelopeService(
        MeshFriendService friends,
        TimeProvider? timeProvider = null,
        MeshSecurityLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(friends);
        _friends = friends;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _limits = limits ?? new MeshSecurityLimits();
        _limits.Validate();
    }

    public async Task<MeshResult<byte[]>> SealAsync(
        string profileId,
        string recipientPeerId,
        MeshMessageKind kind,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind))
            return MeshResult<byte[]>.Failed("invalid_message_kind", "The mesh message kind is not supported");
        if (payload.Length > _limits.MaximumEnvelopePayloadBytes)
        {
            return MeshResult<byte[]>.Failed(
                "payload_too_large",
                "The mesh message payload exceeds the configured safety limit");
        }

        var contextResult = await _friends.GetPairwiseContextAsync(
            profileId,
            recipientPeerId,
            cancellationToken).ConfigureAwait(false);
        if (!contextResult.IsSuccess)
            return MeshResult<byte[]>.Failed(contextResult.Failure.Code, contextResult.Failure.Message);

        using var context = contextResult.Value;
        var issuedAt = _timeProvider.GetUtcNow();
        var messageId = RandomNumberGenerator.GetBytes(MessageIdLength);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var plaintext = SerializePlaintext(
            kind,
            messageId,
            context.LocalIdentity.PeerId,
            context.RemoteFriend.PeerId,
            payload.Span);
        try
        {
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[AuthenticationTagLength];
            var header = CreateHeader(issuedAt, nonce, ciphertext.Length);
            using (var cipher = new AesGcm(context.Key, AuthenticationTagLength))
                cipher.Encrypt(nonce, plaintext, ciphertext, tag, header);

            var signedLength = header.Length + ciphertext.Length + tag.Length;
            var envelope = new byte[signedLength + MeshCryptography.SignatureLength];
            header.CopyTo(envelope, 0);
            ciphertext.CopyTo(envelope, header.Length);
            tag.CopyTo(envelope, header.Length + ciphertext.Length);
            var signature = await _friends.SignAsync(
                profileId,
                envelope.AsMemory(0, signedLength),
                cancellationToken).ConfigureAwait(false);
            signature.CopyTo(envelope, signedLength);

            if (envelope.Length > _limits.MaximumInboundEnvelopeBytes)
            {
                CryptographicOperations.ZeroMemory(envelope);
                return MeshResult<byte[]>.Failed(
                    "envelope_too_large",
                    "The sealed mesh envelope exceeds the configured safety limit");
            }

            return MeshResult<byte[]>.Success(envelope);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(messageId);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task<MeshResult<MeshMessage>> OpenAsync(
        string profileId,
        string senderPeerId,
        ReadOnlyMemory<byte> envelope,
        CancellationToken cancellationToken = default)
    {
        var parsedHeader = ParseHeader(envelope.Span);
        if (!parsedHeader.IsSuccess)
            return MeshResult<MeshMessage>.Failed(parsedHeader.Failure.Code, parsedHeader.Failure.Message);

        var header = parsedHeader.Value;
        var timeFailure = ValidateEnvelopeTime(header.IssuedAt);
        if (timeFailure is not null)
            return MeshResult<MeshMessage>.Failed(timeFailure.Code, timeFailure.Message);

        var contextResult = await _friends.GetPairwiseContextAsync(
            profileId,
            senderPeerId,
            cancellationToken).ConfigureAwait(false);
        if (!contextResult.IsSuccess)
            return MeshResult<MeshMessage>.Failed(contextResult.Failure.Code, contextResult.Failure.Message);

        using var context = contextResult.Value;
        if (!MeshCryptography.TryBase64UrlDecode(
                context.RemoteFriend.SigningPublicKey,
                MeshCryptography.PublicKeyLength,
                out var signingPublicKey))
        {
            return MeshResult<MeshMessage>.Failed("invalid_friend_key", "The friend signing key is malformed");
        }

        var signedLength = envelope.Length - MeshCryptography.SignatureLength;
        var signature = envelope.Span[signedLength..];
        try
        {
            if (!MeshCryptography.Verify(signingPublicKey, envelope.Span[..signedLength], signature))
            {
                return MeshResult<MeshMessage>.Failed(
                    "invalid_signature",
                    "The mesh envelope signature is invalid");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signingPublicKey);
        }

        var ciphertext = envelope.Span.Slice(HeaderLength, header.CiphertextLength);
        var tag = envelope.Span.Slice(HeaderLength + header.CiphertextLength, AuthenticationTagLength);
        var plaintext = new byte[header.CiphertextLength];
        try
        {
            using (var cipher = new AesGcm(context.Key, AuthenticationTagLength))
            {
                cipher.Decrypt(
                    header.Nonce,
                    ciphertext,
                    tag,
                    plaintext,
                    envelope.Span[..HeaderLength]);
            }

            var messageResult = ParsePlaintext(plaintext, header.IssuedAt);
            if (!messageResult.IsSuccess)
                return messageResult;
            if (!string.Equals(messageResult.Value.SenderPeerId, senderPeerId, StringComparison.Ordinal)
                || !string.Equals(
                    messageResult.Value.RecipientPeerId,
                    context.LocalIdentity.PeerId,
                    StringComparison.Ordinal))
            {
                return MeshResult<MeshMessage>.Failed(
                    "wrong_recipient",
                    "The mesh envelope identity binding is invalid");
            }

            return messageResult;
        }
        catch (CryptographicException)
        {
            return MeshResult<MeshMessage>.Failed(
                "decryption_failed",
                "The mesh envelope could not be authenticated");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private MeshResult<ParsedEnvelopeHeader> ParseHeader(ReadOnlySpan<byte> envelope)
    {
        if (envelope.Length is < HeaderLength + TrailerLength
            || envelope.Length > _limits.MaximumInboundEnvelopeBytes
            || !envelope[..Magic.Length].SequenceEqual(Magic)
            || envelope[Magic.Length] != EnvelopeVersion)
        {
            return MeshResult<ParsedEnvelopeHeader>.Failed(
                "invalid_envelope",
                "The mesh envelope header is invalid");
        }

        var issuedAtValue = BinaryPrimitives.ReadInt64BigEndian(envelope.Slice(5, 8));
        DateTimeOffset issuedAt;
        try
        {
            issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtValue);
        }
        catch (ArgumentOutOfRangeException)
        {
            return MeshResult<ParsedEnvelopeHeader>.Failed(
                "invalid_envelope",
                "The mesh envelope timestamp is invalid");
        }

        var ciphertextLength = BinaryPrimitives.ReadInt32BigEndian(envelope.Slice(25, 4));
        if (ciphertextLength <= 0
            || ciphertextLength > _limits.MaximumEnvelopePayloadBytes + 512
            || HeaderLength + ciphertextLength + TrailerLength != envelope.Length)
        {
            return MeshResult<ParsedEnvelopeHeader>.Failed(
                "invalid_envelope",
                "The mesh envelope length is invalid");
        }

        return MeshResult<ParsedEnvelopeHeader>.Success(new ParsedEnvelopeHeader(
            issuedAt,
            envelope.Slice(13, NonceLength).ToArray(),
            ciphertextLength));
    }

    private MeshFailure? ValidateEnvelopeTime(DateTimeOffset issuedAt)
    {
        var now = _timeProvider.GetUtcNow();
        if (issuedAt > now.Add(_limits.MaximumClockSkew))
            return new MeshFailure("invalid_time", "The mesh envelope was issued too far in the future");
        return now - issuedAt > _limits.MaximumEnvelopeLifetime
            ? new MeshFailure("expired", "The mesh envelope has expired")
            : null;
    }

    private static byte[] CreateHeader(DateTimeOffset issuedAt, ReadOnlySpan<byte> nonce, int ciphertextLength)
    {
        var header = new byte[HeaderLength];
        Magic.CopyTo(header);
        header[4] = EnvelopeVersion;
        BinaryPrimitives.WriteInt64BigEndian(header.AsSpan(5, 8), issuedAt.ToUnixTimeSeconds());
        nonce.CopyTo(header.AsSpan(13, NonceLength));
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(25, 4), ciphertextLength);
        return header;
    }

    private static byte[] SerializePlaintext(
        MeshMessageKind kind,
        ReadOnlySpan<byte> messageId,
        string senderPeerId,
        string recipientPeerId,
        ReadOnlySpan<byte> payload)
    {
        var sender = Encoding.UTF8.GetBytes(senderPeerId);
        var recipient = Encoding.UTF8.GetBytes(recipientPeerId);
        if (sender.Length > byte.MaxValue || recipient.Length > byte.MaxValue)
            throw new InvalidOperationException("A mesh Peer ID exceeds the wire-format limit");

        var plaintext = new byte[1 + MessageIdLength + 1 + sender.Length + 1 + recipient.Length + 4 + payload.Length];
        var offset = 0;
        plaintext[offset++] = (byte)kind;
        messageId.CopyTo(plaintext.AsSpan(offset, MessageIdLength));
        offset += MessageIdLength;
        plaintext[offset++] = (byte)sender.Length;
        sender.CopyTo(plaintext, offset);
        offset += sender.Length;
        plaintext[offset++] = (byte)recipient.Length;
        recipient.CopyTo(plaintext, offset);
        offset += recipient.Length;
        BinaryPrimitives.WriteInt32BigEndian(plaintext.AsSpan(offset, 4), payload.Length);
        offset += 4;
        payload.CopyTo(plaintext.AsSpan(offset));
        return plaintext;
    }

    private MeshResult<MeshMessage> ParsePlaintext(ReadOnlySpan<byte> plaintext, DateTimeOffset issuedAt)
    {
        var minimumLength = 1 + MessageIdLength + 1 + 1 + 4;
        if (plaintext.Length < minimumLength)
            return MeshResult<MeshMessage>.Failed("invalid_message", "The mesh message is malformed");

        var offset = 0;
        var kind = (MeshMessageKind)plaintext[offset++];
        if (!Enum.IsDefined(kind))
            return MeshResult<MeshMessage>.Failed("invalid_message_kind", "The mesh message kind is not supported");

        var messageId = MeshCryptography.Base64UrlEncode(plaintext.Slice(offset, MessageIdLength));
        offset += MessageIdLength;
        if (!TryReadString(plaintext, ref offset, out var senderPeerId)
            || !TryReadString(plaintext, ref offset, out var recipientPeerId)
            || offset + 4 > plaintext.Length)
        {
            return MeshResult<MeshMessage>.Failed("invalid_message", "The mesh message is malformed");
        }

        var payloadLength = BinaryPrimitives.ReadInt32BigEndian(plaintext.Slice(offset, 4));
        offset += 4;
        if (payloadLength < 0
            || payloadLength > _limits.MaximumEnvelopePayloadBytes
            || offset + payloadLength != plaintext.Length)
        {
            return MeshResult<MeshMessage>.Failed("invalid_message", "The mesh message payload length is invalid");
        }

        return MeshResult<MeshMessage>.Success(new MeshMessage(
            messageId,
            senderPeerId,
            recipientPeerId,
            kind,
            issuedAt,
            plaintext.Slice(offset, payloadLength).ToArray()));
    }

    private static bool TryReadString(ReadOnlySpan<byte> plaintext, ref int offset, out string value)
    {
        value = string.Empty;
        if (offset >= plaintext.Length)
            return false;
        var length = plaintext[offset++];
        if (length == 0 || offset + length > plaintext.Length)
            return false;

        try
        {
            value = new UTF8Encoding(false, true).GetString(plaintext.Slice(offset, length));
            offset += length;
            return value.Length <= 128 && !value.Any(char.IsControl);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private sealed record ParsedEnvelopeHeader(
        DateTimeOffset IssuedAt,
        byte[] Nonce,
        int CiphertextLength);
}
