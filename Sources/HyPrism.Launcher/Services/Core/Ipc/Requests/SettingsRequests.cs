using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HyPrism.Services.Core.Ipc.Requests;

public record TestMirrorSpeedRequest(string MirrorId, bool? ForceRefresh = null);
public record TestOfficialSpeedRequest(bool? ForceRefresh = null);
public record AddMirrorRequest(string Url, string? Headers = null);
public record MirrorIdRequest(string MirrorId);
public record ToggleMirrorRequest(string MirrorId, bool Enabled);
public record SetInstanceDirRequest(string Path);
public record PingAuthServerRequest(string? AuthDomain = null);

/// <summary>
/// Settings update — arbitrary key/value pairs from the frontend.
/// Uses <see cref="JsonExtensionDataAttribute"/> so that any flat JSON object
/// (e.g. <c>{"useDualAuth":false}</c>) is captured directly into <see cref="Updates"/>
/// without requiring a nested <c>{"updates":{…}}</c> wrapper.
/// </summary>
public class UpdateSettingsRequest
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Updates { get; set; } = new();
}
