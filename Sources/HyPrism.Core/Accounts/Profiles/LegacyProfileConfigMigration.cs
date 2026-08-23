// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using HyPrism.Core.Infrastructure;
using HyPrism.Core.Models;

namespace HyPrism.Core.Accounts;

/// <summary>
/// Moves profile identity out of legacy config fields into Profiles/profiles.json
/// </summary>
internal static class LegacyProfileConfigMigration
{
    private static readonly JsonSerializerOptions ProfileOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Migrates legacy profile fields and returns config JSON without those fields
    /// </summary>
    public static string Migrate(string appDataPath, string json, out bool changed)
    {
        changed = false;
        var root = JsonNode.Parse(json)?.AsObject()
                   ?? throw new JsonException("The launcher config root must be an object");

        var embeddedProfilesNode = Remove(root, "Profiles", ref changed);
        var legacyNickname = GetString(Remove(root, "Nick", ref changed));
        var legacyUuid = GetString(Remove(root, "UUID", ref changed));
        var legacyIndex = GetInt32(Remove(root, "ActiveProfileIndex", ref changed));

        var profilesDirectory = Path.Combine(appDataPath, "Profiles");
        var profilesPath = Path.Combine(profilesDirectory, "profiles.json");
        var profiles = ReadProfiles(profilesPath);
        var profilesChanged = false;

        if (profiles.Count == 0 && embeddedProfilesNode is not null)
        {
            profiles = embeddedProfilesNode.Deserialize<List<Profile>>(ProfileOptions) ?? [];
            profilesChanged = profiles.Count > 0;
        }

        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id) || !Guid.TryParse(profile.Id, out _))
            {
                profile.Id = Guid.NewGuid().ToString();
                profilesChanged = true;
            }
        }

        if (profiles.Count == 0
            && !string.IsNullOrWhiteSpace(legacyNickname)
            && Guid.TryParse(legacyUuid, out var parsedUuid))
        {
            profiles.Add(new Profile
            {
                Id = Guid.NewGuid().ToString(),
                Name = legacyNickname.Trim(),
                UUID = parsedUuid.ToString(),
                CreatedAt = DateTime.UtcNow
            });
            profilesChanged = true;
        }

        var selectedId = GetString(Get(root, "SelectedProfileId"));
        var selectedProfile = profiles.FirstOrDefault(profile => profile.Id == selectedId);
        if (selectedProfile is null && legacyIndex is >= 0 && legacyIndex < profiles.Count)
            selectedProfile = profiles[legacyIndex.Value];
        if (selectedProfile is null && Guid.TryParse(legacyUuid, out var selectedUuid))
        {
            selectedProfile = profiles.FirstOrDefault(profile =>
                Guid.TryParse(profile.UUID, out var profileUuid) && profileUuid == selectedUuid);
        }
        selectedProfile ??= profiles.Count == 1 ? profiles[0] : null;

        var migratedSelectedId = selectedProfile?.Id ?? string.Empty;
        if (!string.Equals(selectedId, migratedSelectedId, StringComparison.Ordinal))
        {
            root[FindKey(root, "SelectedProfileId") ?? "SelectedProfileId"] = migratedSelectedId;
            changed = true;
        }

        if (profilesChanged)
        {
            Directory.CreateDirectory(profilesDirectory);
            File.WriteAllText(profilesPath, JsonSerializer.Serialize(profiles, ProfileOptions));
            changed = true;
        }

        return root.ToJsonString(JsonDefaults.IndentedUnsafeRelaxed);
    }

    private static List<Profile> ReadProfiles(string path)
    {
        if (!File.Exists(path))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(path), ProfileOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static JsonNode? Remove(JsonObject root, string name, ref bool changed)
    {
        var key = FindKey(root, name);
        if (key is null)
            return null;
        var value = root[key]?.DeepClone();
        root.Remove(key);
        changed = true;
        return value;
    }

    private static JsonNode? Get(JsonObject root, string name)
    {
        var key = FindKey(root, name);
        return key is null ? null : root[key];
    }

    private static string? FindKey(JsonObject root, string name)
        => root.Select(pair => pair.Key)
            .FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));

    private static string? GetString(JsonNode? node)
    {
        try { return node?.GetValue<string>(); }
        catch (InvalidOperationException) { return null; }
    }

    private static int? GetInt32(JsonNode? node)
    {
        try { return node?.GetValue<int>(); }
        catch (InvalidOperationException) { return null; }
    }
}
