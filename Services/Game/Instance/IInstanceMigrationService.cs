namespace HyPrism.Services.Game.Instance;

/// <summary>
/// Handles one-time and on-startup migrations of legacy instance folder structures
/// and legacy configuration data to the current format.
/// </summary>
public interface IInstanceMigrationService
{
    /// <summary>
    /// Migrates data from legacy launcher installations (config, UUIDs, and instance folders)
    /// to the current application structure.
    /// Should be called once at startup before any instance operations.
    /// </summary>
    void MigrateLegacyData();

    /// <summary>
    /// Copies or restructures instance folders from a legacy root directory into the
    /// current instance root, converting old naming conventions (e.g. <c>release-v5</c>)
    /// to the new branch/version layout.
    /// </summary>
    /// <param name="legacyInstanceRoot">Path to the legacy instances directory.</param>
    void MigrateLegacyInstances(string legacyInstanceRoot);

    /// <summary>
    /// Renames legacy dash-separated folders (e.g. <c>release-v5</c>) that already live in
    /// the correct root to the new branch-subdirectory layout (<c>release/5</c>) in-place.
    /// </summary>
    /// <param name="instanceRoot">Path to the instance root to restructure.</param>
    void RestructureLegacyFoldersInPlace(string instanceRoot);

    /// <summary>
    /// Migrates instance folders from version-based naming (e.g. <c>release/5</c>) to
    /// GUID-based naming (e.g. <c>release/{guid}</c>).
    /// Should be called during startup after <see cref="MigrateLegacyData"/>.
    /// </summary>
    void MigrateVersionFoldersToIdFolders();

    /// <summary>
    /// Migrates instance folders from the branch-subdirectory layout
    /// (<c>{root}/{branch}/{guid}</c>) to the flat layout (<c>{root}/{guid}</c>).
    /// Empty branch directories are removed afterwards.
    /// Should be called during startup after <see cref="MigrateVersionFoldersToIdFolders"/>.
    /// </summary>
    void MigrateBranchSubdirectoriesToFlat();
}
