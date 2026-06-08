using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.Game.Launch;

/// <summary>
/// Manages launch prerequisites including Java Runtime Environment and Visual C++ Redistributable.
/// Downloads and installs required runtimes before game launch.
/// </summary>
/// <remarks>
/// Uses the official Hytale JRE distribution for maximum compatibility.
/// On Windows, also ensures the Visual C++ Redistributable is installed.
/// </remarks>
public class LaunchService : ILaunchService
{
    private const string RequiredJreVersion = "25.0.1_8";
    private const string VCRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
    
    private readonly string _appDir;
    private readonly HttpClient _httpClient;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="LaunchService"/> class.
    /// </summary>
    /// <param name="appDir">The application data directory path.</param>
    /// <param name="httpClient">The HTTP client for downloading runtimes.</param>
    public LaunchService(string appDir, HttpClient httpClient)
    {
        _appDir = appDir;
        _httpClient = httpClient;
    }

    #region JRE Management

    /// <inheritdoc/>
    public async Task EnsureJREInstalledAsync(Action<int, string> progressCallback)
    {
        string jreDir = Path.Combine(_appDir, "Jre");
        string javaBin;
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            javaBin = Path.Combine(jreDir, "bin", "java.exe");
        }
        else
        {
            javaBin = Path.Combine(jreDir, "bin", "java");
        }
        
        // Check if correct JRE version is installed by looking for version marker file
        string versionMarkerPath = Path.Combine(jreDir, ".jre_version");
        
        if (File.Exists(javaBin) && File.Exists(versionMarkerPath))
        {
            try
            {
                string installedVersion = await File.ReadAllTextAsync(versionMarkerPath);
                if (installedVersion.Trim() == RequiredJreVersion)
                {
                    Logger.Info("JRE", $"Java Runtime {RequiredJreVersion} already installed");
                    EnsureJavaWrapper(javaBin);
                    progressCallback(100, "Java Runtime ready");
                    return;
                }
                Logger.Warning("JRE", $"Installed JRE version {installedVersion.Trim()} != required {RequiredJreVersion}. Reinstalling...");
            }
            catch (Exception ex)
            {
                Logger.Warning("JRE", $"Failed to check JRE version: {ex.Message}. Reinstalling...");
            }
        }
        else if (File.Exists(javaBin))
        {
            // Old installation without version marker - reinstall
            Logger.Warning("JRE", "JRE version marker not found. Reinstalling official Hytale JRE...");
        }
        
        // Delete old JRE if exists
        if (Directory.Exists(jreDir))
        {
            try
            {
                Directory.Delete(jreDir, true);
                Logger.Info("JRE", "Removed old JRE installation");
            }
            catch (Exception ex)
            {
                Logger.Warning("JRE", $"Failed to remove old JRE: {ex.Message}");
            }
        }
        
        progressCallback(0, "Downloading Java Runtime...");
        Logger.Info("JRE", "Downloading official Hytale Java Runtime...");
        
        // Determine platform - Hytale uses different naming convention
        string osName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin" : 
                        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" : "linux";
        string arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "amd64";
        string archiveType = osName == "windows" ? "zip" : "tar.gz";
        
        // First try to fetch latest JRE info from Hytale launcher directly
        string? url = null;
        string? expectedSha256 = null;
        
