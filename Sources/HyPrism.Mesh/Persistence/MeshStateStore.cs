// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;

namespace HyPrism.Mesh;

internal sealed class MeshStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _lockPath;
    private readonly string _path;

    public MeshStateStore(string dataDirectory)
    {
        var directory = Path.Combine(Path.GetFullPath(dataDirectory), "Mesh");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "Friends.json");
        _lockPath = Path.Combine(directory, "friends.lock");
    }

    public async Task<T> ReadAsync<T>(
        string profileId,
        Func<MeshProfileState, T> read,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var fileLock = await AcquireFileLockAsync(cancellationToken).ConfigureAwait(false);
            var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var profile = state.Profiles.TryGetValue(profileId, out var existing)
                ? existing
                : new MeshProfileState();
            return read(profile);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<T> UpdateAsync<T>(
        string profileId,
        Func<MeshProfileState, T> update,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var fileLock = await AcquireFileLockAsync(cancellationToken).ConfigureAwait(false);
            var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (!state.Profiles.TryGetValue(profileId, out var profile))
            {
                profile = new MeshProfileState();
                state.Profiles[profileId] = profile;
            }

            var result = update(profile);
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<MeshPersistentState> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return new MeshPersistentState();

        try
        {
            await using var stream = File.OpenRead(_path);
            var state = await JsonSerializer.DeserializeAsync<MeshPersistentState>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The persisted mesh friend state is empty");
            if (state.Version != 1)
                throw new InvalidDataException($"Mesh friend state version {state.Version} is not supported");
            return state;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The persisted mesh friend state is malformed", exception);
        }
    }

    private async Task SaveAsync(MeshPersistentState state, CancellationToken cancellationToken)
    {
        var temporaryPath = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             8192,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            ProtectPrivateFile(temporaryPath);
            File.Move(temporaryPath, _path, overwrite: true);
            ProtectPrivateFile(_path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private async Task<FileStream> AcquireFileLockAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
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
}

internal sealed class MeshPersistentState
{
    public int Version { get; set; } = 1;
    public Dictionary<string, MeshProfileState> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class MeshProfileState
{
    public List<MeshFriendState> Friends { get; set; } = [];
    public List<MeshIssuedInviteState> IssuedInvites { get; set; } = [];
    public List<MeshConsumedInviteState> ConsumedInvites { get; set; } = [];
}

internal sealed class MeshFriendState
{
    public string PeerId { get; set; } = string.Empty;
    public string SigningPublicKey { get; set; } = string.Empty;
    public string AgreementPublicKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset AddedAt { get; set; }
    public string? PlayerUuid { get; set; }

    public MeshFriend ToFriend()
        => new(PeerId, SigningPublicKey, AgreementPublicKey, DisplayName, AddedAt, PlayerUuid);
}

internal sealed class MeshIssuedInviteState
{
    public string InviteHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
}

internal sealed class MeshConsumedInviteState
{
    public string InviteHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}
