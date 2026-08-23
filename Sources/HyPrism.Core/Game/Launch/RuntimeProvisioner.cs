// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using HyPrism.Core.Infrastructure;

namespace HyPrism.Core.Game.Launch;

/// <summary>
/// Manages launch prerequisites including Java Runtime Environment and Visual C++ Redistributable.
/// Downloads and installs required runtimes before game launch
/// </summary>
/// <remarks>
/// Uses the official Hytale JRE distribution for maximum compatibility.
/// On Windows, also ensures the Visual C++ Redistributable is installed
/// </remarks>
public partial class RuntimeProvisioner : IRuntimeProvisioner
{
    private const string RequiredJreVersion = "25.0.1_8";
    private const string VCRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";

    private readonly string _appDir;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="RuntimeProvisioner"/> class
    /// </summary>
    /// <param name="appDir">The application data directory path</param>
    /// <param name="httpClient">The HTTP client for downloading runtimes</param>
    public RuntimeProvisioner(string appDir, HttpClient httpClient)
    {
        _appDir = appDir;
        _httpClient = httpClient;
    }

    #region JRE Management

    /// <inheritdoc/>
    public async Task EnsureJREInstalledAsync(
        Action<int, string> progressCallback,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

        string versionMarkerPath = Path.Combine(jreDir, ".jre_version");

        if (File.Exists(javaBin) && File.Exists(versionMarkerPath))
        {
            try
            {
                string installedVersion = await File.ReadAllTextAsync(versionMarkerPath, cancellationToken);
                if (installedVersion.Trim() == RequiredJreVersion)
                {
                    Logger.Info("JRE", $"Java Runtime {RequiredJreVersion} already installed");
                    await EnsureJavaWrapperAsync(javaBin, cancellationToken);
                    progressCallback(100, "Java Runtime ready");
                    return;
                }
                Logger.Warning("JRE", $"Installed JRE version {installedVersion.Trim()} != required {RequiredJreVersion}. Reinstalling...");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warning("JRE", $"Failed to check JRE version: {ex.Message}. Reinstalling...");
            }
        }
        else if (File.Exists(javaBin))
        {
            Logger.Warning("JRE", "JRE version marker not found. Reinstalling official Hytale JRE...");
        }

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

        string osName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin" :
                        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" : "linux";
        string arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "amd64";
        string archiveType = osName == "windows" ? "zip" : "tar.gz";

        string? url = null;
        string? expectedSha256 = null;
        try
        {
            Logger.Info("JRE", "Fetching JRE info from launcher.hytale.com...");
            var jreInfoResponse = await _httpClient.GetStringAsync(
                "https://launcher.hytale.com/version/release/jre.json",
                cancellationToken);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warning("JRE", $"Failed to fetch from launcher.hytale.com: {ex.Message}");
        }

        if (string.IsNullOrEmpty(url))
        {
            try
            {
                var jreConfigPath = Path.Combine(AppContext.BaseDirectory, "jre.json");
                if (File.Exists(jreConfigPath))
                {
                    var jreConfigJson = await File.ReadAllTextAsync(jreConfigPath, cancellationToken);
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warning("JRE", $"Failed to load local jre.json: {ex.Message}");
            }
        }

        if (string.IsNullOrEmpty(url))
        {
            url = $"https://launcher.hytale.com/redist/jre/{osName}/{arch}/jre-{RequiredJreVersion}.{archiveType}";
            Logger.Info("JRE", $"Using hardcoded Hytale JRE URL: {url}");
        }

        string cacheDir = Path.Combine(_appDir, "Cache");
        Directory.CreateDirectory(cacheDir);
        string archivePath = Path.Combine(cacheDir, $"jre.{archiveType}");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", LauncherUserAgent.Value);
        request.Headers.Add("Accept", "*/*");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192);

        var buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalRead += bytesRead;

            if (totalBytes > 0)
            {
                var progress = (int)(totalRead * 80 / totalBytes);
                progressCallback(progress, $"Downloading Java Runtime... {progress}%");
            }
        }
        await fileStream.FlushAsync(cancellationToken);
        fileStream.Close();

        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            await using var archiveStream = File.OpenRead(archivePath);
            var actualSha256 = Convert.ToHexString(
                await SHA256.HashDataAsync(archiveStream, cancellationToken));
            if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(archivePath);
                throw new InvalidDataException("Downloaded Java Runtime failed SHA-256 verification");
            }
        }

        progressCallback(85, "Extracting Java Runtime...");
        Logger.Info("JRE", "Extracting Java Runtime...");

        Directory.CreateDirectory(jreDir);

        if (archiveType == "zip")
        {
            await ZipFile.ExtractToDirectoryAsync(
                archivePath,
                jreDir,
                overwriteFiles: true,
                cancellationToken);
        }
        else
        {
            var tarProcess = new ProcessStartInfo("tar", $"-xzf \"{archivePath}\" -C \"{jreDir}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var tar = Process.Start(tarProcess);
            if (tar is not null)
            {
                await tar.WaitForExitAsync(cancellationToken);
                if (tar.ExitCode != 0)
                {
                    throw new InvalidDataException($"Java Runtime extraction failed with exit code {tar.ExitCode}");
                }
            }
        }

        var entries = Directory.GetDirectories(jreDir);
        if (entries.Length == 1)
        {
            var subDir = entries[0];

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var contentsDir = Path.Combine(subDir, "Contents", "Home");
                if (Directory.Exists(contentsDir))
                {
                    subDir = contentsDir;
                }
            }

            foreach (var entry in Directory.GetFileSystemEntries(subDir))
            {
                var name = Path.GetFileName(entry);
                var dest = Path.Combine(jreDir, name);
                if (!File.Exists(dest) && !Directory.Exists(dest))
                {
                    Directory.Move(entry, dest);
                }
            }

            try { Directory.Delete(entries[0], true); } catch { }
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var chmod = new ProcessStartInfo("chmod", $"+x \"{javaBin}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var chmodProcess = Process.Start(chmod);
            if (chmodProcess is not null)
                await chmodProcess.WaitForExitAsync(cancellationToken);
        }

        try { File.Delete(archivePath); } catch { }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            await SetupMacOSJavaSymlinksAsync(jreDir, cancellationToken);
        }

        await EnsureJavaWrapperAsync(javaBin, cancellationToken);

        try
        {
            await File.WriteAllTextAsync(versionMarkerPath, RequiredJreVersion, cancellationToken);
            Logger.Info("JRE", $"Written version marker: {RequiredJreVersion}");
        }
        catch (Exception ex)
        {
            Logger.Warning("JRE", $"Failed to write version marker: {ex.Message}");
        }

        progressCallback(100, "Java Runtime installed");
        Logger.Success("JRE", $"Hytale Java Runtime {RequiredJreVersion} installed successfully");
    }

    private async Task SetupMacOSJavaSymlinksAsync(
        string jreDir,
        CancellationToken cancellationToken)
    {
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

                var lnBin = new ProcessStartInfo("ln", $"-sf \"{Path.Combine(jreDir, "bin")}\" \"{Path.Combine(javaDir, "Contents", "Home", "bin")}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var binLinkProcess = Process.Start(lnBin);
                if (binLinkProcess is not null)
                    await binLinkProcess.WaitForExitAsync(cancellationToken);

                var lnLib = new ProcessStartInfo("ln", $"-sf \"{Path.Combine(jreDir, "lib")}\" \"{Path.Combine(javaDir, "Contents", "Home", "lib")}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var libLinkProcess = Process.Start(lnLib);
                if (libLinkProcess is not null)
                    await libLinkProcess.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warning("JRE", $"Failed to create Java symlinks: {ex.Message}");
            }
        }

        Logger.Info("JRE", "Signing Java Runtime...");
        await RunSilentProcessAsync("xattr", $"-cr \"{jreDir}\"", cancellationToken);
        await RunSilentProcessAsync("codesign", $"--force --deep --sign - \"{jreDir}\"", cancellationToken);
    }

    /// <inheritdoc/>
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
            var match = JavaVersionRegex().Match(combined);
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

    private static int ParseJavaMajor(string versionString)
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

    /// <inheritdoc/>
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

    private const string WrapperVersion = "v4";

    private static async Task EnsureJavaWrapperAsync(
        string javaBin,
        CancellationToken cancellationToken)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
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
                        byte[] headBytes = new byte[2];
                        await using var fs = new FileStream(
                            javaBin,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite,
                            bufferSize: 2,
                            useAsync: true);
                        _ = await fs.ReadAsync(headBytes, cancellationToken);

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

            var versionMarker = Path.Combine(javaDir, ".wrapper-version");
            if (File.Exists(javaBin) && File.Exists(versionMarker))
            {
                try
                {
                    if ((await File.ReadAllTextAsync(versionMarker, cancellationToken)).Trim() == WrapperVersion)
                        return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch { }
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

            await File.WriteAllTextAsync(javaBin, wrapper, cancellationToken);
            await File.WriteAllTextAsync(versionMarker, WrapperVersion, cancellationToken);
            var chmod = new ProcessStartInfo("chmod", $"+x \"{javaBin}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var chmodProcess = Process.Start(chmod);
            if (chmodProcess is not null)
                await chmodProcess.WaitForExitAsync(cancellationToken);
            Logger.Info("JRE", $"Java wrapper script installed/updated ({WrapperVersion})");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warning("JRE", $"Failed to create Java wrapper: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public string GetJavaPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
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
    /// Uses registry check for VC++ 14.x (Visual Studio 2015-2022)
    /// </summary>
    public bool IsVCRedistInstalled()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return true;
        }

        try
        {
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
    /// Downloads and runs the installer if not present
    /// </summary>
    public async Task EnsureVCRedistInstalledAsync(
        Action<int, string> progressCallback,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
            using var response = await _httpClient.GetAsync(
                VCRedistUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long downloadedBytes = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    int percent = (int)(downloadedBytes * 50 / totalBytes);
                    progressCallback(percent, $"Downloading VC++ Redistributable... {percent * 2}%");
                }
            }

            Logger.Info("VCRedist", "Download complete, running installer...");
            progressCallback(50, "Installing Visual C++ Redistributable...");

            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/install /quiet /norestart",
                UseShellExecute = true,
                Verb = "runas"
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode == 0 || process.ExitCode == 1638)
                {
                    Logger.Success("VCRedist", "VC++ Redistributable installed successfully");
                    progressCallback(100, "VC++ Redistributable installed");
                }
                else if (process.ExitCode == 3010)
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

            try { File.Delete(installerPath); }
            catch { }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error("VCRedist", $"Failed to install VC++ Redistributable: {ex.Message}");
            progressCallback(100, "VC++ installation skipped");
        }
    }

    #endregion

    #region Utilities

    private static async Task RunSilentProcessAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
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

            using var process = Process.Start(psi);
            if (process is not null)
                await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warning("Process", $"Failed to run {fileName} {arguments}: {ex.Message}");
        }
    }

    [GeneratedRegex("version \"?([0-9][^\"\\s]*)")]
    private static partial Regex JavaVersionRegex();

    #endregion
}