        try
        {
            Logger.Info("JRE", "Fetching JRE info from launcher.hytale.com...");
            var jreInfoResponse = await _httpClient.GetStringAsync("https://launcher.hytale.com/version/release/jre.json");
            var jreInfo = JsonSerializer.Deserialize<JsonElement>(jreInfoResponse);
            
            if (jreInfo.TryGetProperty("download_url", out var downloadUrls) &&
                downloadUrls.TryGetProperty(osName, out var osUrls) &&
                osUrls.TryGetProperty(arch, out var archInfo))
            {
                if (archInfo.TryGetProperty("url", out var urlProp))
                {
                    url = urlProp.GetString();
                }
                if (archInfo.TryGetProperty("sha256", out var sha256Prop))
                {
                    expectedSha256 = sha256Prop.GetString();
                }
                Logger.Info("JRE", $"Got JRE URL from Hytale launcher: {url}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("JRE", $"Failed to fetch from launcher.hytale.com: {ex.Message}");
        }
        
        // Fallback to local jre.json config
        if (string.IsNullOrEmpty(url))
        {
            try
            {
                var jreConfigPath = Path.Combine(AppContext.BaseDirectory, "jre.json");
                if (File.Exists(jreConfigPath))
                {
                    var jreConfigJson = await File.ReadAllTextAsync(jreConfigPath);
                    var jreConfig = JsonSerializer.Deserialize<JsonElement>(jreConfigJson);
                    
                    if (jreConfig.TryGetProperty("download_url", out var downloadUrls) &&
                        downloadUrls.TryGetProperty(osName, out var osUrls) &&
                        osUrls.TryGetProperty(arch, out var archInfo))
                    {
                        if (archInfo.TryGetProperty("url", out var urlProp))
                        {
                            url = urlProp.GetString();
                        }
                        if (archInfo.TryGetProperty("sha256", out var sha256Prop))
                        {
                            expectedSha256 = sha256Prop.GetString();
                        }
                        Logger.Info("JRE", $"Using JRE URL from local config: {url}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("JRE", $"Failed to load local jre.json: {ex.Message}");
            }
        }
        
        // Ultimate fallback - hardcoded URLs for official Hytale JRE
        if (string.IsNullOrEmpty(url))
        {
            url = $"https://launcher.hytale.com/redist/jre/{osName}/{arch}/jre-{RequiredJreVersion}.{archiveType}";
            Logger.Info("JRE", $"Using hardcoded Hytale JRE URL: {url}");
        }
        
        string cacheDir = Path.Combine(_appDir, "Cache");
        Directory.CreateDirectory(cacheDir);
        string archivePath = Path.Combine(cacheDir, $"jre.{archiveType}");
        
        // Download with proper headers for Adoptium API
        // Reuse injected HttpClient instead of creating a new one (avoids socket exhaustion)
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "HyPrism/1.0");
        request.Headers.Add("Accept", "*/*");
        
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        
        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        using var stream = await response.Content.ReadAsStreamAsync();
        using var fileStream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192);
        
        var buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;
        
        while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            totalRead += bytesRead;
            
            if (totalBytes > 0)
            {
                var progress = (int)((totalRead * 80) / totalBytes); // 0-80%
                progressCallback(progress, $"Downloading Java Runtime... {progress}%");
            }
        }
        fileStream.Close();
        
        progressCallback(85, "Extracting Java Runtime...");
        Logger.Info("JRE", "Extracting Java Runtime...");
        
        // Create jre directory
        Directory.CreateDirectory(jreDir);
        
        // Extract
        if (archiveType == "zip")
        {
            ZipFile.ExtractToDirectory(archivePath, jreDir, true);
        }
        else
        {
            // Use tar on Unix systems
            var tarProcess = new ProcessStartInfo("tar", $"-xzf \"{archivePath}\" -C \"{jreDir}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var tar = Process.Start(tarProcess);
            tar?.WaitForExit();
        }
        
        // Normalize JRE structure - move contents up if nested
        var entries = Directory.GetDirectories(jreDir);
        if (entries.Length == 1)
        {
            var subDir = entries[0];
            
            // On macOS, structure is different
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var contentsDir = Path.Combine(subDir, "Contents", "Home");
                if (Directory.Exists(contentsDir))
                {
                    subDir = contentsDir;
                }
            }
            
            // Move files from subdirectory to jreDir
            foreach (var entry in Directory.GetFileSystemEntries(subDir))
            {
                var name = Path.GetFileName(entry);
                var dest = Path.Combine(jreDir, name);
                if (!File.Exists(dest) && !Directory.Exists(dest))
                {
                    Directory.Move(entry, dest);
                }
            }
            
            // Remove empty subdirectory
            try { Directory.Delete(entries[0], true); } catch { }
        }
        
        // Make java executable on Unix
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var chmod = new ProcessStartInfo("chmod", $"+x \"{javaBin}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(chmod)?.WaitForExit();
        }
        
        // Cleanup archive
        try { File.Delete(archivePath); } catch { }
        
        // On macOS, create java symlink structure like old launcher
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            await SetupMacOSJavaSymlinksAsync(jreDir);
        }

        // Wrap java to strip unsupported flags and point to the freshly installed JRE
        EnsureJavaWrapper(javaBin);
        
        // Write version marker file to track installed version
        try
        {
            await File.WriteAllTextAsync(versionMarkerPath, RequiredJreVersion);
            Logger.Info("JRE", $"Written version marker: {RequiredJreVersion}");
        }
        catch (Exception ex)
        {
            Logger.Warning("JRE", $"Failed to write version marker: {ex.Message}");
        }
        
        progressCallback(100, "Java Runtime installed");
        Logger.Success("JRE", $"Hytale Java Runtime {RequiredJreVersion} installed successfully");
    }

