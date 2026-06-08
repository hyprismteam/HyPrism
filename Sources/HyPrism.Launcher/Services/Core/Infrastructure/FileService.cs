using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HyPrism.Services.Core.Infrastructure;

/// <summary>
/// Provides file system operations for opening folders in the native file explorer.
/// Supports Windows Explorer, macOS Finder, and Linux xdg-open.
/// </summary>
public class FileService : IFileService
{
    private readonly string _appDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileService"/> class.
    /// </summary>
    /// <param name="appPath">The application path configuration containing the app data directory.</param>
    public FileService(AppPathConfiguration appPath)
    {
        _appDir = appPath.AppDir;
    }

    /// <inheritdoc/>
    public bool OpenAppFolder() => OpenFolderInExplorer(_appDir);

    /// <inheritdoc/>
    public bool OpenFolder(string path)
    {
        if (!Directory.Exists(path)) return false;
        return OpenFolderInExplorer(path);
    }

    /// <summary>
    /// Opens the specified folder path in the platform-specific file explorer.
    /// </summary>
    /// <param name="path">The absolute path to the folder to open.</param>
    /// <returns><c>true</c> if the explorer was launched successfully; otherwise, <c>false</c>.</returns>
    private bool OpenFolderInExplorer(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start("explorer.exe", $"\"{path}\"")?.Dispose();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo("open", $"\"{path}\"") { UseShellExecute = false })?.Dispose();
            }
            else
            {
                Process.Start("xdg-open", $"\"{path}\"")?.Dispose();
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Files", $"Failed to open folder '{path}': {ex.Message}");
            return false;
        }
    }
}
