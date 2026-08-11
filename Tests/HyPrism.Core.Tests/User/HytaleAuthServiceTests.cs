// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.User;

namespace HyPrism.Core.Tests.User;

public sealed class HytaleAuthServiceTests
{
    [Fact]
    public async Task LoginAsync_PresentsGeneratedAuthorizationUriThroughHostCallback()
    {
        var appDir = Path.Combine(Path.GetTempPath(), $"hyprism-auth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDir);

        try
        {
            var config = new Mock<IConfigService>();
            config.SetupGet(service => service.Configuration).Returns(new Config());
            using var httpClient = new HttpClient();
            var auth = new HytaleAuthService(httpClient, appDir, config.Object);
            Uri? presentedUri = null;

            var session = await auth.LoginAsync((uri, _) =>
            {
                presentedUri = uri;
                return Task.FromResult(false);
            });

            Assert.Null(session);
            Assert.NotNull(presentedUri);
            Assert.Equal(Uri.UriSchemeHttps, presentedUri.Scheme);
            Assert.Equal("oauth.accounts.hytale.com", presentedUri.Host);
            Assert.Contains("code_challenge=", presentedUri.Query, StringComparison.Ordinal);
            Assert.Contains("state=", presentedUri.Query, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoginAsync_RejectsMissingHostCallback()
    {
        var appDir = Path.Combine(Path.GetTempPath(), $"hyprism-auth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDir);

        try
        {
            var config = new Mock<IConfigService>();
            config.SetupGet(service => service.Configuration).Returns(new Config());
            using var httpClient = new HttpClient();
            var auth = new HytaleAuthService(httpClient, appDir, config.Object);

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => auth.LoginAsync(null!));
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }
}
