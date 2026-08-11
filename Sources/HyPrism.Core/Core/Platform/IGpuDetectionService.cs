// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Models;

namespace HyPrism.Services.Core.Platform;

/// <summary>
/// Detects available GPU adapters on the system
/// </summary>
public interface IGpuDetectionService
{
    /// <summary>
    /// Gets the list of detected GPU adapters. Results are cached
    /// </summary>
    /// <returns>The detected GPU adapters in platform enumeration order</returns>
    List<GpuAdapterInfo> GetAdapters();

    /// <summary>
    /// Returns true if only a single GPU was detected (no switchable graphics)
    /// </summary>
    /// <returns><see langword="true"/> when the system exposes no GPU choice</returns>
    bool HasSingleGpu();
}
