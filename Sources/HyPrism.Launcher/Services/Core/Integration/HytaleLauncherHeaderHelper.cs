using System.Text.Json;

namespace HyPrism.Services.Core.Integration;

internal static class HytaleLauncherHeaderHelper
{
    private const string LauncherInfoUrl = "https://launcher.hytale.com/version/release/launcher.json";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);
    private const string FallbackLauncherVersion = "unknown";

    private static readonly SemaphoreSlim FetchLock = new(1, 1);
    private static string? _cachedVersion;
    private static DateTime _cachedAt;

    public static async Task ApplyOfficialHeadersAsync(
        HttpRequestMessage request,
        HttpClient httpClient,
        string branch,
        CancellationToken ct = default)
    {
        var launcherVersion = await GetLauncherVersionAsync(httpClient, ct);
        var launcherBranch = NormalizeLauncherBranch(branch);

        request.Headers.TryAddWithoutValidation("User-Agent", $"hytale-launcher/{launcherVersion}");
        request.Headers.TryAddWithoutValidation("x-hytale-launcher-version", launcherVersion);
        request.Headers.TryAddWithoutValidation("x-hytale-launcher-branch", launcherBranch);
    }

    public static async Task<string> GetLauncherVersionAsync(HttpClient httpClient, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(_cachedVersion) && DateTime.UtcNow - _cachedAt < CacheTtl)
        {
            return _cachedVersion;
        }

        await FetchLock.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrWhiteSpace(_cachedVersion) && DateTime.UtcNow - _cachedAt < CacheTtl)
            {
                return _cachedVersion;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var response = await httpClient.GetAsync(LauncherInfoUrl, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return _cachedVersion ?? FallbackLauncherVersion;
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var parsed = TryExtractLauncherVersion(json);

            _cachedVersion = string.IsNullOrWhiteSpace(parsed)
                ? (_cachedVersion ?? FallbackLauncherVersion)
                : parsed;
            _cachedAt = DateTime.UtcNow;

            return _cachedVersion;
        }
        catch
        {
            return _cachedVersion ?? FallbackLauncherVersion;
        }
        finally
        {
            FetchLock.Release();
        }
    }

    public static string BuildLauncherDataUrlWithClientId(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return baseUrl;
        }

        return baseUrl.Contains('?', StringComparison.Ordinal)
            ? $"{baseUrl}&client_id=hytale-launcher"
            : $"{baseUrl}?client_id=hytale-launcher";
    }

    private static string NormalizeLauncherBranch(string branch)
    {
        return branch.ToLowerInvariant() switch
        {
            "pre-release" => "release",
            "prerelease" => "release",
            "pre_release" => "release",
            _ => "release"
        };
    }

    private static string? TryExtractLauncherVersion(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryGetString(root, "version", out var version)) return version;
                if (TryGetString(root, "launcher_version", out version)) return version;
                if (TryGetString(root, "build", out version)) return version;
                if (TryGetString(root, "id", out version)) return version;
            }
        }
        catch
        {
            // ignored
        }

        return null;
    }

    private static bool TryGetString(JsonElement obj, string key, out string value)
    {
        value = string.Empty;

        if (!obj.TryGetProperty(key, out var el))
            return false;

        if (el.ValueKind == JsonValueKind.String)
        {
            value = el.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        if (el.ValueKind == JsonValueKind.Number)
        {
            value = el.GetRawText();
            return true;
        }

        return false;
    }
}
