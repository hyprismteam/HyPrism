using HyPrism.Models;

namespace HyPrism.Services.Core.Platform;

/// <summary>
/// Checks Rosetta 2 availability on macOS Apple Silicon.
/// </summary>
public interface IRosettaService
{
    /// <summary>
    /// Check if Rosetta 2 is installed on macOS Apple Silicon.
    /// Returns null if not on macOS or if Rosetta is installed.
    /// Returns a warning object if Rosetta is needed but not installed.
    /// </summary>
    RosettaStatus? CheckRosettaStatus();
}
