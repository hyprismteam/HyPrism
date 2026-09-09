// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Core.Migrations;

/// <summary>
/// A durable, idempotent migration of launcher-owned local data.
/// </summary>
public interface IMigration
{
    /// <summary>Stable identifier persisted after a successful run.</summary>
    string Id { get; }

    /// <summary>Applies the migration.</summary>
    Task ApplyAsync(MigrationContext context, CancellationToken cancellationToken = default);
}
