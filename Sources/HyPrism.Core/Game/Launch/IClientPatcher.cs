// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Services.Game.Launch;

/// <summary>
/// Patches the HytaleClient binary to redirect domain references from hytale.com to a
/// custom authentication server, enabling use of community auth infrastructure
/// </summary>
public interface IClientPatcher
{
    /// <summary>
    /// Checks whether the client binary at <paramref name="clientPath"/> has already been patched
    /// </summary>
    /// <param name="clientPath">Absolute path to the client binary</param>
    /// <returns><see langword="true"/> when the expected patch is already present</returns>
    bool IsPatchedAlready(string clientPath);

    /// <summary>
    /// Patches the client binary at <paramref name="clientPath"/> to replace hytale.com references
    /// with the configured target domain. Creates a backup before patching
    /// </summary>
    /// <param name="clientPath">Absolute path to the client binary</param>
    /// <param name="progressCallback">Optional callback receiving stage text and percentage</param>
    /// <returns>The patch result with status and diagnostic details</returns>
    PatchResult PatchClient(string clientPath, Action<string, int?>? progressCallback = null);

    /// <summary>
    /// Patches the server JAR inside <paramref name="gameDir"/> if present
    /// </summary>
    /// <param name="gameDir">Absolute path to the game directory</param>
    /// <param name="progressCallback">Optional callback receiving stage text and percentage</param>
    /// <returns>The patch result with status and diagnostic details</returns>
    PatchResult PatchServerJar(string gameDir, Action<string, int?>? progressCallback = null);

    /// <summary>
    /// Ensures the client binary inside <paramref name="gameDir"/> is patched.
    /// Locates the binary automatically and applies <see cref="PatchClient"/> if needed
    /// </summary>
    /// <param name="gameDir">Absolute path to the game directory</param>
    /// <param name="progressCallback">Optional callback receiving stage text and percentage</param>
    /// <returns>The patch result with status and diagnostic details</returns>
    PatchResult EnsureClientPatched(string gameDir, Action<string, int?>? progressCallback = null);

    /// <summary>
    /// Ensures both client binary and server JAR inside <paramref name="gameDir"/> are patched
    /// </summary>
    /// <param name="gameDir">Absolute path to the game directory</param>
    /// <param name="progressCallback">Optional callback receiving stage text and percentage</param>
    /// <returns>The combined patch result for the client and server artifacts</returns>
    PatchResult EnsureAllPatched(string gameDir, Action<string, int?>? progressCallback = null);
}
