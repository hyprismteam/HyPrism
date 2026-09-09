// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Game.Versions;
using HyPrism.Core.Infrastructure;

namespace HyPrism.Core.Game.Instances;

/// <summary>
/// Backfills human-readable version names for instances created before version names were stored
/// </summary>
public sealed class InstanceVersionNameMigrator
{
    private static readonly TimeSpan MigrationTimeout = TimeSpan.FromSeconds(15);

    private readonly IInstanceRepository _instances;
    private readonly IGameVersionCatalog _versions;

    public InstanceVersionNameMigrator(
        IInstanceRepository instances,
        IGameVersionCatalog versions)
    {
        _instances = instances;
        _versions = versions;
    }

    /// <summary>
    /// Finds missing version names, persists them to instance metadata, and refreshes the instance cache
    /// </summary>
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(MigrationTimeout);

            var instances = _instances.GetCachedInstances()
                .Where(instance => instance.Version > 0 && string.IsNullOrWhiteSpace(instance.VersionName))
                .ToList();
            if (instances.Count == 0)
                return;

            var versionNamesByBranch = new Dictionary<string, Dictionary<int, string>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var branch in instances
                .Select(instance => instance.Branch)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var response = await _versions
                    .GetVersionListWithSourcesAsync(branch, timeout.Token)
                    .ConfigureAwait(false);
                if (response?.Versions is null)
                    continue;

                versionNamesByBranch[branch] = response.Versions
                    .Where(version => version.Version > 0 && !string.IsNullOrWhiteSpace(version.VersionName))
                    .GroupBy(version => version.Version)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First().VersionName!);
            }

            var changed = false;
            foreach (var instance in instances)
            {
                if (!versionNamesByBranch.TryGetValue(instance.Branch, out var versionNames) ||
                    !versionNames.TryGetValue(instance.Version, out var versionName))
                {
                    continue;
                }

                var instancePath = _instances.GetInstancePathById(instance.Id);
                if (string.IsNullOrWhiteSpace(instancePath))
                    continue;

                var meta = _instances.GetInstanceMeta(instancePath);
                if (meta is null)
                    continue;

                if (!string.Equals(meta.VersionName, versionName, StringComparison.Ordinal))
                {
                    meta.VersionName = versionName;
                    _instances.SaveInstanceMeta(instancePath, meta);
                }

                changed = true;
            }

            if (changed && !timeout.IsCancellationRequested)
                _instances.SyncInstancesWithConfig();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            Logger.Warning("Instances", "Version name migration timed out");
        }
        catch (Exception ex)
        {
            Logger.Warning("Instances", $"Version name migration failed: {ex.Message}");
        }
    }
}
