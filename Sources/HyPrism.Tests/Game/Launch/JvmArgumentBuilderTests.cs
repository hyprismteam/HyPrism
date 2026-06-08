using System.Diagnostics;
using HyPrism.Services.Game.Launch;

namespace HyPrism.Tests.Game.Launch;

public class JvmArgumentBuilderTests
{

    [Fact]
    public void Sanitize_SafeArgs_PassThrough()
    {
        var result = JvmArgumentBuilder.Sanitize("-Xmx4G -Xms512m -Dfile.encoding=UTF-8");
        Assert.Contains("-Xmx4G", result);
        Assert.Contains("-Xms512m", result);
    }


    [Theory]
    [InlineData("-javaagent:/path/to/agent.jar")]
    [InlineData("-agentlib:jdwp")]
    [InlineData("-agentpath:/usr/lib/agent.so")]
    [InlineData("-Xbootclasspath:/evil/path")]
    [InlineData("-jar myapp.jar")]
    [InlineData("-cp /evil/classpath")]
    [InlineData("-classpath /evil/classpath")]
    [InlineData("--class-path /evil/classpath")]
    [InlineData("--module-path /evil/path")]
    [InlineData("-Djava.home=/evil/home")]
    public void Sanitize_BlockedFlag_IsStripped(string dangerousArg)
    {
        var result = JvmArgumentBuilder.Sanitize(dangerousArg);
        Assert.True(
            string.IsNullOrWhiteSpace(result),
            $"Expected empty after sanitizing '{dangerousArg}', got '{result}'");
    }

    [Fact]
    public void Sanitize_MixedArgs_OnlyRemovesDangerous()
    {
        var result = JvmArgumentBuilder.Sanitize("-Xmx2G -javaagent:/hack.jar -Dsome.prop=val");
        Assert.Contains("-Xmx2G", result);
        Assert.DoesNotContain("-javaagent", result);
        Assert.Contains("-Dsome.prop=val", result);
    }

    [Fact]
    public void Sanitize_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", JvmArgumentBuilder.Sanitize(""));
    }


    [Fact]
    public void MergeToolOptions_NullExisting_ReturnsAdditional()
    {
        var result = JvmArgumentBuilder.MergeToolOptions(null, "-Xmx4G");
        Assert.Equal("-Xmx4G", result);
    }

    [Fact]
    public void MergeToolOptions_EmptyExisting_ReturnsAdditional()
    {
        var result = JvmArgumentBuilder.MergeToolOptions("", "-Xmx4G");
        Assert.Equal("-Xmx4G", result);
    }

    [Fact]
    public void MergeToolOptions_BothProvided_MergesWithSpace()
    {
        var result = JvmArgumentBuilder.MergeToolOptions("-Xms512m", "-Xmx4G");
        Assert.Equal("-Xms512m -Xmx4G", result);
    }


    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("with space", "with space")]
    [InlineData(@"back\slash", @"back\\slash")]
    [InlineData("with\"quote", "with\\\"quote")]
    [InlineData("dollar$sign", "dollar\\$sign")]
    [InlineData("back`tick", "back\\`tick")]
    public void EscapeForBash_EscapesSpecialChars(string input, string expected)
    {
        Assert.Equal(expected, JvmArgumentBuilder.EscapeForBash(input));
    }


    [Fact]
    public void ApplyToProcess_ValidArgs_SetsJavaToolOptions()
    {
        var psi = new ProcessStartInfo();
        var applied = JvmArgumentBuilder.ApplyToProcess(psi, "-Xmx4G");

        Assert.True(applied);
        Assert.True(psi.Environment.TryGetValue("JAVA_TOOL_OPTIONS", out var value));
        Assert.Contains("-Xmx4G", value);
    }

    [Fact]
    public void ApplyToProcess_NullArgs_ReturnsFalse()
    {
        var psi = new ProcessStartInfo();
        var applied = JvmArgumentBuilder.ApplyToProcess(psi, null);

        Assert.False(applied);
        Assert.False(psi.Environment.ContainsKey("JAVA_TOOL_OPTIONS"));
    }

    [Fact]
    public void ApplyToProcess_OnlyDangerousArgs_ReturnsFalse()
    {
        var psi = new ProcessStartInfo();
        var applied = JvmArgumentBuilder.ApplyToProcess(psi, "-javaagent:/bad.jar");

        Assert.False(applied);
    }

    [Fact]
    public void ApplyToProcess_ExistingJavaToolOptions_AppendsSanitized()
    {
        var psi = new ProcessStartInfo();
        psi.Environment["JAVA_TOOL_OPTIONS"] = "-Xms256m";

        JvmArgumentBuilder.ApplyToProcess(psi, "-Xmx4G");

        psi.Environment.TryGetValue("JAVA_TOOL_OPTIONS", out var val);
        Assert.Contains("-Xms256m", val);
        Assert.Contains("-Xmx4G", val);
    }


    [Fact]
    public void BuildEnvLine_NullArgs_ReturnsCommentedEmpty()
    {
        var result = JvmArgumentBuilder.BuildEnvLine(null);
        Assert.Contains("USER_JAVA_TOOL_OPTIONS=\"\"", result);
    }

    [Fact]
    public void BuildEnvLine_ValidArgs_ContainsAssignment()
    {
        var result = JvmArgumentBuilder.BuildEnvLine("-Xmx4G");
        Assert.Contains("USER_JAVA_TOOL_OPTIONS=\"-Xmx4G\"", result);
    }
}
