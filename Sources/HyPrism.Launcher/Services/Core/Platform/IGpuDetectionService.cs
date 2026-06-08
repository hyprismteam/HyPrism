using HyPrism.Models;

namespace HyPrism.Services.Core.Platform;

/// <summary>
/// Detects available GPU adapters on the system.
/// </summary>
public interface IGpuDetectionService
{
    /// <summary>
    /// Gets the list of detected GPU adapters. Results are cached.
    /// </summary>
    List<GpuAdapterInfo> GetAdapters();

    /// <summary>
    /// Returns true if only a single GPU was detected (no switchable graphics).
    /// </summary>
    bool HasSingleGpu();
}
