namespace HyPrism.Services.Core.Integration;

/// <summary>
/// Provides GitHub API integration for fetching contributor information and avatars.
/// Used primarily for displaying project contributors in the About section.
/// </summary>
public interface IGitHubService
{
    /// <summary>
    /// Retrieves the list of contributors to the HyPrism repository from GitHub.
    /// </summary>
    /// <returns>A list of <see cref="GitHubUser"/> objects representing contributors.</returns>
    Task<List<GitHubUser>> GetContributorsAsync();
    
    /// <summary>
    /// Fetches detailed information about a specific GitHub user.
    /// </summary>
    /// <param name="username">The GitHub username to look up.</param>
    /// <returns>The <see cref="GitHubUser"/> details, or <c>null</c> if user not found.</returns>
    Task<GitHubUser?> GetUserAsync(string username);
    
    /// <summary>
    /// Downloads a user's avatar image from the specified URL.
    /// </summary>
    /// <param name="url">The URL of the avatar image to load.</param>
    /// <param name="decodeWidth">The width hint for the CDN (avatar size in px). Defaults to 96.</param>
    /// <returns>The raw image bytes, or <c>null</c> if loading failed.</returns>
    Task<byte[]?> LoadAvatarAsync(string url, int decodeWidth = 96);
}