    private async Task SetupMacOSJavaSymlinksAsync(string jreDir)
    {
        // Create java directory structure like old launcher
        string javaDir = Path.Combine(_appDir, "java");
        string javaHomeBin = Path.Combine(javaDir, "Contents", "Home", "bin");
        
        if (!Directory.Exists(javaHomeBin))
        {
            try
            {
                if (Directory.Exists(javaDir))
                {
                    Directory.Delete(javaDir, true);
                }
                
                Directory.CreateDirectory(Path.Combine(javaDir, "Contents", "Home"));
                
                // Create symlinks
                var lnBin = new ProcessStartInfo("ln", $"-sf \"{Path.Combine(jreDir, "bin")}\" \"{Path.Combine(javaDir, "Contents", "Home", "bin")}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(lnBin)?.WaitForExit();
                
                var lnLib = new ProcessStartInfo("ln", $"-sf \"{Path.Combine(jreDir, "lib")}\" \"{Path.Combine(javaDir, "Contents", "Home", "lib")}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(lnLib)?.WaitForExit();
            }
            catch (Exception ex)
            {
                Logger.Warning("JRE", $"Failed to create Java symlinks: {ex.Message}");
            }
        }
        
        // Sign JRE
        Logger.Info("JRE", "Signing Java Runtime...");
        RunSilentProcess("xattr", $"-cr \"{jreDir}\"");
        RunSilentProcess("codesign", $"--force --deep --sign - \"{jreDir}\"");
        await Task.CompletedTask;
    }

    public async Task<int> GetJavaFeatureVersionAsync(string javaBin)
    {
        try
        {
            var psi = new ProcessStartInfo(javaBin, "-version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var proc = Process.Start(psi);
            if (proc == null)
            {
                return 0;
            }

            string stderr = await proc.StandardError.ReadToEndAsync();
            string stdout = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var combined = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout + "\n" + stderr;
            var match = Regex.Match(combined, "version \"?([0-9][^\"\\s]*)");
            if (match.Success)
            {
                return ParseJavaMajor(match.Groups[1].Value);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("JRE", $"Failed to read Java version: {ex.Message}");
        }

        return 0;
    }

    private int ParseJavaMajor(string versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString))
        {
            return 0;
        }

        var parts = versionString.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return 0;
        }

        if (int.TryParse(parts[0], out var major))
        {
            if (major == 1 && parts.Length > 1 && int.TryParse(parts[1], out var minor))
            {
                return minor;
            }

            return major;
        }

        return 0;
    }

    public async Task<bool> SupportsShenandoahAsync(string javaBin)
    {
        try
        {
            var psi = new ProcessStartInfo(javaBin, "-XX:+UseShenandoahGC -version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var proc = Process.Start(psi);
            if (proc == null)
            {
                return false;
            }

            string stderr = await proc.StandardError.ReadToEndAsync();
            string stdout = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            if (proc.ExitCode == 0)
            {
                return true;
            }

            var combined = (stdout + "\n" + stderr).ToLowerInvariant();
            if (combined.Contains("unrecognized") || combined.Contains("could not create the java virtual machine"))
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("JRE", $"Shenandoah probe failed: {ex.Message}");
        }

        return false;
    }

    private const string WrapperVersion = "v4"; // Bump when wrapper content changes

    private void EnsureJavaWrapper(string javaBin)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows already uses java.exe; wrapper not required.
            return;
        }

        try
        {
            var javaDir = Path.GetDirectoryName(javaBin);
            if (string.IsNullOrEmpty(javaDir))
            {
                return;
            }

            var realJava = Path.Combine(javaDir, "java.real");

            if (!File.Exists(realJava))
            {
                try
                {
                    if (File.Exists(javaBin))
                    {
                        // If javaBin is already a wrapper script, check if it's ours
                        byte[] headBytes = new byte[2];
                        using (var fs = new FileStream(javaBin, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            _ = fs.Read(headBytes, 0, 2);
                        }

                        bool looksLikeScript = headBytes[0] == (byte)'#' && headBytes[1] == (byte)'!';
                        if (looksLikeScript)
                        {
                            Logger.Warning("JRE", "Wrapper detected but java.real missing; cannot install wrapper without original java binary");
                            return;
                        }

                        File.Move(javaBin, realJava, true);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning("JRE", $"Failed to move java binary for wrapping: {ex.Message}");
                    return;
                }
            }

            // Check if wrapper is already up-to-date via version marker
            var versionMarker = Path.Combine(javaDir, ".wrapper-version");
            if (File.Exists(javaBin) && File.Exists(versionMarker))
            {
                try
                {
                    if (File.ReadAllText(versionMarker).Trim() == WrapperVersion)
                        return; // Wrapper is current
                }
                catch { /* Re-write if we can't read */ }
            }

            var wrapper = "#!/bin/bash\n" +
                         "REAL_JAVA=\"$(cd \"$(dirname \"$0\")\" && pwd)/java.real\"\n" +
                         "ARGS=()\n" +
                         "IS_SERVER=false\n" +
                         "for arg in \"$@\"; do\n" +
                         "  if [[ \"$arg\" == -XX:ShenandoahGCMode=* ]]; then\n" +
                         "    continue\n" +
                         "  fi\n" +
                         "  if [[ \"$arg\" == *\"Server\"* ]] || [[ \"$arg\" == *\"server\"* ]]; then\n" +
                         "    IS_SERVER=true\n" +
                         "  fi\n" +
                         "  ARGS+=(\"$arg\")\n" +
                         "done\n" +
                         "if $IS_SERVER; then\n" +
                         "  if [[ \"$JAVA_TOOL_OPTIONS\" != *\"-javaagent:\"* ]]; then\n" +
                         "    unset JAVA_TOOL_OPTIONS\n" +
                         "  fi\n" +
                         "fi\n" +
                         "exec \"$REAL_JAVA\" \"${ARGS[@]}\"\n";

            File.WriteAllText(javaBin, wrapper);
            File.WriteAllText(versionMarker, WrapperVersion);
            var chmod = new ProcessStartInfo("chmod", $"+x \"{javaBin}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(chmod)?.WaitForExit();
            Logger.Info("JRE", $"Java wrapper script installed/updated ({WrapperVersion})");
        }
        catch (Exception ex)
        {
            Logger.Warning("JRE", $"Failed to create Java wrapper: {ex.Message}");
        }
    }

    public string GetJavaPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Use the symlinked java path on macOS like old launcher
            return Path.Combine(_appDir, "java", "Contents", "Home", "bin", "java");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(_appDir, "Jre", "bin", "java.exe");
        }
        else
        {
            return Path.Combine(_appDir, "Jre", "bin", "java");
        }
    }

    #endregion

    #region VC++ Redistributable (Windows)

    /// <summary>
    /// Checks if Visual C++ Redistributable is installed on Windows.
    /// Uses registry check for VC++ 14.x (Visual Studio 2015-2022).
    /// </summary>
    public bool IsVCRedistInstalled()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return true; // Not Windows - not needed
        }

