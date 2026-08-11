// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Desktop.Features.About;

/// <summary>
/// Provides the public GitHub data used by the launcher
/// </summary>
public interface IGitHubClient
{
    /// <summary>
    /// Retrieves contributors to the HyPrism repository
    /// </summary>
    /// <returns>The contributors reported by GitHub, with recently active accounts first</returns>
    Task<List<GitHubUser>> GetContributorsAsync();

    /// <summary>
    /// Retrieves the latest commit from the main branch
    /// </summary>
    /// <returns>The latest main branch commit, or <c>null</c> when it cannot be loaded</returns>
    Task<GitHubCommit?> GetLatestMainCommitAsync();

    /// <summary>
    /// Fetches public information about a GitHub user
    /// </summary>
    /// <param name="username">The GitHub login to look up</param>
    /// <returns>The user details, or <c>null</c> when the user cannot be loaded</returns>
    Task<GitHubUser?> GetUserAsync(string username);

    /// <summary>
    /// Downloads an avatar from GitHub
    /// </summary>
    /// <param name="url">The absolute avatar URL</param>
    /// <param name="decodeWidth">The preferred image width requested from the GitHub CDN</param>
    /// <returns>The avatar bytes, or <c>null</c> when the image cannot be loaded</returns>
    Task<byte[]?> LoadAvatarAsync(string url, int decodeWidth = 96);
}
