// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;
using HyPrism.Mesh;

namespace HyPrism.LocalNode;

internal static class MeshPairingProtocol
{
    private static ReadOnlySpan<byte> Magic => "HPP1"u8;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static byte[] Encode(MeshPairingMessage message, MeshSecurityLimits limits)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (payload.Length == 0 || payload.Length > limits.MaximumTokenLength + 1024)
            throw new ArgumentOutOfRangeException(nameof(message), "The pairing packet exceeds its limit");

        var packet = new byte[Magic.Length + payload.Length];
        Magic.CopyTo(packet);
        payload.CopyTo(packet.AsSpan(Magic.Length));
        return packet;
    }

    public static bool TryParse(
        ReadOnlyMemory<byte> packet,
        MeshSecurityLimits limits,
        out MeshPairingMessage message)
    {
        message = null!;
        if (packet.Length <= Magic.Length
            || packet.Length > limits.MaximumTokenLength + 1024
            || !packet.Span[..Magic.Length].SequenceEqual(Magic))
        {
            return false;
        }

        try
        {
            message = JsonSerializer.Deserialize<MeshPairingMessage>(packet[Magic.Length..].Span, JsonOptions)!;
        }
        catch (JsonException)
        {
            return false;
        }

        return message is { Version: 1 }
               && message.RequestId is { Length: > 0 and <= 64 }
               && message.RequestId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
               && message.Type is "probe" or "challenge" or "invite" or "ack" or "accept" or "reject" or "punch"
               && (message.TargetFriendId is null
                   || MeshFriendId.TryNormalize(message.TargetFriendId, out _))
               && (message.Token is null || message.Token.Length <= limits.MaximumTokenLength);
    }
}

internal sealed record MeshPairingMessage(
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("target_friend_id")] string? TargetFriendId = null,
    [property: JsonPropertyName("token")] string? Token = null);
