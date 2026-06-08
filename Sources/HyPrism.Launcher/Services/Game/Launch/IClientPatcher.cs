namespace HyPrism.Services.Game.Launch;

/// <summary>
/// Patches the HytaleClient binary to redirect domain references from hytale.com to a
/// custom authentication server, enabling use of community auth infrastructure.
/// </summary>
public interface IClientPatcher
{
    /// <summary>
    /// Checks whether the client binary at <paramref name="clientPath"/> has already been patched.
    /// </summary>
    bool IsPatchedAlready(string clientPath);

    /// <summary>
    /// Patches the client binary at <paramref name="clientPath"/> to replace hytale.com references
    /// with the configured target domain. Creates a backup before patching.
    /// </summary>
    PatchResult PatchClient(string clientPath, Action<string, int?>? progressCallback = null);

    /// <summary>
    /// Patches the server JAR inside <paramref name="gameDir"/> if present.
    /// </summary>
    PatchResult PatchServerJar(string gameDir, Action<string, int?>? progressCallback = null);

    /// <summary>
    /// Ensures the client binary inside <paramref name="gameDir"/> is patched.
    /// Locates the binary automatically and applies <see cref="PatchClient"/> if needed.
    /// </summary>
    PatchResult EnsureClientPatched(string gameDir, Action<string, int?>? progressCallback = null);

    /// <summary>
    /// Ensures both client binary and server JAR inside <paramref name="gameDir"/> are patched.
    /// </summary>
    PatchResult EnsureAllPatched(string gameDir, Action<string, int?>? progressCallback = null);
}
