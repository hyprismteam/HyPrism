// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Core.Infrastructure;

namespace HyPrism.Core.Tests.Core.Infrastructure;

public sealed class LauncherUserAgentTests
{
    [Theory]
    [InlineData("4.0.0", "HyPrism/4.0.0")]
    [InlineData("4.1.0-beta.2", "HyPrism/4.1.0-beta.2")]
    [InlineData("4.0.0+abcdef", "HyPrism/4.0.0")]
    public void Create_UsesProductVersionWithoutBuildMetadata(string version, string expected)
    {
        Assert.Equal(expected, LauncherUserAgent.Create(version));
    }

    [Fact]
    public void GetVersion_RemovesGeneratedSourceRevisionMetadata()
    {
        var version = LauncherUserAgent.GetVersion(typeof(LauncherUserAgentTests).Assembly);

        Assert.DoesNotContain('+', version);
        Assert.False(string.IsNullOrWhiteSpace(version));
    }
}
