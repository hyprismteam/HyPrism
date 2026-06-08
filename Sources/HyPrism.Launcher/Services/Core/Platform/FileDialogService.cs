using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.Core.Platform;

/// <summary>
/// Handles native file dialog interactions across different operating systems.
/// Provides cross-platform file browsing capabilities using OS-specific dialogs.
/// </summary>
public class FileDialogService : IFileDialogService
{
    /// <summary>
    /// Opens a folder browser dialog and returns the selected path.
    /// </summary>
    public async Task<string?> BrowseFolderAsync(string? initialPath = null)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var script = $@"Add-Type -AssemblyName System.Windows.Forms; $dialog = New-Object System.Windows.Forms.FolderBrowserDialog; ";
                if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
                    script += $@"$dialog.SelectedPath = '{initialPath.Replace("'", "''")}'; ";
                script += @"if ($dialog.ShowDialog() -eq 'OK') { $dialog.SelectedPath }";

                var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -NonInteractive -STA -ExecutionPolicy Bypass -EncodedCommand {encodedScript}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null) return null;
                
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    Logger.Warning("Files", $"Windows folder picker failed with code {process.ExitCode}: {error}");
                    return null;
                }
                
                return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var initialDir = !string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath) 
                    ? $"default location \"{initialPath}\"" 
                    : "";
                    
                var script = $@"tell application ""Finder""
                    activate
                    set theFolder to choose folder with prompt ""Select Folder"" {initialDir}
                    return POSIX path of theFolder
                end tell";
                
                var psi = new ProcessStartInfo
                {
                    FileName = "osascript",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null) return null;
                
                await process.StandardInput.WriteAsync(script);
                process.StandardInput.Close();
                
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
            }
            else
            {
                return await BrowseFolderLinuxAsync(initialPath);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Files", $"Failed to browse folder: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Opens a native file picker to select a Java executable.
    /// </summary>
    public async Task<string?> BrowseJavaExecutableAsync()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await BrowseJavaExecutableWindowsAsync();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return await BrowseJavaExecutableMacOSAsync();
            }
            else
            {
                return await BrowseJavaExecutableLinuxAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Files", $"Failed to browse Java executable: {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> BrowseJavaExecutableWindowsAsync()
    {
        var script = @"Add-Type -AssemblyName System.Windows.Forms; $dialog = New-Object System.Windows.Forms.OpenFileDialog; $dialog.Filter = 'Java executable (java.exe;javaw.exe)|java.exe;javaw.exe|Executable files (*.exe)|*.exe|All Files (*.*)|*.*'; $dialog.Multiselect = $false; $dialog.Title = 'Select Java executable'; if ($dialog.ShowDialog() -eq 'OK') { $dialog.FileName }";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -Command \"{script}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return null;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
    }

    private static async Task<string?> BrowseJavaExecutableMacOSAsync()
    {
        var script = @"tell application ""Finder""
            activate
            set theFile to choose file with prompt ""Select Java executable""
            return POSIX path of theFile
        end tell";

        var psi = new ProcessStartInfo
        {
            FileName = "osascript",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return null;

        await process.StandardInput.WriteAsync(script);
        process.StandardInput.Close();

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output.Trim() : null;
    }

    private static async Task<string?> BrowseJavaExecutableLinuxAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "zenity",
            Arguments = "--file-selection --title=\"Select Java executable\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return null;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output.Trim() : null;
    }

    private static async Task<string?> BrowseFolderLinuxAsync(string? initialPath)
    {
        var safeInitialPath = !string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath)
            ? initialPath
            : null;

        // Preferred order: zenity (GNOME), kdialog (KDE), yad/qarma (alternatives)
        var candidates = new List<(string fileName, string arguments)>();

        var zenityArgs = "--file-selection --directory --title=\"Select Folder\"";
        if (!string.IsNullOrEmpty(safeInitialPath))
            zenityArgs += $" --filename=\"{safeInitialPath}/\"";
        candidates.Add(("zenity", zenityArgs));

        var kdialogArgs = "--getexistingdirectory";
        if (!string.IsNullOrEmpty(safeInitialPath))
            kdialogArgs += $" \"{safeInitialPath}\"";
        candidates.Add(("kdialog", kdialogArgs));

        var yadArgs = "--file-selection --directory --title=\"Select Folder\"";
        if (!string.IsNullOrEmpty(safeInitialPath))
            yadArgs += $" --filename=\"{safeInitialPath}/\"";
        candidates.Add(("yad", yadArgs));

        var qarmaArgs = "--file-selection --directory --title=\"Select Folder\"";
        if (!string.IsNullOrEmpty(safeInitialPath))
            qarmaArgs += $" --filename=\"{safeInitialPath}/\"";
        candidates.Add(("qarma", qarmaArgs));

        foreach (var (fileName, arguments) in candidates)
        {
            var selected = await TryRunLinuxDialogAsync(fileName, arguments);
            if (!string.IsNullOrWhiteSpace(selected))
                return selected.Trim();
        }

        Logger.Warning("Files", "No Linux folder dialog tool available (tried: zenity, kdialog, yad, qarma)");
        return null;
    }

    private static async Task<string?> TryRunLinuxDialogAsync(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Opens a native file dialog to browse for mod files.
    /// Supports multiple file selection across Windows, macOS, and Linux.
    /// </summary>
    /// <returns>Array of selected file paths, or empty array if cancelled</returns>
    public async Task<string[]> BrowseModFilesAsync()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return await BrowseFilesMacOSAsync();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await BrowseFilesWindowsAsync();
            }
            else
            {
                return await BrowseFilesLinuxAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Files", $"Failed to browse files: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// macOS file picker using AppleScript.
    /// </summary>
    private static async Task<string[]> BrowseFilesMacOSAsync()
    {
        var script = @"tell application ""Finder""
            activate
            set theFiles to choose file with prompt ""Select Mod Files"" of type {""jar"", ""zip"", ""hmod"", ""litemod"", ""json""} with multiple selections allowed
            set filePaths to """"
            repeat with aFile in theFiles
                set filePaths to filePaths & POSIX path of aFile & ""\n""
            end repeat
            return filePaths
        end tell";
        
        var psi = new ProcessStartInfo
        {
            FileName = "osascript",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        using var process = Process.Start(psi);
        if (process == null) return Array.Empty<string>();
        
        await process.StandardInput.WriteAsync(script);
        process.StandardInput.Close();
        
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        
        if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToArray();
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Windows file picker using PowerShell.
    /// </summary>
    private static async Task<string[]> BrowseFilesWindowsAsync()
    {
        var script = @"Add-Type -AssemblyName System.Windows.Forms; $dialog = New-Object System.Windows.Forms.OpenFileDialog; $dialog.Filter = 'Mod Files (*.jar;*.zip;*.hmod;*.litemod;*.json)|*.jar;*.zip;*.hmod;*.litemod;*.json|All Files (*.*)|*.*'; $dialog.Multiselect = $true; $dialog.Title = 'Select Mod Files'; if ($dialog.ShowDialog() -eq 'OK') { $dialog.FileNames -join ""`n"" }";
        
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -Command \"{script}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        using var process = Process.Start(psi);
        if (process == null) return Array.Empty<string>();
        
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        
        if (!string.IsNullOrWhiteSpace(output))
        {
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToArray();
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Linux file picker using zenity.
    /// </summary>
    private static async Task<string[]> BrowseFilesLinuxAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "zenity",
            Arguments = "--file-selection --multiple --title=\"Select Mod Files\" --file-filter=\"Mod Files | *.jar *.zip *.hmod *.litemod *.json\" --separator=\"\\n\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        using var process = Process.Start(psi);
        if (process == null) return Array.Empty<string>();
        
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        
        if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToArray();
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Opens a save file dialog to allow the user to specify a save location.
    /// </summary>
    public async Task<string?> SaveFileAsync(string defaultFileName, string filter, string? initialPath = null)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await SaveFileWindowsAsync(defaultFileName, filter, initialPath);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return await SaveFileMacOSAsync(defaultFileName, initialPath);
            }
            else
            {
                return await SaveFileLinuxAsync(defaultFileName, filter, initialPath);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Files", $"Failed to show save dialog: {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> SaveFileWindowsAsync(string defaultFileName, string filter, string? initialPath)
    {
        // Convert filter format from "Zip files|*.zip" to "Zip files (*.zip)|*.zip"
        var filterParts = filter.Split('|');
        var psFilter = filterParts.Length >= 2 ? $"{filterParts[0]} ({filterParts[1]})|{filterParts[1]}" : filter;
        
        var script = $@"Add-Type -AssemblyName System.Windows.Forms; $dialog = New-Object System.Windows.Forms.SaveFileDialog; $dialog.Filter = '{psFilter.Replace("'", "''")}'; $dialog.FileName = '{defaultFileName.Replace("'", "''")}'; ";
        if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
            script += $@"$dialog.InitialDirectory = '{initialPath.Replace("'", "''")}'; ";
        script += @"if ($dialog.ShowDialog() -eq 'OK') { $dialog.FileName }";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -Command \"{script}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return null;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
    }

    private static async Task<string?> SaveFileMacOSAsync(string defaultFileName, string? initialPath)
    {
        var initialDir = !string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath)
            ? $"default location \"{initialPath}\""
            : "";

        var script = $@"tell application ""Finder""
            activate
            set theFile to choose file name with prompt ""Save As"" default name ""{defaultFileName}"" {initialDir}
            return POSIX path of theFile
        end tell";

        var psi = new ProcessStartInfo
        {
            FileName = "osascript",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return null;

        await process.StandardInput.WriteAsync(script);
        process.StandardInput.Close();

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
    }

    private static async Task<string?> SaveFileLinuxAsync(string defaultFileName, string filter, string? initialPath)
    {
        var args = $"--file-selection --save --confirm-overwrite --title=\"Save As\" --filename=\"{defaultFileName}\"";
        if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
            args = $"--file-selection --save --confirm-overwrite --title=\"Save As\" --filename=\"{Path.Combine(initialPath, defaultFileName)}\"";
        
        // Add file filter for zip
        if (filter.Contains("*.zip"))
            args += " --file-filter=\"Zip files | *.zip\"";

        var psi = new ProcessStartInfo
        {
            FileName = "zenity",
            Arguments = args,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return null;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output.Trim() : null;
    }

    /// <summary>
    /// Opens a file picker dialog configured for selecting instance archive files (ZIP or PWR).
    /// </summary>
    public async Task<string?> BrowseInstanceArchiveAsync()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await BrowseInstanceArchiveWindowsAsync();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return await BrowseInstanceArchiveMacOSAsync();
            }
            else
            {
                return await BrowseInstanceArchiveLinuxAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Files", $"Failed to browse instance archive: {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> BrowseInstanceArchiveWindowsAsync()
    {
        var script = @"Add-Type -AssemblyName System.Windows.Forms; $dialog = New-Object System.Windows.Forms.OpenFileDialog; $dialog.Filter = 'Instance Archives (*.zip;*.pwr)|*.zip;*.pwr|Zip Files (*.zip)|*.zip|PWR Files (*.pwr)|*.pwr|All Files (*.*)|*.*'; $dialog.Multiselect = $false; $dialog.Title = 'Select Instance Archive'; if ($dialog.ShowDialog() -eq 'OK') { $dialog.FileName }";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -Command \"{script}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return null;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
    }

    private static async Task<string?> BrowseInstanceArchiveMacOSAsync()
    {
        var script = @"tell application ""Finder""
            activate
            set theFile to choose file with prompt ""Select Instance Archive"" of type {""zip"", ""public.zip-archive"", ""pwr"", ""public.data""}
            return POSIX path of theFile
        end tell";

        var psi = new ProcessStartInfo
        {
            FileName = "osascript",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return null;

        await process.StandardInput.WriteAsync(script);
        process.StandardInput.Close();

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output.Trim() : null;
    }

    private static async Task<string?> BrowseInstanceArchiveLinuxAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "zenity",
            Arguments = "--file-selection --title=\"Select Instance Archive\" --file-filter=\"Instance Archives | *.zip *.pwr\" --file-filter=\"All files | *\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return null;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output.Trim() : null;
    }

    /// <summary>
    /// Opens a file picker dialog configured for selecting ZIP files.
    /// </summary>
    public async Task<string?> BrowseZipFileAsync()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await BrowseZipFileWindowsAsync();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return await BrowseZipFileMacOSAsync();
            }
            else
            {
                return await BrowseZipFileLinuxAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("Files", $"Failed to browse zip file: {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> BrowseZipFileWindowsAsync()
    {
        var script = @"Add-Type -AssemblyName System.Windows.Forms; $dialog = New-Object System.Windows.Forms.OpenFileDialog; $dialog.Filter = 'Zip Files (*.zip)|*.zip|All Files (*.*)|*.*'; $dialog.Multiselect = $false; $dialog.Title = 'Select Instance Archive'; if ($dialog.ShowDialog() -eq 'OK') { $dialog.FileName }";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -Command \"{script}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return null;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
    }

    private static async Task<string?> BrowseZipFileMacOSAsync()
    {
        var script = @"tell application ""Finder""
            activate
            set theFile to choose file with prompt ""Select Instance Archive"" of type {""zip"", ""public.zip-archive""}
            return POSIX path of theFile
        end tell";

        var psi = new ProcessStartInfo
        {
            FileName = "osascript",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return null;

        await process.StandardInput.WriteAsync(script);
        process.StandardInput.Close();

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output.Trim() : null;
    }

    private static async Task<string?> BrowseZipFileLinuxAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "zenity",
            Arguments = "--file-selection --title=\"Select Instance Archive\" --file-filter=\"Zip files | *.zip\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return null;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output.Trim() : null;
    }
}
