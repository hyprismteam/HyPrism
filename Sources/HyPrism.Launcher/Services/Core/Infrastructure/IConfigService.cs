using HyPrism.Models;

namespace HyPrism.Services.Core.Infrastructure;

/// <summary>
/// Manages launcher configuration persistence including loading, saving, and modifying settings.
/// Handles configuration migrations between versions automatically.
/// </summary>
public interface IConfigService
{
    /// <summary>
    /// Gets the current launcher configuration.
    /// </summary>
    Config Configuration { get; }
    
    /// <summary>
    /// Persists the current configuration state to disk.
    /// </summary>
    void SaveConfig();
    
    /// <summary>
    /// Resets configuration to default values while preserving essential user data (UUID, Profiles).
    /// </summary>
    void ResetConfig();
    
    /// <summary>
    /// Sets a custom directory for storing game instances and launcher data.
    /// </summary>
    /// <param name="path">The new directory path for launcher data.</param>
    /// <returns>The new directory path if successful, or <c>null</c> if the path is invalid.</returns>
    Task<string?> SetInstanceDirectoryAsync(string path);
}
