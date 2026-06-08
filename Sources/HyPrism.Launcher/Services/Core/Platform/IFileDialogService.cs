namespace HyPrism.Services.Core.Platform;

/// <summary>
/// Provides file and folder dialog operations abstracted from the UI layer.
/// Enables ViewModels to request file/folder selection without direct UI dependencies.
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// Opens a folder picker dialog to allow the user to select a directory.
    /// </summary>
    /// <param name="initialPath">Optional initial directory path to start the dialog in.</param>
    /// <returns>The selected folder path, or <c>null</c> if the user cancelled the dialog.</returns>
    Task<string?> BrowseFolderAsync(string? initialPath = null);

    /// <summary>
    /// Opens a file picker dialog configured for selecting a Java executable.
    /// </summary>
    /// <returns>The selected executable path, or <c>null</c> if the user cancelled.</returns>
    Task<string?> BrowseJavaExecutableAsync();
    
    /// <summary>
    /// Opens a file picker dialog configured for selecting mod files (.jar, .zip).
    /// </summary>
    /// <returns>An array of selected file paths. Returns empty array if cancelled.</returns>
    Task<string[]> BrowseModFilesAsync();
    
    /// <summary>
    /// Opens a save file dialog to allow the user to specify a save location.
    /// </summary>
    /// <param name="defaultFileName">Default file name to suggest.</param>
    /// <param name="filter">File type filter (e.g., "Zip files|*.zip").</param>
    /// <param name="initialPath">Optional initial directory path.</param>
    /// <returns>The selected save path, or <c>null</c> if the user cancelled.</returns>
    Task<string?> SaveFileAsync(string defaultFileName, string filter, string? initialPath = null);
    
    /// <summary>
    /// Opens a file picker dialog configured for selecting instance archive files (ZIP or PWR).
    /// </summary>
    /// <returns>The selected file path, or <c>null</c> if cancelled.</returns>
    Task<string?> BrowseInstanceArchiveAsync();
    
    /// <summary>
    /// Opens a file picker dialog configured for selecting ZIP files.
    /// </summary>
    /// <returns>The selected file path, or <c>null</c> if cancelled.</returns>
    [Obsolete("Use BrowseInstanceArchiveAsync instead")]
    Task<string?> BrowseZipFileAsync();
}
