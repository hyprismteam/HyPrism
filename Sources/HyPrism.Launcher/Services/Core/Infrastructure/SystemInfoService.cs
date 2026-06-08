using System.Runtime.InteropServices;

namespace HyPrism.Services.Core.Infrastructure;

/// <summary>
/// Provides cross-platform system information queries.
/// </summary>
public static class SystemInfoService
{
    /// <summary>
    /// Returns total physical memory in megabytes.
    /// Falls back to GC memory info or 8192 MB if detection fails.
    /// </summary>
    public static int GetSystemMemoryMb()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var memoryStatus = new MemoryStatusEx();
                if (GlobalMemoryStatusEx(memoryStatus) && memoryStatus.ullTotalPhys > 0)
                {
                    return (int)(memoryStatus.ullTotalPhys / (1024 * 1024));
                }
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                const string memInfoPath = "/proc/meminfo";
                if (File.Exists(memInfoPath))
                {
                    var memTotalLine = File.ReadLines(memInfoPath).FirstOrDefault(line => line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(memTotalLine))
                    {
                        var parts = memTotalLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && long.TryParse(parts[1], out var kb) && kb > 0)
                        {
                            return (int)(kb / 1024);
                        }
                    }
                }
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/sbin/sysctl",
                    Arguments = "-n hw.memsize",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit(2000);
                    if (process.ExitCode == 0 && long.TryParse(output, out var bytes) && bytes > 0)
                    {
                        return (int)(bytes / (1024 * 1024));
                    }
                }
            }
        }
        catch
        {
        }

        var fallback = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        if (fallback > 0)
        {
            return (int)Math.Max(1024, fallback / (1024 * 1024));
        }

        return 8192;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);
}
