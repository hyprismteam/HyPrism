// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Models;

namespace HyPrism.Core.Game.Sources;

/// <summary>
/// Owns persisted community download source definitions
/// </summary>
public interface IMirrorCatalog
{
    /// <summary>
    /// Reads all configured community download sources
    /// </summary>
    /// <returns>Definitions ordered by priority and display name</returns>
    IReadOnlyList<MirrorMeta> GetAll();

    /// <summary>
    /// Saves a new or updated community download source
    /// </summary>
    /// <param name="mirror">The validated source definition to persist</param>
    /// <exception cref="ArgumentException">Thrown when the definition is incomplete or unsafe</exception>
    void Save(MirrorMeta mirror);

    /// <summary>
    /// Deletes a community download source
    /// </summary>
    /// <param name="mirrorId">The identifier of the source to delete</param>
    /// <returns><c>true</c> when a persisted definition was removed</returns>
    /// <exception cref="ArgumentException">Thrown when the source identifier is unsafe</exception>
    bool Delete(string mirrorId);

    /// <summary>
    /// Creates runtime adapters for enabled community download sources
    /// </summary>
    /// <returns>Enabled sources ordered by priority</returns>
    IReadOnlyList<IVersionSource> CreateEnabledSources();
}
