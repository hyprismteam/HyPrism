// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using HyPrism.Core.Infrastructure;

namespace HyPrism.Core.Game.Patching;

/// <summary>
/// Provides functionality for managing the Butler patching tool.
/// Butler is used for applying differential game updates via PWR patch files
/// </summary>
public partial class ButlerClient : IButlerClient
{
    private const string BrothUrlTemplate = "https://broth.itch.zone/butler/{0}-{1}/LATEST/archive/default";

    private readonly string _butlerDir;
    private readonly string _cacheDir;
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>
    /// Initializes a new instance of the <see cref="ButlerClient"/> class.
    /// Creates the Butler and Cache directories if they don't exist
    /// </summary>
    /// <param name="appDir">The application data directory path</param>
    public ButlerClient(string appDir)
    {
        _butlerDir = Path.Combine(appDir, "Butler");
        _cacheDir = Path.Combine(appDir, "Cache");
        Directory.CreateDirectory(_butlerDir);
        Directory.CreateDirectory(_cacheDir);
    }

    /// <inheritdoc/>
    public string GetButlerPath()
    {
        string name = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "butler.exe" : "butler";
        return Path.Combine(_butlerDir, name);
    }

    /// <inheritdoc/>
    public bool IsButlerInstalled()
    {
        string path = GetButlerPath();
        return File.Exists(path);
    }

    /// <inheritdoc/>
    public async Task<string> EnsureButlerInstalledAsync(
        Action<int, string>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_butlerDir);
        Directory.CreateDirectory(_cacheDir);

        string butlerPath = GetButlerPath();

