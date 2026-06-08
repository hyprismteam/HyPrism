using System.Diagnostics;
using System.Text.RegularExpressions;

namespace HyPrism.Services.Game.Launch;

/// <summary>
/// Provides helpers for building, sanitizing, and applying JVM arguments
/// to game process start configurations.
/// </summary>
public static class JvmArgumentBuilder
{
    /// <summary>
    /// Sanitizes user-supplied JVM arguments by removing dangerous flags
    /// that could compromise launcher integrity (e.g., -javaagent, -classpath, -jar).
    /// </summary>
    /// <param name="args">The raw JVM argument string from user settings.</param>
    /// <returns>The sanitized argument string, or empty if all args were stripped.</returns>
    public static string Sanitize(string args)
    {
        var sanitized = args;

        var blockedPatterns = new[]
        {
            @"(?:^|\s)-javaagent:\S+",
            @"(?:^|\s)-agentlib:\S+",
            @"(?:^|\s)-agentpath:\S+",
            @"(?:^|\s)-Xbootclasspath(?::\S+)?",
            @"(?:^|\s)-jar(?:\s+\S+)?",
            @"(?:^|\s)-cp(?:\s+\S+)?",
            @"(?:^|\s)-classpath(?:\s+\S+)?",
            @"(?:^|\s)--class-path(?:\s+\S+)?",
            @"(?:^|\s)--module-path(?:\s+\S+)?",
            @"(?:^|\s)-Djava\.home=\S+",
        };

        foreach (var pattern in blockedPatterns)
        {
            sanitized = Regex.Replace(sanitized, pattern, " ", RegexOptions.IgnoreCase);
        }

        sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();
        return sanitized;
    }

    /// <summary>
    /// Merges an additional argument string into an existing JAVA_TOOL_OPTIONS value.
    /// </summary>
    /// <param name="existing">The current JAVA_TOOL_OPTIONS value, or null.</param>
    /// <param name="additional">The arguments to append.</param>
    /// <returns>The merged options string.</returns>
    public static string MergeToolOptions(string? existing, string additional)
    {
        if (string.IsNullOrWhiteSpace(existing))
            return additional;

        return $"{existing} {additional}";
    }

    /// <summary>
    /// Escapes a string for use inside a double-quoted bash string.
    /// </summary>
    /// <param name="value">The string to escape.</param>
    /// <returns>The bash-escaped string.</returns>
    public static string EscapeForBash(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("$", "\\$")
            .Replace("`", "\\`");
    }

    /// <summary>
    /// Applies user-provided Java arguments to a process via the JAVA_TOOL_OPTIONS
    /// environment variable, preserving any existing value (e.g., a DualAuth javaagent).
    /// </summary>
    /// <param name="startInfo">The process start info to modify.</param>
    /// <param name="javaArguments">The raw user Java argument string from settings.</param>
    /// <returns><c>true</c> if arguments were applied; <c>false</c> if none were set.</returns>
    public static bool ApplyToProcess(ProcessStartInfo startInfo, string? javaArguments)
    {
        var userJavaArgs = javaArguments?.Trim();
        if (string.IsNullOrWhiteSpace(userJavaArgs))
            return false;

        var sanitized = Sanitize(userJavaArgs);
        if (string.IsNullOrWhiteSpace(sanitized))
            return false;

        startInfo.Environment.TryGetValue("JAVA_TOOL_OPTIONS", out var current);
        startInfo.Environment["JAVA_TOOL_OPTIONS"] = MergeToolOptions(current, sanitized);
        return true;
    }

    /// <summary>
    /// Builds the USER_JAVA_TOOL_OPTIONS bash environment variable block used
    /// in Unix launch scripts. Returns a commented-out empty assignment if no
    /// valid arguments are present.
    /// </summary>
    /// <param name="javaArguments">The raw user Java argument string from settings.</param>
    /// <returns>A multi-line bash script fragment with the variable assignment.</returns>
    public static string BuildEnvLine(string? javaArguments)
    {
        var userJavaArgs = javaArguments?.Trim();
        if (string.IsNullOrWhiteSpace(userJavaArgs))
            return "# No custom user Java args\nUSER_JAVA_TOOL_OPTIONS=\"\"\n\n";

        userJavaArgs = Sanitize(userJavaArgs);
        if (string.IsNullOrWhiteSpace(userJavaArgs))
            return "# No custom user Java args\nUSER_JAVA_TOOL_OPTIONS=\"\"\n\n";

        var escaped = EscapeForBash(userJavaArgs);
        return $@"# Custom user Java arguments from Settings
USER_JAVA_TOOL_OPTIONS=""{escaped}""

";
    }
}
