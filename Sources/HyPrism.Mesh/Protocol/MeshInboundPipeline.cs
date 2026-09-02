// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace HyPrism.Mesh;

/// <summary>
/// Applies backpressure, authentication, decryption, and replay protection to inbound mesh envelopes
/// </summary>
public sealed class MeshInboundPipeline
{
    private readonly Channel<InboundEnvelope> _channel;
    private readonly MeshEnvelopeService _envelopes;
    private readonly MeshReplayWindow _replayWindow;
    private readonly MeshSecurityLimits _limits;
    private int _completed;
    private int _queued;
    private int _readerStarted;
    private long _accepted;
    private long _dropped;
    private long _rejected;

    public MeshInboundPipeline(
        MeshEnvelopeService envelopes,
        TimeProvider? timeProvider = null,
        MeshSecurityLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(envelopes);
        _envelopes = envelopes;
        _limits = limits ?? new MeshSecurityLimits();
        _limits.Validate();
        _replayWindow = new MeshReplayWindow(_limits, timeProvider ?? TimeProvider.System);
        _channel = Channel.CreateBounded<InboundEnvelope>(new BoundedChannelOptions(
            _limits.MaximumInboundQueueDepth)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    }

    public long AcceptedCount => Interlocked.Read(ref _accepted);
    public long RejectedCount => Interlocked.Read(ref _rejected);
    public long DroppedCount => Interlocked.Read(ref _dropped);

    public bool TryEnqueue(string senderPeerId, ReadOnlySpan<byte> envelope)
        => TryEnqueue(senderPeerId, envelope, transportContext: null);

    public bool TryEnqueue(
        string senderPeerId,
        ReadOnlySpan<byte> envelope,
        string? transportContext)
    {
        if (Volatile.Read(ref _completed) != 0
            || string.IsNullOrWhiteSpace(senderPeerId)
            || senderPeerId.Length > 128
            || transportContext?.Length > 256
            || envelope.Length is 0
            || envelope.Length > _limits.MaximumInboundEnvelopeBytes)
        {
            Interlocked.Increment(ref _dropped);
            return false;
        }

        if (Interlocked.Increment(ref _queued) > _limits.MaximumInboundQueueDepth)
        {
            Interlocked.Decrement(ref _queued);
            Interlocked.Increment(ref _dropped);
            return false;
        }

        var item = new InboundEnvelope(senderPeerId, envelope.ToArray(), transportContext);
        if (_channel.Writer.TryWrite(item))
            return true;

        Interlocked.Decrement(ref _queued);
        Interlocked.Increment(ref _dropped);
        return false;
    }

    public async IAsyncEnumerable<MeshMessage> ReadAllAsync(
        string profileId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var delivery in ReadDeliveriesCoreAsync(profileId, cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return delivery.Message;
        }
    }

    public IAsyncEnumerable<MeshInboundDelivery> ReadDeliveriesAsync(
        string profileId,
        CancellationToken cancellationToken = default)
        => ReadDeliveriesCoreAsync(profileId, cancellationToken);

    private async IAsyncEnumerable<MeshInboundDelivery> ReadDeliveriesCoreAsync(
        string profileId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _readerStarted, 1) != 0)
            throw new InvalidOperationException("A mesh inbound pipeline supports one reader");

        await foreach (var inbound in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            Interlocked.Decrement(ref _queued);
            var opened = await _envelopes.OpenAsync(
                profileId,
                inbound.SenderPeerId,
                inbound.Envelope,
                cancellationToken).ConfigureAwait(false);
            if (!opened.IsSuccess
                || !_replayWindow.TryAccept(
                    opened.Value.SenderPeerId,
                    opened.Value.MessageId,
                    opened.Value.IssuedAt))
            {
                Interlocked.Increment(ref _rejected);
                continue;
            }

            Interlocked.Increment(ref _accepted);
            yield return new MeshInboundDelivery(opened.Value, inbound.TransportContext);
        }
    }

    public void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
            _channel.Writer.TryComplete();
    }

    private sealed record InboundEnvelope(
        string SenderPeerId,
        byte[] Envelope,
        string? TransportContext);
}

internal sealed class MeshReplayWindow
{
    private readonly MeshSecurityLimits _limits;
    private readonly Dictionary<string, PeerReplayState> _peers = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public MeshReplayWindow(MeshSecurityLimits limits, TimeProvider timeProvider)
    {
        _limits = limits;
        _timeProvider = timeProvider;
    }

    public bool TryAccept(string senderPeerId, string messageId, DateTimeOffset issuedAt)
    {
        var now = _timeProvider.GetUtcNow();
        if (!_peers.TryGetValue(senderPeerId, out var peer))
        {
            peer = new PeerReplayState();
            _peers[senderPeerId] = peer;
        }

        while (peer.Order.Count > 0 && peer.Order.Peek().ExpiresAt <= now)
        {
            var expired = peer.Order.Dequeue();
            peer.MessageIds.Remove(expired.MessageId);
        }

        if (peer.MessageIds.Contains(messageId)
            || peer.MessageIds.Count >= _limits.MaximumReplayEntriesPerFriend)
        {
            return false;
        }

        var expiresAt = issuedAt
            .Add(_limits.MaximumEnvelopeLifetime)
            .Add(_limits.MaximumClockSkew);
        peer.MessageIds.Add(messageId);
        peer.Order.Enqueue(new ReplayEntry(messageId, expiresAt));
        return true;
    }

    private sealed class PeerReplayState
    {
        public HashSet<string> MessageIds { get; } = new(StringComparer.Ordinal);
        public Queue<ReplayEntry> Order { get; } = new();
    }

    private sealed record ReplayEntry(string MessageId, DateTimeOffset ExpiresAt);
}
