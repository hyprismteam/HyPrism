using System.Diagnostics;
using System.Runtime.InteropServices;
using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.Core.Platform;

/// <summary>
/// Service responsible for checking Rosetta 2 availability on macOS Apple Silicon.
/// </summary>
public class RosettaService : IRosettaService
{
    /// <inheritdoc/>
    public RosettaStatus? CheckRosettaStatus()
    {
        // Only relevant on macOS
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return null;
        }

        // Only relevant on Apple Silicon (ARM64)
        if (RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
        {
            return null;
        }

        try
        {
            // Check if Rosetta is installed by checking for the runtime at /Library/Apple/usr/share/rosetta
            var rosettaPath = "/Library/Apple/usr/share/rosetta";
            if (Directory.Exists(rosettaPath))
            {
                Logger.Info("Rosetta", "Rosetta 2 is installed");
                return null; // Rosetta is installed, no warning needed
            }

            // Also try running arch -x86_64 to verify
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/arch",
                    Arguments = "-x86_64 /usr/bin/true",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                process?.WaitForExit(5000);
                if (process?.ExitCode == 0)
                {
                    Logger.Info("Rosetta", "Rosetta 2 is installed (verified via arch command)");
                    return null;
                }
            }
            catch
            {
                // Ignore, proceed with warning
            }

            Logger.Warning("Rosetta", "Rosetta 2 is NOT installed - Hytale requires it to run on Apple Silicon");
            return new RosettaStatus
            {
                NeedsInstall = true,
                Message = "Rosetta 2 is required to run Hytale on Apple Silicon Macs.",
                Command = "softwareupdate --install-rosetta --agree-to-license",
                TutorialUrl = "https://www.youtube.com/watch?v=1W2vuSfnpXw"
            };
        }
        catch (Exception ex)
        {
            Logger.Warning("Rosetta", $"Failed to check Rosetta status: {ex.Message}");
            return null;
        }
    }
}
