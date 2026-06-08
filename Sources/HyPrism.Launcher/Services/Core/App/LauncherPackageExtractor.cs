using System.Diagnostics;
using System.IO.Compression;

namespace HyPrism.Services.Core.App;

/// <summary>
/// Provides static helpers for extracting and locating launcher update packages
/// across all supported archive formats (.zip, .tar.gz) and platforms.
/// </summary>
public static class LauncherPackageExtractor
{
    /// <summary>
    /// Extracts a Windows update ZIP archive and returns the path of the
    /// launcher executable inside it, preferring an exe with the same name
    /// as the currently running process.
    /// </summary>
    /// <param name="zipPath">The path to the ZIP archive.</param>
    /// <returns>The path to the best matching .exe file, or <c>null</c> if none found.</returns>
    public static string? ExtractZipAndFindWindowsExe(string zipPath)
    {
        var extractDir = Path.Combine(Path.GetTempPath(), "HyPrism", "launcher-update", "extract", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractDir);
        ZipFile.ExtractToDirectory(zipPath, extractDir, true);

        var currentExe = Environment.ProcessPath;
        var preferredName = string.IsNullOrWhiteSpace(currentExe) ? null : Path.GetFileName(currentExe);
        var exes = Directory.GetFiles(extractDir, "*.exe", SearchOption.AllDirectories);

        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            var match = exes.FirstOrDefault(e => string.Equals(Path.GetFileName(e), preferredName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match)) return match;
        }

        return exes.FirstOrDefault();
    }

    /// <summary>
    /// Finds the primary launcher executable inside an extracted directory.
    /// Prefers a file named "HyPrism"; otherwise returns the first non-metadata file.
    /// </summary>
    /// <param name="rootDir">The root directory to search.</param>
    /// <returns>The path to the best matching executable file, or <c>null</c> if none found.</returns>
    public static string? FindExecutableFile(string rootDir)
    {
        try
        {
            var files = Directory.GetFiles(rootDir, "*", SearchOption.AllDirectories);
            var direct = files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), "HyPrism", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(direct)) return direct;

            return files.FirstOrDefault(f =>
                !f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                !f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                !f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) &&
                !f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts a .tar.gz archive to the specified destination directory
    /// using the system <c>tar</c> command.
    /// </summary>
    /// <param name="archivePath">The path to the .tar.gz archive.</param>
    /// <param name="destinationDir">The directory to extract into.</param>
    /// <exception cref="Exception">Thrown if <c>tar</c> exits with a non-zero code.</exception>
    public static async Task ExtractTarGz(string archivePath, string destinationDir)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "tar",
                Arguments = $"-xzf \"{archivePath}\" -C \"{destinationDir}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new Exception($"Failed to extract tar.gz: {error}");
        }
    }
}
