// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;

namespace HyPrism.Desktop.Platform;

/// <summary>
/// Provides platform memory information used by the Desktop settings UI
/// </summary>
public static class SystemMemoryProvider
{
    /// <summary>
    /// Returns total physical memory in megabytes
    /// </summary>
    /// <returns>Total physical memory, or a conservative fallback when detection is unavailable</returns>
    public static int GetSystemMemoryMb()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var memoryStatus = new MemoryStatusEx();
                if (GlobalMemoryStatusEx(memoryStatus) && memoryStatus.TotalPhysicalMemory > 0)
                    return (int)(memoryStatus.TotalPhysicalMemory / (1024 * 1024));
            }

            if (OperatingSystem.IsLinux())
            {
                const string memInfoPath = "/proc/meminfo";
                var totalLine = File.Exists(memInfoPath)
                    ? File.ReadLines(memInfoPath)
                        .FirstOrDefault(line => line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase))
                    : null;

                if (!string.IsNullOrWhiteSpace(totalLine))
                {
                    var parts = totalLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && long.TryParse(parts[1], out var kilobytes) && kilobytes > 0)
                        return (int)(kilobytes / 1024);
                }
            }

            if (OperatingSystem.IsMacOS())
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/sbin/sysctl",
                    Arguments = "-n hw.memsize",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process is not null)
                {
                    var output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit(2000);
                    if (process.ExitCode == 0 && long.TryParse(output, out var bytes) && bytes > 0)
                        return (int)(bytes / (1024 * 1024));
                }
            }
        }
        catch
        {
            // The fallback below keeps settings usable on restricted systems
        }

        var fallback = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return fallback > 0
            ? (int)Math.Max(1024, fallback / (1024 * 1024))
            : 8192;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysicalMemory;
        public ulong AvailablePhysicalMemory;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtualMemory;
        public ulong AvailableVirtualMemory;
        public ulong AvailableExtendedVirtualMemory;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx memoryStatus);
}
