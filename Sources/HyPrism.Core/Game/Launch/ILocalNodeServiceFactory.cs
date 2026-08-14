// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Core.Game.Launch;

/// <summary>
/// Creates an isolated Local Node control plane for one autonomous game launch
/// </summary>
public interface ILocalNodeServiceFactory
{
    /// <summary>
    /// Creates a Local Node that is not shared with any other launch session
    /// <returns>An isolated Local Node service</returns>
    /// </summary>
    ILocalNodeService Create();
}
