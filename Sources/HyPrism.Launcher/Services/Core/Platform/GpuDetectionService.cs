using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.Core.Platform;

/// <summary>
/// Detects available GPU adapters on the system.
/// Works cross-platform: Windows (wmic/powershell), Linux (lspci/glxinfo), macOS (system_profiler).
/// Results are cached after first detection.
/// </summary>
public class GpuDetectionService : IGpuDetectionService
{
    private List<GpuAdapterInfo>? _cachedAdapters;

    /// <inheritdoc/>
    public List<GpuAdapterInfo> GetAdapters()
    {
        if (_cachedAdapters != null) return _cachedAdapters;
        
        try
        {
            _cachedAdapters = DetectGpus();
        }
        catch (Exception ex)
        {
            Logger.Warning("GPU", $"GPU detection failed: {ex.Message}");
            _cachedAdapters = new List<GpuAdapterInfo>();
        }
        
        foreach (var gpu in _cachedAdapters)
        {
            Logger.Info("GPU", $"Detected: {gpu.Name} (type={gpu.Type})");
        }
        
        return _cachedAdapters;
    }

    /// <inheritdoc/>
    public bool HasSingleGpu()
    {
        var adapters = GetAdapters();
        return adapters.Count <= 1;
    }

    private static List<GpuAdapterInfo> DetectGpus()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return DetectWindows();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return DetectLinux();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return DetectMacOS();
        
