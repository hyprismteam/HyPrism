// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;

namespace HyPrism.LocalNode;

/// <summary>
/// Persists local profile, skin, and presence data without a network database
/// </summary>
public sealed class LocalAccountStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private LocalAccountState? _state;

    /// <summary>
    /// Creates a store below the Local Node data directory
    /// </summary>
    public LocalAccountStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "accounts.json");
    }

    /// <summary>
    /// Gets or creates a local profile
    /// </summary>
    public async Task<LocalProfileData> GetOrCreateAsync(string uuid, string username, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            if (!state.Profiles.TryGetValue(uuid, out var profile))
            {
                profile = CreateProfile(uuid, username);
                state.Profiles[uuid] = profile;
                await SaveAsync(state, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(username) && profile.Username != username)
            {
                profile.Username = username;
                await SaveAsync(state, cancellationToken);
            }

            return profile.Clone();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Finds a profile by UUID
    /// </summary>
    public async Task<LocalProfileData?> FindByUuidAsync(string uuid, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            return state.Profiles.TryGetValue(uuid, out var profile) ? profile.Clone() : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Finds a profile by username
    /// </summary>
    public async Task<LocalProfileData?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            return state.Profiles.Values
                .FirstOrDefault(profile => string.Equals(profile.Username, username, StringComparison.OrdinalIgnoreCase))
                ?.Clone();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Replaces the stored skin JSON for a profile
    /// </summary>
    public async Task SaveSkinAsync(string uuid, string username, JsonElement skin, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            if (!state.Profiles.TryGetValue(uuid, out var profile))
            {
                profile = CreateProfile(uuid, username);
                state.Profiles[uuid] = profile;
            }

            profile.SkinJson = skin.GetRawText();
            var activeSkin = profile.PlayerSkins.FirstOrDefault(item => item.Id == profile.ActiveSkinId);
            if (activeSkin is null)
            {
                activeSkin = new LocalPlayerSkinData
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Default Avatar",
                    CreatedAt = DateTimeOffset.UtcNow
                };
                profile.PlayerSkins.Add(activeSkin);
                profile.ActiveSkinId = activeSkin.Id;
            }
            activeSkin.SkinData = profile.SkinJson;
            await SaveAsync(state, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Saves local presence preferences
    /// </summary>
    public async Task SavePresenceSettingsAsync(
        string uuid,
        Dictionary<string, JsonElement> settings,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            if (!state.Profiles.TryGetValue(uuid, out var profile))
            {
                profile = CreateProfile(uuid, "Player");
                state.Profiles[uuid] = profile;
            }

            profile.PresenceSettings = settings.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());
            await SaveAsync(state, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Creates and persists a named player skin
    /// </summary>
    public async Task<string> CreatePlayerSkinAsync(
        string uuid,
        string username,
        string name,
        string skinData,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            if (!state.Profiles.TryGetValue(uuid, out var profile))
            {
                profile = CreateProfile(uuid, username);
                state.Profiles[uuid] = profile;
            }

            var skin = new LocalPlayerSkinData
            {
                Id = Guid.NewGuid().ToString(),
                Name = string.IsNullOrWhiteSpace(name) ? "Avatar" : name,
                SkinData = skinData,
                CreatedAt = DateTimeOffset.UtcNow
            };
            profile.PlayerSkins.Add(skin);
            profile.ActiveSkinId ??= skin.Id;
            if (profile.ActiveSkinId == skin.Id)
                profile.SkinJson = skin.SkinData;
            await SaveAsync(state, cancellationToken);
            return skin.Id;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Selects a saved player skin as active
    /// </summary>
    public async Task<bool> SetActivePlayerSkinAsync(
        string uuid,
        string skinId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            if (!state.Profiles.TryGetValue(uuid, out var profile))
                return false;
            var skin = profile.PlayerSkins.FirstOrDefault(item => item.Id == skinId);
            if (skin is null)
                return false;

            profile.ActiveSkinId = skin.Id;
            profile.SkinJson = skin.SkinData;
            await SaveAsync(state, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Updates a saved player skin
    /// </summary>
    public async Task<bool> UpdatePlayerSkinAsync(
        string uuid,
        string skinId,
        string? name,
        string? skinData,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            if (!state.Profiles.TryGetValue(uuid, out var profile))
                return false;
            var skin = profile.PlayerSkins.FirstOrDefault(item => item.Id == skinId);
            if (skin is null)
                return false;

            if (!string.IsNullOrWhiteSpace(name))
                skin.Name = name;
            if (skinData is not null)
                skin.SkinData = skinData;
            if (profile.ActiveSkinId == skin.Id)
                profile.SkinJson = skin.SkinData;
            await SaveAsync(state, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Deletes a saved player skin
    /// </summary>
    public async Task<bool> DeletePlayerSkinAsync(
        string uuid,
        string skinId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            if (!state.Profiles.TryGetValue(uuid, out var profile))
                return false;
            var removed = profile.PlayerSkins.RemoveAll(item => item.Id == skinId) > 0;
            if (!removed)
                return false;

            if (profile.ActiveSkinId == skinId)
            {
                var next = profile.PlayerSkins.FirstOrDefault();
                profile.ActiveSkinId = next?.Id;
                profile.SkinJson = next?.SkinData;
            }
            await SaveAsync(state, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<LocalAccountState> LoadAsync(CancellationToken cancellationToken)
    {
        if (_state is not null)
            return _state;
        if (!File.Exists(_path))
            return _state = new LocalAccountState();

        try
        {
            await using var stream = File.OpenRead(_path);
            _state = await JsonSerializer.DeserializeAsync<LocalAccountState>(stream, SerializerOptions, cancellationToken)
                ?? new LocalAccountState();
        }
        catch (JsonException)
        {
            var backupPath = _path + $".corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            File.Move(_path, backupPath, overwrite: true);
            _state = new LocalAccountState();
        }

        return _state;
    }

    private async Task SaveAsync(LocalAccountState state, CancellationToken cancellationToken)
    {
        var temporaryPath = _path + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, state, SerializerOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, _path, overwrite: true);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static LocalProfileData CreateProfile(string uuid, string username)
        => new() { Uuid = uuid, Username = username };
}

/// <summary>
/// Serializable Local Node account state
/// </summary>
public sealed class LocalAccountState
{
    public Dictionary<string, LocalProfileData> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Local account data required by Hytale account endpoints
/// </summary>
public sealed class LocalProfileData
{
    public string Uuid { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? SkinJson { get; set; }
    public Dictionary<string, JsonElement> PresenceSettings { get; set; } = [];
    public string? ActiveSkinId { get; set; }
    public List<LocalPlayerSkinData> PlayerSkins { get; set; } = [];

    public LocalProfileData Clone()
        => new()
        {
            Uuid = Uuid,
            Username = Username,
            SkinJson = SkinJson,
            PresenceSettings = PresenceSettings.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()),
            ActiveSkinId = ActiveSkinId,
            PlayerSkins = PlayerSkins.Select(skin => skin.Clone()).ToList()
        };
}

/// <summary>
/// One locally persisted avatar profile
/// </summary>
public sealed class LocalPlayerSkinData
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SkinData { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public LocalPlayerSkinData Clone()
        => new()
        {
            Id = Id,
            Name = Name,
            SkinData = SkinData,
            CreatedAt = CreatedAt
        };
}
