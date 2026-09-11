// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Infrastructure;
using System.Collections.Concurrent;

namespace HyPrism.Core.Game.Launch;

/// <summary>
/// A single console line produced by a game process or by the launcher on its behalf
/// </summary>
/// <param name="InstanceId">Stable instance identifier the line belongs to.</param>
/// <param name="Level">Severity tag: INF, WRN, ERR, or OUT.</param>
/// <param name="Text">Raw line text without a trailing newline.</param>
/// <param name="Timestamp">Local time when the line was captured.</param>
public sealed record GameConsoleLine(string InstanceId, string Level, string Text, DateTimeOffset Timestamp);

/// <summary>
/// Provides details when a game console line is captured
/// </summary>
public sealed class GameConsoleLineEventArgs(GameConsoleLine line) : EventArgs
{
    /// <summary>
    /// Gets the captured line
    /// </summary>
    public GameConsoleLine Line { get; } = line;
}

/// <summary>
/// Buffers live game process output per instance and notifies subscribers as lines arrive
/// </summary>
public interface IGameConsoleService
{
    /// <summary>
    /// Raised for every captured line, including lines appended before a subscriber attached
    /// only when reading <see cref="GetLines"/>
    /// </summary>
    event EventHandler<GameConsoleLineEventArgs>? LineReceived;

    /// <summary>
    /// Captures one console line for an instance
    /// </summary>
    /// <param name="instanceId">Stable instance identifier</param>
    /// <param name="level">Severity tag such as INF, WRN, ERR, or OUT</param>
    /// <param name="text">Raw line text</param>
    void Append(string instanceId, string level, string text);

    /// <summary>
    /// Gets the retained console lines for an instance in capture order
    /// </summary>
    /// <param name="instanceId">Stable instance identifier</param>
    /// <returns>A snapshot of buffered lines; empty when nothing was captured yet</returns>
    IReadOnlyList<GameConsoleLine> GetLines(string instanceId);

    /// <summary>
    /// Drops all retained console lines for an instance
    /// </summary>
    /// <param name="instanceId">Stable instance identifier</param>
    void Clear(string instanceId);
}

/// <summary>
/// In-memory per-instance console ring buffer with bounded retention
/// </summary>
public sealed class GameConsoleService : IGameConsoleService
{
    private const int MaxLinesPerInstance = 4000;

    private readonly ConcurrentDictionary<string, ConcurrentQueue<GameConsoleLine>> _buffers =
        new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public event EventHandler<GameConsoleLineEventArgs>? LineReceived;

    /// <inheritdoc/>
    public void Append(string instanceId, string level, string text)
    {
        if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(text))
            return;

        var line = new GameConsoleLine(instanceId, level, text, DateTimeOffset.Now);
        var buffer = _buffers.GetOrAdd(instanceId, _ => new ConcurrentQueue<GameConsoleLine>());
        buffer.Enqueue(line);
        while (buffer.Count > MaxLinesPerInstance && buffer.TryDequeue(out _))
        {
        }

        try
        {
            LineReceived?.Invoke(this, new GameConsoleLineEventArgs(line));
        }
        catch (Exception ex)
        {
            Logger.Warning("GameConsole", $"Console subscriber failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<GameConsoleLine> GetLines(string instanceId)
        => _buffers.TryGetValue(instanceId, out var buffer)
            ? buffer.ToArray()
            : [];

    /// <inheritdoc/>
    public void Clear(string instanceId)
    {
        if (_buffers.TryGetValue(instanceId, out var buffer))
            while (buffer.TryDequeue(out _))
            {
            }
    }
}
