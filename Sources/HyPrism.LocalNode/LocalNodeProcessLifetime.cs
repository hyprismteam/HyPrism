// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace HyPrism.LocalNode;

/// <summary>
/// Transfers Local Node ownership from the launcher to the launched game process
/// </summary>
public sealed class LocalNodeProcessLifetime : IDisposable
{
    private static readonly TimeSpan AttachTimeout = TimeSpan.FromMinutes(2);
    private readonly object _gate = new();
    private readonly LocalNodeLog _log;
    private readonly int? _ownerProcessId;
    private readonly CancellationTokenSource _waitingForGame = new();
    private int? _gameProcessId;
    private bool _stopping;
    private bool _disposed;

    /// <summary>
    /// Creates a lifecycle controller with an optional launcher owner process
    /// </summary>
    public LocalNodeProcessLifetime(LocalNodeLog log, int? ownerProcessId)
    {
        _log = log;
        _ownerProcessId = ownerProcessId;
    }

    /// <summary>
    /// Gets the game process currently responsible for the node lifetime
    /// </summary>
    public int? GameProcessId
    {
        get
        {
            lock (_gate)
                return _gameProcessId;
        }
    }

    /// <summary>
    /// Starts the pre-attachment launcher and timeout monitor
    /// </summary>
    public void Start(IHostApplicationLifetime applicationLifetime)
    {
        if (_ownerProcessId is not null)
            _ = MonitorBeforeAttachmentAsync(_ownerProcessId.Value, applicationLifetime);
    }

    /// <summary>
    /// Transfers ownership to a running game process
    /// </summary>
    public bool TryAttachGameProcess(
        int processId,
        IHostApplicationLifetime applicationLifetime,
        out string? error)
    {
        if (processId <= 0)
        {
            error = "gameProcessId must be a positive process ID";
            return false;
        }

        Process process;
        try
        {
            process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                process.Dispose();
                error = "The game process has already exited";
                return false;
            }
        }
        catch (ArgumentException)
        {
            error = "The game process does not exist";
            return false;
        }

        lock (_gate)
        {
            if (_gameProcessId is not null)
            {
                process.Dispose();
                error = _gameProcessId == processId
                    ? null
                    : $"Local Node is already attached to process {_gameProcessId}";
                return _gameProcessId == processId;
            }

            if (_stopping || _disposed)
            {
                process.Dispose();
                error = "Local Node is stopping";
                return false;
            }

            _gameProcessId = processId;
            _waitingForGame.Cancel();
        }

        _log.Info($"Lifetime attached to game process {processId}");
        _ = MonitorGameProcessAsync(process, applicationLifetime);
        error = null;
        return true;
    }

    /// <summary>
    /// Requests graceful Local Node shutdown
    /// </summary>
    public void Stop(string reason, IHostApplicationLifetime applicationLifetime)
    {
        lock (_gate)
        {
            if (_stopping)
                return;
            _stopping = true;
        }

        _log.Info(reason);
        applicationLifetime.StopApplication();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _waitingForGame.Cancel();
        _waitingForGame.Dispose();
    }

    private async Task MonitorBeforeAttachmentAsync(
        int ownerProcessId,
        IHostApplicationLifetime applicationLifetime)
    {
        try
        {
            using var ownerProcess = Process.GetProcessById(ownerProcessId);
            var ownerExit = ownerProcess.WaitForExitAsync(_waitingForGame.Token);
            var timeout = Task.Delay(AttachTimeout, _waitingForGame.Token);
            var completed = await Task.WhenAny(ownerExit, timeout);
            await completed;

            lock (_gate)
            {
                if (_gameProcessId is not null || _disposed)
                    return;
            }

            var reason = completed == ownerExit
                ? $"Launcher process {ownerProcessId} exited before game attachment"
                : "Game process was not attached before the startup timeout";
            Stop(reason, applicationLifetime);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ArgumentException)
        {
            Stop($"Launcher process {ownerProcessId} is no longer running", applicationLifetime);
        }
        catch (Exception exception)
        {
            _log.Error($"Launcher lifetime monitor failed: {exception.Message}");
            Stop("Launcher lifetime monitor stopped unexpectedly", applicationLifetime);
        }
    }

    private async Task MonitorGameProcessAsync(
        Process process,
        IHostApplicationLifetime applicationLifetime)
    {
        using (process)
        {
            try
            {
                await process.WaitForExitAsync();
                Stop($"Game process {process.Id} exited", applicationLifetime);
            }
            catch (Exception exception)
            {
                _log.Error($"Game lifetime monitor failed: {exception.Message}");
                Stop("Game lifetime monitor stopped unexpectedly", applicationLifetime);
            }
        }
    }
}