        return new List<GpuAdapterInfo>();
    }

    private static List<GpuAdapterInfo> DetectWindows()
    {
        var adapters = new List<GpuAdapterInfo>();
        
        try
        {
            // Use PowerShell to query GPU info (more reliable than wmic which is deprecated)
            var output = RunProcess("powershell", 
                "-NoProfile -Command \"Get-CimInstance Win32_VideoController | Select-Object Name,AdapterCompatibility,VideoProcessor | ForEach-Object { $_.Name + '|||' + $_.AdapterCompatibility + '|||' + $_.VideoProcessor }\"");
            
            if (!string.IsNullOrEmpty(output))
            {
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Trim().Split("|||");
                    var name = parts[0].Trim();
                    if (string.IsNullOrEmpty(name)) continue;
                    
                    adapters.Add(new GpuAdapterInfo
                    {
                        Name = name,
                        Vendor = parts.Length > 1 ? parts[1].Trim() : "",
                        Type = ClassifyGpu(name)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("GPU", $"Windows GPU detection via PowerShell failed: {ex.Message}");
            // Fallback to wmic
            try
            {
                var output = RunProcess("wmic", "path win32_videocontroller get name /format:list");
                if (!string.IsNullOrEmpty(output))
                {
                    foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (line.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
                        {
                            var name = line["Name=".Length..].Trim();
                            if (!string.IsNullOrEmpty(name))
                            {
                                adapters.Add(new GpuAdapterInfo
                                {
                                    Name = name,
                                    Vendor = "",
                                    Type = ClassifyGpu(name)
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex2)
            {
                Logger.Warning("GPU", $"Windows GPU detection via wmic also failed: {ex2.Message}");
            }
        }
        
        return adapters;
    }

    private static List<GpuAdapterInfo> DetectLinux()
    {
        var adapters = new List<GpuAdapterInfo>();
        
        try
        {
            // lspci is the most reliable way to detect GPUs on Linux
            var output = RunProcess("lspci", "");
            if (!string.IsNullOrEmpty(output))
            {
                // Match lines with VGA compatible controller or 3D controller
                // Format: "00:02.0 VGA compatible controller: Intel Corporation..."
                var gpuLines = Regex.Matches(output, @"^([0-9a-fA-F:\.]+)\s+(?:VGA compatible controller|3D controller|Display controller):\s*(.+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                foreach (Match match in gpuLines)
                {
                    var pciId = match.Groups[1].Value.Trim();
                    var name = match.Groups[2].Value.Trim();
                    // Clean up common prefixes like "NVIDIA Corporation" etc.
                    name = Regex.Replace(name, @"\s*\(rev [0-9a-f]+\)$", "", RegexOptions.IgnoreCase).Trim();
                    
                    if (!string.IsNullOrEmpty(name))
                    {
                        adapters.Add(new GpuAdapterInfo
                        {
                            Name = name,
                            Vendor = ExtractVendor(name),
                            Type = ClassifyGpu(name),
                            PciId = pciId
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("GPU", $"Linux GPU detection via lspci failed: {ex.Message}");
        }
        
        return adapters;
    }

    private static List<GpuAdapterInfo> DetectMacOS()
    {
        var adapters = new List<GpuAdapterInfo>();
        
        try
        {
            var output = RunProcess("system_profiler", "SPDisplaysDataType -detailLevel mini");
            if (!string.IsNullOrEmpty(output))
            {
                // Parse "Chipset Model: xxx" lines
                var chipsetMatches = Regex.Matches(output, @"Chipset Model:\s*(.+)", RegexOptions.IgnoreCase);
                foreach (Match match in chipsetMatches)
                {
                    var name = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(name))
                    {
                        adapters.Add(new GpuAdapterInfo
                        {
                            Name = name,
                            Vendor = ExtractVendor(name),
                            Type = ClassifyGpu(name)
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("GPU", $"macOS GPU detection failed: {ex.Message}");
        }
        
        return adapters;
    }

    /// <summary>
    /// Classifies a GPU as "dedicated" or "integrated" based on its name.
    /// </summary>
    private static string ClassifyGpu(string name)
    {
        var lower = name.ToLowerInvariant();
        
        // Known integrated GPU patterns
        if (lower.Contains("intel") && (lower.Contains("hd graphics") || lower.Contains("uhd graphics") || 
            lower.Contains("iris") || lower.Contains("integrated")))
            return "integrated";
        
        // AMD APU integrated graphics
        if ((lower.Contains("amd") || lower.Contains("ati")) && 
            lower.Contains("vega") && !lower.Contains("rx vega") || 
            lower.Contains("radeon graphics") ||
            lower.Contains("radeon(tm) graphics"))
            return "integrated";
        
        // Apple integrated GPUs
        if (lower.Contains("apple m") || lower.Contains("apple gpu"))
            return "integrated";
        
        // Microsoft Basic Render Driver (virtual, not real hardware)
        if (lower.Contains("microsoft basic") || lower.Contains("basic render"))
            return "integrated";
        
        // Everything else (NVIDIA GeForce, AMD Radeon RX, etc.) is likely dedicated
        if (lower.Contains("geforce") || lower.Contains("rtx") || lower.Contains("gtx") || 
            lower.Contains("quadro") || lower.Contains("tesla"))
            return "dedicated";
        
        if (lower.Contains("radeon") && (lower.Contains("rx") || lower.Contains("pro") || lower.Contains("r9") || lower.Contains("r7")))
            return "dedicated";
        
        // Default: if it's NVIDIA it's almost certainly dedicated
        if (lower.Contains("nvidia"))
            return "dedicated";
        
        // Default to dedicated for unknown GPUs
        return "dedicated";
    }

    private static string ExtractVendor(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("nvidia")) return "NVIDIA";
        if (lower.Contains("amd") || lower.Contains("ati") || lower.Contains("radeon")) return "AMD";
        if (lower.Contains("intel")) return "Intel";
        if (lower.Contains("apple")) return "Apple";
        return "";
    }

    private static string RunProcess(string fileName, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000); // 5 second timeout
            return output;
        }
        catch
        {
            return "";
        }
    }
}

/// <summary>
/// Represents a detected GPU adapter.
/// </summary>
public class GpuAdapterInfo
{
    /// <summary>Full name of the GPU (e.g., "NVIDIA GeForce RTX 4070")</summary>
    public string Name { get; set; } = "";
    
    /// <summary>Vendor name (e.g., "NVIDIA", "AMD", "Intel")</summary>
    public string Vendor { get; set; } = "";
    
    /// <summary>PCI ID (Linux only, e.g. "0000:03:00.0")</summary>
    public string PciId { get; set; } = "";
    
    /// <summary>GPU type: "dedicated" or "integrated"</summary>
    public string Type { get; set; } = "dedicated";
}