        try
        {
            // Check registry for VC++ 14.x (VS 2015-2022 uses the same redistributable)
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
            
            if (key != null)
            {
                var installed = key.GetValue("Installed");
                if (installed != null && installed.ToString() == "1")
                {
                    Logger.Info("VCRedist", "Visual C++ Redistributable is already installed");
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("VCRedist", $"Failed to check VC++ registry: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Ensures Visual C++ Redistributable is installed on Windows.
    /// Downloads and runs the installer if not present.
    /// </summary>
    public async Task EnsureVCRedistInstalledAsync(Action<int, string> progressCallback)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            progressCallback(100, "VC++ not required on this platform");
            return;
        }
        
        if (IsVCRedistInstalled())
        {
            progressCallback(100, "VC++ Redistributable ready");
            return;
        }
        
        progressCallback(0, "Downloading Visual C++ Redistributable...");
        Logger.Info("VCRedist", "Downloading VC++ Redistributable...");
        
        string cacheDir = Path.Combine(_appDir, "Cache");
        Directory.CreateDirectory(cacheDir);
        string installerPath = Path.Combine(cacheDir, "vc_redist.x64.exe");
        
        try
        {
            // Download the installer
            using var response = await _httpClient.GetAsync(VCRedistUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            
            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
            
            var buffer = new byte[8192];
            long downloadedBytes = 0;
            int bytesRead;
            
            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                downloadedBytes += bytesRead;
                
                if (totalBytes > 0)
                {
                    int percent = (int)((downloadedBytes * 50) / totalBytes); // 0-50%
                    progressCallback(percent, $"Downloading VC++ Redistributable... {percent * 2}%");
                }
            }
            
            Logger.Info("VCRedist", "Download complete, running installer...");
            progressCallback(50, "Installing Visual C++ Redistributable...");
            
            // Run the installer silently
            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/install /quiet /norestart",
                UseShellExecute = true,
                Verb = "runas" // Request elevation
            };
            
            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                
                if (process.ExitCode == 0 || process.ExitCode == 1638) // 1638 = already installed
                {
                    Logger.Success("VCRedist", "VC++ Redistributable installed successfully");
                    progressCallback(100, "VC++ Redistributable installed");
                }
                else if (process.ExitCode == 3010) // Restart required
                {
                    Logger.Success("VCRedist", "VC++ Redistributable installed (restart may be required)");
                    progressCallback(100, "VC++ Redistributable installed");
                }
                else
                {
                    Logger.Warning("VCRedist", $"VC++ installer exited with code: {process.ExitCode}");
                    progressCallback(100, "VC++ installation completed");
                }
            }
            
            // Clean up installer
            try { File.Delete(installerPath); } catch { }
        }
        catch (Exception ex)
        {
            Logger.Error("VCRedist", $"Failed to install VC++ Redistributable: {ex.Message}");
            // Don't fail the game launch - the game might work anyway
            progressCallback(100, "VC++ installation skipped");
        }
    }

    #endregion

    #region Utilities

    private void RunSilentProcess(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var proc = Process.Start(psi);
            proc?.WaitForExit();
        }
        catch (Exception ex)
        {
            Logger.Warning("Process", $"Failed to run {fileName} {arguments}: {ex.Message}");
        }
    }

    #endregion
}
