// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text;
using HyPrism.Mesh;

namespace HyPrism.LocalNode;

internal static class MeshTransportProtocol
{
    private static ReadOnlySpan<byte> Magic => "HPT1"u8;
    private const byte ProtocolVersion = 1;
    private const int FixedHeaderLength = 4 + 1 + 1 + 1 + 2;

    public static byte[] EncodeEnvelope(
        string senderPeerId,
        string recipientPeerId,
        ReadOnlySpan<byte> envelope,
        MeshSecurityLimits limits)
    {
        var sender = Encoding.UTF8.GetBytes(senderPeerId);
        var recipient = Encoding.UTF8.GetBytes(recipientPeerId);
        if (sender.Length is 0 or > byte.MaxValue
            || recipient.Length is 0 or > byte.MaxValue
            || envelope.Length is 0 or > ushort.MaxValue
            || envelope.Length > limits.MaximumInboundEnvelopeBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(envelope), "The mesh transport frame exceeds its limit");
        }

        var packet = new byte[FixedHeaderLength + sender.Length + recipient.Length + envelope.Length];
        Magic.CopyTo(packet);
        packet[4] = ProtocolVersion;
        packet[5] = (byte)sender.Length;
        packet[6] = (byte)recipient.Length;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(7, 2), (ushort)envelope.Length);
        sender.CopyTo(packet, FixedHeaderLength);
        recipient.CopyTo(packet, FixedHeaderLength + sender.Length);
        envelope.CopyTo(packet.AsSpan(FixedHeaderLength + sender.Length + recipient.Length));
        return packet;
    }

    public static bool TryParseEnvelope(
        ReadOnlyMemory<byte> packet,
        MeshSecurityLimits limits,
        out string senderPeerId,
        out string recipientPeerId,
        out ReadOnlyMemory<byte> envelope)
    {
        senderPeerId = string.Empty;
        recipientPeerId = string.Empty;
        envelope = default;
        var packetSpan = packet.Span;
        if (packetSpan.Length < FixedHeaderLength + 3
            || !packetSpan[..Magic.Length].SequenceEqual(Magic)
            || packetSpan[4] != ProtocolVersion)
        {
            return false;
        }

        var senderLength = packetSpan[5];
        var recipientLength = packetSpan[6];
        var envelopeLength = BinaryPrimitives.ReadUInt16BigEndian(packetSpan.Slice(7, 2));
        if (senderLength == 0
            || recipientLength == 0
            || envelopeLength == 0
            || envelopeLength > limits.MaximumInboundEnvelopeBytes
            || FixedHeaderLength + senderLength + recipientLength + envelopeLength != packetSpan.Length)
        {
            return false;
        }

        try
        {
            var strictUtf8 = new UTF8Encoding(false, true);
            senderPeerId = strictUtf8.GetString(packetSpan.Slice(FixedHeaderLength, senderLength));
            recipientPeerId = strictUtf8.GetString(
                packetSpan.Slice(FixedHeaderLength + senderLength, recipientLength));
        }
        catch (DecoderFallbackException)
        {
            senderPeerId = string.Empty;
            recipientPeerId = string.Empty;
            return false;
        }

        if (!IsValidPeerId(senderPeerId) || !IsValidPeerId(recipientPeerId))
            return false;

        envelope = packet.Slice(FixedHeaderLength + senderLength + recipientLength, envelopeLength);
        return true;
    }

    private static bool IsValidPeerId(string peerId)
        => peerId.Length is > 0 and <= 128 && !peerId.Any(char.IsControl);
}