        if (File.Exists(butlerPath))
        {
            if (await VerifyButlerWorksAsync(butlerPath, cancellationToken))
            {
                progressCallback?.Invoke(100, "launch.detail.butler_ready");
                return butlerPath;
            }
            else
            {
                Logger.Warning("Butler", "Butler exists but is not working, re-downloading...");
                try
                {
                    File.Delete(butlerPath);
                    if (Directory.Exists(_butlerDir))
                    {
                        Directory.Delete(_butlerDir, true);
                        Directory.CreateDirectory(_butlerDir);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning("Butler", $"Failed to clean up: {ex.Message}");
                }
            }
        }

        progressCallback?.Invoke(0, "launch.detail.downloading_butler");

        string osName = LauncherUtilities.GetOS();
        string arch = LauncherUtilities.GetArch();

        string url = string.Format(BrothUrlTemplate, osName, arch);
        Logger.Info("Butler", $"Downloading from: {url}");

        string archivePath = Path.Combine(_cacheDir, "butler.zip");

        try
        {
            using var response = await HttpClient.GetAsync(
                url,
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
                    int progress = (int)(totalRead * 80 / totalBytes);
                    progressCallback?.Invoke(progress, "launch.detail.downloading_butler");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error("Butler", $"Download failed: {ex.Message}");
            throw new Exception($"Failed to download Butler: {ex.Message}");
        }

        progressCallback?.Invoke(85, "Extracting Butler...");

        try
        {
            await ZipFile.ExtractToDirectoryAsync(
                archivePath,
                _butlerDir,
                overwriteFiles: true,
                cancellationToken);
            File.Delete(archivePath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error("Butler", $"Extraction failed: {ex.Message}");
            throw new Exception($"Failed to extract Butler: {ex.Message}");
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                using var chmod = Process.Start(new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{butlerPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (chmod is not null)
                    await chmod.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch { }
        }

        progressCallback?.Invoke(95, "Verifying Butler...");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = butlerPath,
                Arguments = "version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                Logger.Success("Butler", $"Installed: {output.Trim()}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error("Butler", $"Verification failed: {ex.Message}");
            throw new Exception($"Butler verification failed: {ex.Message}");
        }

        progressCallback?.Invoke(100, "launch.detail.butler_ready");
        return butlerPath;
    }

    private static async Task<bool> VerifyButlerWorksAsync(
        string butlerPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = butlerPath,
                Arguments = "version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutCts.Token);
                try
                {
                    await process.WaitForExitAsync(linkedCts.Token);
                    return process.ExitCode == 0;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    process.Kill();
                    throw;
                }
                catch (OperationCanceledException)
                {
                    process.Kill();
                    return false;
                }
            }
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task ApplyPwrAsync(string pwrFile, string targetDir, Action<int, string>? progressCallback = null, CancellationToken externalCancellationToken = default)
    {
        string butlerPath = await EnsureButlerInstalledAsync(
            progressCallback,
            externalCancellationToken);
        string stagingDir = Path.Combine(targetDir, "staging-temp");

        progressCallback?.Invoke(5, "Preparing installation...");

        CleanStagingDirectory(targetDir);

        Directory.CreateDirectory(targetDir);
        Directory.CreateDirectory(stagingDir);

        progressCallback?.Invoke(10, "launch.detail.installing_game");

        Logger.Info("Butler", $"Applying PWR: {pwrFile} -> {targetDir}");

        var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"apply --staging-dir \"{stagingDir}\" --save-interval=60 \"{pwrFile}\" \"{targetDir}\""
            : $"apply --staging-dir \"{stagingDir}\" \"{pwrFile}\" \"{targetDir}\"";

        var psi = new ProcessStartInfo
        {
            FileName = butlerPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = targetDir
        };

        using var process = Process.Start(psi) ?? throw new Exception("Failed to start Butler process");
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, externalCancellationToken);
        var cts = linkedCts;

        int lastProgress = 10;
        using var progressTimer = new System.Timers.Timer(2000);
        progressTimer.Elapsed += (_, _) =>
        {
            while (true)
            {
                var current = Volatile.Read(ref lastProgress);
                if (current >= 90)
                    return;

                var next = Math.Min(current + 2, 90);
                if (Interlocked.CompareExchange(ref lastProgress, next, current) == current)
                {
                    progressCallback?.Invoke(next, "launch.detail.installing_game");
                    return;
                }
            }
        };
        progressTimer.Start();

        string output = "";
        string error = "";

        try
        {
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            async Task ReadOutputAsync()
            {
                try
                {
                    var buffer = new char[1024];
                    while (true)
                    {
                        var read = await process.StandardOutput.ReadAsync(buffer, cts.Token);
                        if (read == 0)
                            break;

                        var chunk = new string(buffer, 0, read);
                        outputBuilder.Append(chunk);

                        if (chunk.Contains('%'))
                        {
                            var match = ProgressPercentageRegex().Match(chunk);
                            if (match.Success && double.TryParse(match.Groups[1].Value,
                                NumberStyles.Any,
                                CultureInfo.InvariantCulture,
                                out var percentage))
                            {
                                var mappedProgress = 10 + (int)(percentage * 0.85);
                                while (true)
                                {
                                    var current = Volatile.Read(ref lastProgress);
                                    if (mappedProgress <= current)
                                        break;

                                    if (Interlocked.CompareExchange(
                                            ref lastProgress,
                                            mappedProgress,
                                            current) == current)
                                    {
                                        progressCallback?.Invoke(mappedProgress, "launch.detail.installing_game");
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Logger.Warning("Butler", $"Output read error: {ex.Message}");
                }
            }

            async Task ReadErrorAsync()
            {
                try
                {
                    errorBuilder.Append(await process.StandardError.ReadToEndAsync(cts.Token));
                }
                catch (OperationCanceledException) { }
                catch { }
            }

            var outputTask = ReadOutputAsync();
            var errorTask = ReadErrorAsync();

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                if (externalCancellationToken.IsCancellationRequested)
                {
                    Logger.Info("Butler", "Butler process cancelled by user");
                    try { process.Kill(); } catch { }
                    CleanStagingDirectory(targetDir);
                    throw new OperationCanceledException("Download cancelled by user.");
                }
                else
                {
                    Logger.Error("Butler", "Butler process timed out after 8 minutes");
                    try { process.Kill(); } catch { }
                    throw new Exception("Installation timed out. Please try again.");
                }
            }

            try
            {
                await Task.WhenAll(outputTask, errorTask).WaitAsync(TimeSpan.FromSeconds(5), externalCancellationToken);
            }
            catch (TimeoutException)
            {
                Logger.Warning("Butler", "Output tasks did not finish in time");
            }

            output = outputBuilder.ToString();
            error = errorBuilder.ToString();
        }
        finally
        {
            progressTimer.Stop();
        }

        if (process.ExitCode != 0)
        {
            Logger.Error("Butler", $"Error output: {error}");
            CleanStagingDirectory(targetDir);
            throw new Exception($"Butler apply failed (exit code {process.ExitCode}): {error}");
        }

        Logger.Debug("Butler", $"Output: {output}");

        CleanStagingDirectory(targetDir);

        progressCallback?.Invoke(98, "Setting permissions...");

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string clientPath = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? Path.Combine(targetDir, "Client", "Hytale.app", "Contents", "MacOS", "HytaleClient")
                : Path.Combine(targetDir, "Client", "HytaleClient");

            if (File.Exists(clientPath))
            {
                try
                {
                    using var chmod = Process.Start(new ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"+x \"{clientPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    if (chmod is not null)
                        await chmod.WaitForExitAsync(externalCancellationToken);
                }
                catch { }
            }
        }

        progressCallback?.Invoke(100, "Installation complete");
        Logger.Success("Butler", "Installation complete");
    }

    private static void CleanStagingDirectory(string gameDir)
    {
        string stagingDir = Path.Combine(gameDir, "staging-temp");

        try
        {
            if (Directory.Exists(stagingDir))
            {
                Directory.Delete(stagingDir, true);
            }
        }
        catch
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && Directory.Exists(stagingDir))
            {
                foreach (var file in Directory.GetFiles(stagingDir, "*", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); } catch { }
                }
                try { Directory.Delete(stagingDir, true); } catch { }
            }
        }

        if (Directory.Exists(gameDir))
        {
            foreach (var file in Directory.GetFiles(gameDir))
            {
                var name = Path.GetFileName(file);
                if (name.EndsWith(".tmp") || name.StartsWith("sf-"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
    }

    [GeneratedRegex(@"(\d+(?:\.\d+)?)%")]
    private static partial Regex ProgressPercentageRegex();
}
