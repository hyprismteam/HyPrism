using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Tests.Core.Infrastructure;

public class UtilityServiceTests
{

    [Theory]
    [InlineData("release", "release")]
    [InlineData("beta", "beta")]
    [InlineData("latest", "release")]
    [InlineData("prerelease", "pre-release")]
    [InlineData("pre-release", "pre-release")]
    [InlineData("", "release")]
    [InlineData("   ", "release")]
    public void NormalizeVersionType_ReturnsExpected(string input, string expected)
    {
        var result = UtilityService.NormalizeVersionType(input);
        Assert.Equal(expected, result);
    }


    [Fact]
    public void SanitizeFileName_ValidName_ReturnsSameName()
    {
        Assert.Equal("MyProfile", UtilityService.SanitizeFileName("MyProfile"));
    }

    [Fact]
    public void SanitizeFileName_InvalidChars_ReplacedWithUnderscore()
    {
        // Only '/' is universally invalid across platforms; ':' is valid on Linux.
        var result = UtilityService.SanitizeFileName("my/profile/name");
        Assert.DoesNotContain('/', result);
    }

    [Fact]
    public void SanitizeFileName_EmptyResult_ReturnsDefault()
    {
        // All chars are invalid → sanitized is empty → "default"
        var result = UtilityService.SanitizeFileName("\0\0");
        Assert.Equal("default", result);
    }


    [Fact]
    public void GenerateRandomUsername_LengthWithinLimit()
    {
        for (int i = 0; i < 20; i++)
        {
            var username = UtilityService.GenerateRandomUsername();
            Assert.True(username.Length <= 16, $"Username '{username}' exceeds 16 chars");
            Assert.False(string.IsNullOrWhiteSpace(username));
        }
    }

    [Fact]
    public void GenerateRandomUsername_IsNotConstant()
    {
        var names = Enumerable.Range(0, 50).Select(_ => UtilityService.GenerateRandomUsername()).ToHashSet();
        // With random adjective + noun + 4-digit number, collisions are possible but extremely rare
        Assert.True(names.Count > 1, "GenerateRandomUsername should produce varied names");
    }


    [Fact]
    public void GetOS_ReturnsKnownPlatform()
    {
        var os = UtilityService.GetOS();
        Assert.Contains(os, new[] { "windows", "darwin", "linux", "unknown" });
    }

    [Fact]
    public void GetArch_ReturnsKnownArch()
    {
        var arch = UtilityService.GetArch();
        Assert.Contains(arch, new[] { "amd64", "arm64" });
    }


    [Fact]
    public void GetDefaultAppDir_IsAbsolutePath()
    {
        var dir = UtilityService.GetDefaultAppDir();
        Assert.True(System.IO.Path.IsPathRooted(dir), "App dir should be an absolute path");
        Assert.EndsWith("HyPrism", dir, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void GetEffectiveAppDir_EnvVarOverride_UsesCustomDir()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            Environment.SetEnvironmentVariable("HYPRISM_DATA", tempDir);
            var result = UtilityService.GetEffectiveAppDir();
            Assert.Equal(tempDir, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HYPRISM_DATA", null);
            System.IO.Directory.Delete(tempDir);
        }
    }

    [Fact]
    public void GetEffectiveAppDir_NoEnvVar_FallsBackToDefault()
    {
        Environment.SetEnvironmentVariable("HYPRISM_DATA", null);
        var result = UtilityService.GetEffectiveAppDir();
        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.True(System.IO.Path.IsPathRooted(result));
    }


    [Fact]
    public void CopyDirectory_CopiesFilesAndSubdirs()
    {
        var src = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
        var dst = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            System.IO.Directory.CreateDirectory(src);
            var subDir = System.IO.Path.Combine(src, "sub");
            System.IO.Directory.CreateDirectory(subDir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(src, "file.txt"), "hello");
            System.IO.File.WriteAllText(System.IO.Path.Combine(subDir, "nested.txt"), "world");

            UtilityService.CopyDirectory(src, dst);

            Assert.True(System.IO.File.Exists(System.IO.Path.Combine(dst, "file.txt")));
            Assert.True(System.IO.File.Exists(System.IO.Path.Combine(dst, "sub", "nested.txt")));
        }
        finally
        {
            if (System.IO.Directory.Exists(src)) System.IO.Directory.Delete(src, true);
            if (System.IO.Directory.Exists(dst)) System.IO.Directory.Delete(dst, true);
        }
    }

    [Fact]
    public void CopyDirectory_NonExistentSource_DoesNotThrow()
    {
        var src = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nonexistent_" + Guid.NewGuid());
        var dst = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dst_" + Guid.NewGuid());
        // Should not throw
        var ex = Record.Exception(() => UtilityService.CopyDirectory(src, dst));
        Assert.Null(ex);
    }


    [Fact]
    public void MacSignature_RoundTrip_IsConsistent()
    {
        var tempFile = System.IO.Path.GetTempFileName();
        var stampFile = tempFile + ".stamp";
        try
        {
            System.IO.File.WriteAllText(tempFile, "dummy");
            Assert.False(UtilityService.IsMacAppSignatureCurrent(tempFile, stampFile));

            UtilityService.MarkMacAppSigned(tempFile, stampFile);
            Assert.True(UtilityService.IsMacAppSignatureCurrent(tempFile, stampFile));

            // Touching the file changes the timestamp → stamp becomes stale
            System.IO.File.AppendAllText(tempFile, "changed");
            Assert.False(UtilityService.IsMacAppSignatureCurrent(tempFile, stampFile));
        }
        finally
        {
            if (System.IO.File.Exists(tempFile)) System.IO.File.Delete(tempFile);
            if (System.IO.File.Exists(stampFile)) System.IO.File.Delete(stampFile);
        }
    }
}
