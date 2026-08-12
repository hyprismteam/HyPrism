// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.RegularExpressions;
using HyPrism.Core.Models;

namespace HyPrism.Core.Game.Sources;

/// <summary>
/// Stores community download source definitions in the application data directory
/// </summary>
public sealed partial class MirrorCatalog : IMirrorCatalog
{
    private readonly string _appDir;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Creates a persisted community download source catalog
    /// </summary>
    /// <param name="appDir">The application data directory</param>
    /// <param name="httpClient">The shared HTTP client used by runtime source adapters</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="appDir"/> is empty</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="httpClient"/> is null</exception>
    public MirrorCatalog(string appDir, HttpClient httpClient)
    {
        if (string.IsNullOrWhiteSpace(appDir))
            throw new ArgumentException("Application directory is required", nameof(appDir));

        _appDir = Path.GetFullPath(appDir);
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <inheritdoc/>
    public IReadOnlyList<MirrorMeta> GetAll()
        => MirrorCatalogLoader.GetAllMirrorMetas(_appDir)
            .OrderBy(mirror => mirror.Priority)
            .ThenBy(mirror => mirror.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <inheritdoc/>
    public void Save(MirrorMeta mirror)
    {
        ArgumentNullException.ThrowIfNull(mirror);
        Validate(mirror);
        MirrorCatalogLoader.SaveMirror(_appDir, mirror);
    }

    /// <inheritdoc/>
    public bool Delete(string mirrorId)
    {
        ValidateId(mirrorId);
        return MirrorCatalogLoader.DeleteMirror(_appDir, mirrorId);
    }

    /// <inheritdoc/>
    public IReadOnlyList<IVersionSource> CreateEnabledSources()
        => MirrorCatalogLoader.LoadAll(_appDir, _httpClient);

    private static void Validate(MirrorMeta mirror)
    {
        ValidateId(mirror.Id);

        if (string.IsNullOrWhiteSpace(mirror.Name))
            throw new ArgumentException("Download source name is required", nameof(mirror));
        if (mirror.SchemaVersion != 1)
            throw new ArgumentException($"Unsupported mirror schema version: {mirror.SchemaVersion}", nameof(mirror));
        if (mirror.SourceType is not ("pattern" or "json-index"))
            throw new ArgumentException($"Unsupported mirror source type: {mirror.SourceType}", nameof(mirror));
        if (mirror.SourceType == "pattern" && mirror.Pattern is null)
            throw new ArgumentException("Pattern configuration is required", nameof(mirror));
        if (mirror.SourceType == "json-index" && mirror.JsonIndex is null)
            throw new ArgumentException("JSON index configuration is required", nameof(mirror));
    }

    private static void ValidateId(string mirrorId)
    {
        if (string.IsNullOrWhiteSpace(mirrorId) || !SafeIdPattern().IsMatch(mirrorId))
        {
            throw new ArgumentException(
                "Download source ID may contain only lowercase letters, numbers, dots, hyphens, and underscores",
                nameof(mirrorId));
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdPattern();
}
