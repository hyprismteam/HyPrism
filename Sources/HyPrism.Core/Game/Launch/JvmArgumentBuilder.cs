// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HyPrism.Core.Game.Launch;

/// <summary>
/// Provides helpers for building, sanitizing, and applying JVM arguments
/// to game process start configurations.
/// </summary>
public static partial class JvmArgumentBuilder
{
    /// <summary>
    /// Reads the maximum JVM heap size (<c>-Xmx</c>) and returns it in megabytes.
    /// </summary>
    public static int? ParseMaximumHeapMb(string? args) => ParseHeapMb(args, "Xmx");

    /// <summary>
    /// Reads the initial JVM heap size (<c>-Xms</c>) and returns it in megabytes.
    /// </summary>
    public static int? ParseInitialHeapMb(string? args) => ParseHeapMb(args, "Xms");

    /// <summary>
    /// Replaces or inserts the maximum JVM heap size (<c>-Xmx</c>).
    /// </summary>
    public static string SetMaximumHeapMb(string? args, int memoryMb)
        => SetHeapMb(args, "Xmx", memoryMb);

    /// <summary>
    /// Replaces or inserts the initial JVM heap size (<c>-Xms</c>).
    /// </summary>
    public static string SetInitialHeapMb(string? args, int memoryMb)
        => SetHeapMb(args, "Xms", memoryMb);

    /// <summary>
    /// Removes launcher-managed heap arguments (<c>-Xms</c> and <c>-Xmx</c>)
    /// from a user-editable JVM argument string.
    /// </summary>
    public static string RemoveHeapArguments(string? args)
    {
        var withoutHeap = HeapArgumentRegex().Replace(args ?? string.Empty, " ");
        return WhitespaceRegex().Replace(withoutHeap, " ").Trim();
    }

    /// <summary>
    /// Returns whether a JVM argument string contains launcher-managed heap arguments.
    /// </summary>
    public static bool ContainsHeapArguments(string? args)
        => !string.Equals(
            RemoveHeapArguments(args),
            WhitespaceRegex().Replace(args ?? string.Empty, " ").Trim(),
            StringComparison.Ordinal);

    /// <summary>
    /// Sanitizes user-supplied JVM arguments by removing dangerous flags
    /// that could compromise launcher integrity (e.g., -javaagent, -classpath, -jar).
    /// </summary>
    /// <param name="args">The raw JVM argument string from user settings.</param>
    /// <returns>The sanitized argument string, or empty if all args were stripped.</returns>
    public static string Sanitize(string args)
    {
        var sanitized = args;

        sanitized = BlockedArgumentRegex().Replace(sanitized, " ");
        sanitized = WhitespaceRegex().Replace(sanitized, " ").Trim();
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

    private static int? ParseHeapMb(string? args, string flag)
    {
        if (string.IsNullOrWhiteSpace(args))
            return null;

        var match = flag == "Xmx"
            ? MaximumHeapValueRegex().Match(args)
            : InitialHeapValueRegex().Match(args);
        if (!match.Success ||
            !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value) ||
            value <= 0)
        {
            return null;
        }

        var memoryMb = char.ToUpperInvariant(match.Groups[2].Value[0]) switch
        {
            'G' => value * 1024,
            'K' => value / 1024,
            _ => value
        };

        return Math.Max(1, (int)Math.Round(memoryMb, MidpointRounding.AwayFromZero));
    }

    private static string SetHeapMb(string? args, string flag, int memoryMb)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(memoryMb);

        var withoutHeap = (flag == "Xmx"
            ? MaximumHeapArgumentRegex()
            : InitialHeapArgumentRegex()).Replace(args ?? string.Empty, " ");
        withoutHeap = WhitespaceRegex().Replace(withoutHeap, " ").Trim();

        var heapArgument = $"-{flag}{memoryMb}M";
        return withoutHeap.Length == 0 ? heapArgument : $"{heapArgument} {withoutHeap}";
    }

    [GeneratedRegex(@"(?:^|\s)-Xm[sx]\S*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeapArgumentRegex();

    [GeneratedRegex(
        @"(?:^|\s)-Xmx(\d+(?:\.\d+)?)([KMG])(?=\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MaximumHeapValueRegex();

    [GeneratedRegex(
        @"(?:^|\s)-Xms(\d+(?:\.\d+)?)([KMG])(?=\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InitialHeapValueRegex();

    [GeneratedRegex(@"(?:^|\s)-Xmx\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MaximumHeapArgumentRegex();

    [GeneratedRegex(@"(?:^|\s)-Xms\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InitialHeapArgumentRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(
        @"(?:^|\s)(?:-javaagent:\S+|-agentlib:\S+|-agentpath:\S+|-Xbootclasspath(?::\S+)?|-jar(?:\s+\S+)?|-cp(?:\s+\S+)?|-classpath(?:\s+\S+)?|--class-path(?:\s+\S+)?|--module-path(?:\s+\S+)?|-Djava\.home=\S+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockedArgumentRegex();
}
