namespace HyPrism.Services.Core.Infrastructure;

/// <summary>
/// Provides file system operations for opening folders in the system file explorer.
/// </summary>
public interface IFileService
{
    /// <summary>
    /// Opens the HyPrism application data folder in the system file explorer.
    /// </summary>
    /// <returns><c>true</c> if the folder was opened successfully; otherwise, <c>false</c>.</returns>
    bool OpenAppFolder();
    
    /// <summary>
    /// Opens the specified folder path in the system file explorer.
    /// </summary>
    /// <param name="path">The absolute path to the folder to open.</param>
    /// <returns><c>true</c> if the folder was opened successfully; otherwise, <c>false</c>.</returns>
    bool OpenFolder(string path);
}
