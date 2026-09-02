// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using HyPrism.Mesh;

namespace HyPrism.LocalNode;

/// <summary>
/// Uses the public Mainline Kademlia DHT as a serverless UDP rendezvous index
/// </summary>
internal sealed class MainlineDhtPeerLocator(
    MeshTransportOptions options,
    LocalNodeLog? log = null)
{
    private const int MaximumNodes = 256;
    private static ReadOnlySpan<byte> TopicDomain => "HyPrism pairing rendezvous v1\0"u8;
    private readonly ConcurrentDictionary<string, PendingQuery> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DhtNode> _nodes = new(StringComparer.Ordinal);
    private readonly byte[] _nodeId = RandomNumberGenerator.GetBytes(20);

    public bool TryHandleResponse(ReadOnlyMemory<byte> packet, IPEndPoint source)
    {
        if (packet.IsEmpty || packet.Span[0] != (byte)'d' || packet.Length > 64 * 1024)
            return false;

        Dictionary<string, object?> root;
        try
        {
            root = Bencode.DecodeDictionary(packet.Span);
        }
        catch (FormatException)
        {
            return false;
        }

        if (!TryBytes(root, "t", out var transaction)
            || !TryBytes(root, "y", out var kind))
        {
            return false;
        }

        var key = Convert.ToHexString(transaction);
        if (!_pending.TryGetValue(key, out var pending)
            || !Equals(pending.Endpoint, source)
            || !_pending.TryRemove(key, out pending))
            return false;

        if (!kind.AsSpan().SequenceEqual("r"u8)
            || !root.TryGetValue("r", out var responseValue)
            || responseValue is not Dictionary<string, object?> response)
        {
            pending.Completion.TrySetResult(new DhtResponse(source, [], [], [], null));
            return true;
        }

        TryBytes(response, "id", out var sourceNodeId);
        if (sourceNodeId.Length != 20)
            sourceNodeId = [];
        else if (_nodes.Count < MaximumNodes || _nodes.ContainsKey(EndpointKey(source)))
            _nodes[EndpointKey(source)] = new DhtNode(sourceNodeId, source);

        var nodes = ParseNodes(response).ToArray();
        foreach (var node in nodes)
        {
            if (_nodes.Count >= MaximumNodes)
                break;
            _nodes.TryAdd(EndpointKey(node.Endpoint), node);
        }
        var peers = ParsePeers(response).ToArray();
        TryBytes(response, "token", out var token);
        if (token.Length > 512)
            token = [];
        pending.Completion.TrySetResult(new DhtResponse(source, sourceNodeId, nodes, peers, token));
        return true;
    }

    public async Task<IReadOnlyList<IPEndPoint>> LookupAndAnnounceAsync(
        string friendId,
        int transportPort,
        Func<ReadOnlyMemory<byte>, IPEndPoint, CancellationToken, ValueTask> sendAsync,
        CancellationToken cancellationToken)
    {
        if (!options.EnableInternetDiscovery)
            return [];
        if (!MeshFriendId.TryNormalize(friendId, out var normalized))
            throw new ArgumentException("A valid Friend ID is required", nameof(friendId));
        if (transportPort is < 1 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(transportPort));

        var topic = CreateTopic(normalized);
        var candidates = (await ResolveStartingNodesAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(node => EndpointKey(node.Endpoint), StringComparer.Ordinal);
        var peers = new Dictionary<string, IPEndPoint>(StringComparer.Ordinal);
        var queried = new HashSet<string>(StringComparer.Ordinal);
        var announceTargets = new List<AnnounceTarget>();
        var distanceComparer = Comparer<DhtNode>.Create(
            (left, right) => CompareDistance(left.Id, right.Id, topic));

        for (var round = 0; round < 6 && candidates.Count > 0; round++)
        {
            var wave = candidates.Values
                .Where(node => !queried.Contains(EndpointKey(node.Endpoint)))
                .OrderBy(node => node, distanceComparer)
                .Take(8)
                .ToArray();
            if (wave.Length == 0)
                break;
            foreach (var node in wave)
                queried.Add(EndpointKey(node.Endpoint));

            var responses = await Task.WhenAll(wave.Select(node => QueryGetPeersAsync(
                node.Endpoint,
                topic,
                sendAsync,
                cancellationToken))).ConfigureAwait(false);
            foreach (var response in responses.OfType<DhtResponse>())
            {
                foreach (var peer in response.Peers)
                    peers[EndpointKey(peer)] = peer;
                foreach (var node in response.Nodes)
                    candidates[EndpointKey(node.Endpoint)] = node;
                if (response.Token is { Length: > 0 })
                    announceTargets.Add(new AnnounceTarget(response.SourceNodeId, response.Source, response.Token));
            }
        }

        await Task.WhenAll(announceTargets
            .OrderBy(
                target => new DhtNode(target.NodeId, target.Endpoint),
                distanceComparer)
            .Take(8)
            .Select(target => AnnounceAsync(
                target.Endpoint,
                topic,
                target.Token,
                transportPort,
                sendAsync,
                cancellationToken))).ConfigureAwait(false);
        return peers.Values.ToArray();
    }

    private async Task<DhtResponse?> QueryGetPeersAsync(
        IPEndPoint endpoint,
        byte[] topic,
        Func<ReadOnlyMemory<byte>, IPEndPoint, CancellationToken, ValueTask> sendAsync,
        CancellationToken cancellationToken)
    {
        var transaction = RandomNumberGenerator.GetBytes(4);
        var query = Bencode.Encode(new Dictionary<string, object?>
        {
            ["t"] = transaction,
            ["y"] = "q"u8.ToArray(),
            ["q"] = "get_peers"u8.ToArray(),
            ["a"] = new Dictionary<string, object?>
            {
                ["id"] = _nodeId,
                ["info_hash"] = topic
            }
        });
        return await SendQueryAsync(transaction, query, endpoint, sendAsync, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task AnnounceAsync(
        IPEndPoint endpoint,
        byte[] topic,
        byte[] token,
        int transportPort,
        Func<ReadOnlyMemory<byte>, IPEndPoint, CancellationToken, ValueTask> sendAsync,
        CancellationToken cancellationToken)
    {
        var transaction = RandomNumberGenerator.GetBytes(4);
        var query = Bencode.Encode(new Dictionary<string, object?>
        {
            ["t"] = transaction,
            ["y"] = "q"u8.ToArray(),
            ["q"] = "announce_peer"u8.ToArray(),
            ["a"] = new Dictionary<string, object?>
            {
                ["id"] = _nodeId,
                ["info_hash"] = topic,
                ["port"] = (long)transportPort,
                ["implied_port"] = 1L,
                ["token"] = token
            }
        });
        await SendQueryAsync(transaction, query, endpoint, sendAsync, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DhtResponse?> SendQueryAsync(
        byte[] transaction,
        byte[] query,
        IPEndPoint endpoint,
        Func<ReadOnlyMemory<byte>, IPEndPoint, CancellationToken, ValueTask> sendAsync,
        CancellationToken cancellationToken)
    {
        var key = Convert.ToHexString(transaction);
        var completion = new TaskCompletionSource<DhtResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(key, new PendingQuery(endpoint, completion)))
            return null;

        try
        {
            await sendAsync(query, endpoint, cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (SocketException exception)
        {
            log?.Warning($"DHT query to {endpoint} failed: {exception.Message}");
            return null;
        }
        finally
        {
            _pending.TryRemove(key, out _);
        }
    }

    private async Task<List<DhtNode>> ResolveStartingNodesAsync(CancellationToken cancellationToken)
    {
        if (!_nodes.IsEmpty)
            return _nodes.Values.ToList();

        var nodes = new List<DhtNode>();
        foreach (var host in options.DhtBootstrapHosts)
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
                nodes.AddRange(addresses
                    .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Take(2)
                    .Select(address => new DhtNode([], new IPEndPoint(address, options.DhtBootstrapPort))));
            }
            catch (SocketException exception)
            {
                log?.Warning($"DHT bootstrap host {host} could not be resolved: {exception.Message}");
            }
        }
        return nodes;
    }

    private static byte[] CreateTopic(string friendId)
    {
        var id = Encoding.ASCII.GetBytes(friendId);
        var input = new byte[TopicDomain.Length + id.Length];
        TopicDomain.CopyTo(input);
        id.CopyTo(input, TopicDomain.Length);
        return SHA1.HashData(input);
    }

    private static IEnumerable<DhtNode> ParseNodes(Dictionary<string, object?> response)
    {
        if (!TryBytes(response, "nodes", out var compact))
            yield break;
        for (var offset = 0; offset + 26 <= compact.Length; offset += 26)
        {
            var address = new IPAddress(compact.AsSpan(offset + 20, 4));
            var port = BinaryPrimitives.ReadUInt16BigEndian(compact.AsSpan(offset + 24, 2));
            if (port > 0 && IsPublicIpv4(address))
                yield return new DhtNode(
                    compact.AsSpan(offset, 20).ToArray(),
                    new IPEndPoint(address, port));
        }
    }

    private static int CompareDistance(byte[] left, byte[] right, ReadOnlySpan<byte> topic)
    {
        if (left.Length != topic.Length)
            return right.Length == topic.Length ? 1 : 0;
        if (right.Length != topic.Length)
            return -1;
        for (var index = 0; index < topic.Length; index++)
        {
            var comparison = (left[index] ^ topic[index]).CompareTo(right[index] ^ topic[index]);
            if (comparison != 0)
                return comparison;
        }
        return 0;
    }

    private static IEnumerable<IPEndPoint> ParsePeers(Dictionary<string, object?> response)
    {
        if (!response.TryGetValue("values", out var valuesValue) || valuesValue is not List<object?> values)
            yield break;
        foreach (var value in values.OfType<byte[]>())
        {
            if (value.Length != 6)
                continue;
            var address = new IPAddress(value.AsSpan(0, 4));
            var port = BinaryPrimitives.ReadUInt16BigEndian(value.AsSpan(4, 2));
            if (port > 0 && IsPublicIpv4(address))
                yield return new IPEndPoint(address, port);
        }
    }

    private static bool IsPublicIpv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
            return false;
        return bytes[0] switch
        {
            0 or 10 or 127 => false,
            100 when bytes[1] is >= 64 and <= 127 => false,
            169 when bytes[1] == 254 => false,
            172 when bytes[1] is >= 16 and <= 31 => false,
            192 when bytes[1] == 168 => false,
            192 when bytes[1] == 0 && bytes[2] is 0 or 2 => false,
            198 when bytes[1] is 18 or 19 => false,
            198 when bytes[1] == 51 && bytes[2] == 100 => false,
            203 when bytes[1] == 0 && bytes[2] == 113 => false,
            >= 224 => false,
            _ => true
        };
    }

    private static bool TryBytes(Dictionary<string, object?> dictionary, string key, out byte[] value)
    {
        if (dictionary.TryGetValue(key, out var item) && item is byte[] bytes)
        {
            value = bytes;
            return true;
        }
        value = [];
        return false;
    }

    private static string EndpointKey(IPEndPoint endpoint) => $"{endpoint.Address}:{endpoint.Port}";

    private sealed record DhtNode(byte[] Id, IPEndPoint Endpoint);
    private sealed record AnnounceTarget(byte[] NodeId, IPEndPoint Endpoint, byte[] Token);
    private sealed record PendingQuery(IPEndPoint Endpoint, TaskCompletionSource<DhtResponse> Completion);
    private sealed record DhtResponse(
        IPEndPoint Source,
        byte[] SourceNodeId,
        IReadOnlyList<DhtNode> Nodes,
        IReadOnlyList<IPEndPoint> Peers,
        byte[]? Token);

    private static class Bencode
    {
        public static byte[] Encode(Dictionary<string, object?> value)
        {
            using var stream = new MemoryStream();
            WriteValue(stream, value);
            return stream.ToArray();
        }

        public static Dictionary<string, object?> DecodeDictionary(ReadOnlySpan<byte> value)
        {
            var offset = 0;
            var decoded = ReadValue(value, ref offset, 0);
            if (offset != value.Length || decoded is not Dictionary<string, object?> dictionary)
                throw new FormatException("Invalid bencoded dictionary");
            return dictionary;
        }

        private static void WriteValue(Stream stream, object? value)
        {
            switch (value)
            {
                case byte[] bytes:
                    WriteAscii(stream, bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    stream.WriteByte((byte)':');
                    stream.Write(bytes);
                    break;
                case long number:
                    stream.WriteByte((byte)'i');
                    WriteAscii(stream, number.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    stream.WriteByte((byte)'e');
                    break;
                case Dictionary<string, object?> dictionary:
                    stream.WriteByte((byte)'d');
                    foreach (var item in dictionary.OrderBy(item => item.Key, StringComparer.Ordinal))
                    {
                        WriteValue(stream, Encoding.ASCII.GetBytes(item.Key));
                        WriteValue(stream, item.Value);
                    }
                    stream.WriteByte((byte)'e');
                    break;
                case List<object?> list:
                    stream.WriteByte((byte)'l');
                    foreach (var item in list)
                        WriteValue(stream, item);
                    stream.WriteByte((byte)'e');
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value));
            }
        }

        private static object? ReadValue(ReadOnlySpan<byte> value, ref int offset, int depth)
        {
            if (depth > 8 || offset >= value.Length)
                throw new FormatException("Invalid bencoded value");
            if (value[offset] == (byte)'d')
            {
                offset++;
                var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
                while (offset < value.Length && value[offset] != (byte)'e')
                {
                    var keyBytes = ReadBytes(value, ref offset);
                    if (!dictionary.TryAdd(Encoding.ASCII.GetString(keyBytes), ReadValue(value, ref offset, depth + 1)))
                        throw new FormatException("Duplicate bencoded key");
                }
                RequireEnd(value, ref offset);
                return dictionary;
            }
            if (value[offset] == (byte)'l')
            {
                offset++;
                var list = new List<object?>();
                while (offset < value.Length && value[offset] != (byte)'e')
                {
                    if (list.Count >= 1024)
                        throw new FormatException("Bencoded list is too large");
                    list.Add(ReadValue(value, ref offset, depth + 1));
                }
                RequireEnd(value, ref offset);
                return list;
            }
            if (value[offset] == (byte)'i')
            {
                offset++;
                var end = value[offset..].IndexOf((byte)'e');
                if (end <= 0
                    || !long.TryParse(
                        Encoding.ASCII.GetString(value.Slice(offset, end)),
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var number))
                {
                    throw new FormatException("Invalid bencoded integer");
                }
                offset += end + 1;
                return number;
            }
            return ReadBytes(value, ref offset);
        }

        private static byte[] ReadBytes(ReadOnlySpan<byte> value, ref int offset)
        {
            var separator = value[offset..].IndexOf((byte)':');
            if (separator <= 0
                || !int.TryParse(
                    Encoding.ASCII.GetString(value.Slice(offset, separator)),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var length)
                || length < 0
                || length > 64 * 1024)
            {
                throw new FormatException("Invalid bencoded byte string");
            }
            offset += separator + 1;
            if (offset + length > value.Length)
                throw new FormatException("Truncated bencoded byte string");
            var result = value.Slice(offset, length).ToArray();
            offset += length;
            return result;
        }

        private static void RequireEnd(ReadOnlySpan<byte> value, ref int offset)
        {
            if (offset >= value.Length || value[offset++] != (byte)'e')
                throw new FormatException("Unterminated bencoded container");
        }

        private static void WriteAscii(Stream stream, string value)
            => stream.Write(Encoding.ASCII.GetBytes(value));
    }
}
