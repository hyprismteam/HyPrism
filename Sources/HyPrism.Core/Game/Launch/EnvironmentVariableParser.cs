// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace HyPrism.Core.Game.Launch;

/// <summary>
/// Parses user-defined environment variable assignments from settings and
/// applies them to game process start configurations on every platform.
/// </summary>
public static partial class EnvironmentVariableParser
{
    /// <summary>
    /// A single parsed KEY=VALUE assignment with the surrounding quotes removed.
    /// </summary>
    /// <param name="Key">The validated variable name.</param>
    /// <param name="Value">The raw variable value.</param>
    public sealed record EnvironmentVariable(string Key, string Value);

    /// <summary>
    /// Parses line-separated KEY=VALUE assignments. Blank lines and lines
    /// starting with <c>#</c> are skipped. A line may carry several
    /// space-separated assignments or one assignment whose value contains
    /// spaces. Keys must match <c>[A-Za-z_][A-Za-z0-9_]*</c>. Duplicate keys
    /// are returned in order so the last assignment wins when applied.
    /// </summary>
    /// <param name="input">The raw user variable string from settings.</param>
    /// <returns>The parsed assignments in source order.</returns>
    public static IReadOnlyList<EnvironmentVariable> Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return [];

        var result = new List<EnvironmentVariable>();

        foreach (var line in input.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            if (MultipleEnvironmentVariablesRegex().IsMatch(trimmed))
            {
                foreach (Match match in EnvironmentVariableAssignmentRegex().Matches(trimmed))
                    TryAdd(result, match.Groups["key"].Value, match.Groups["value"].Value);
            }
            else
            {
                var separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex <= 0)
                    continue;

                TryAdd(result, trimmed[..separatorIndex].Trim(), trimmed[(separatorIndex + 1)..].Trim());
            }
        }

        return result;
    }

    /// <summary>
    /// Applies parsed user environment variables to a process start info,
    /// overriding values that were set earlier by other launch features.
    /// </summary>
    /// <param name="startInfo">The process start info to modify.</param>
    /// <param name="variables">The raw user variable string from settings.</param>
    /// <returns>The number of applied assignments.</returns>
    public static int ApplyToProcess(ProcessStartInfo startInfo, string? variables)
    {
        var parsed = Parse(variables);
        foreach (var variable in parsed)
            startInfo.Environment[variable.Key] = variable.Value;

        return parsed.Count;
    }

    private static void TryAdd(ICollection<EnvironmentVariable> result, string key, string value)
    {
        if (!EnvironmentVariableNameRegex().IsMatch(key))
            return;

        if (value.Length >= 2 &&
            ((value.StartsWith('"') && value.EndsWith('"')) ||
             (value.StartsWith('\'') && value.EndsWith('\''))))
        {
            value = value[1..^1];
        }

        if (value.Contains('\0'))
            return;

        result.Add(new EnvironmentVariable(key, value));
    }

    [GeneratedRegex(@"(?<key>[A-Za-z_][A-Za-z0-9_]*)=(?<value>""[^""]*""|'[^']*'|[^""'\s]+)")]
    private static partial Regex EnvironmentVariableAssignmentRegex();

    [GeneratedRegex(@"\s+[A-Za-z_][A-Za-z0-9_]*=")]
    private static partial Regex MultipleEnvironmentVariablesRegex();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex EnvironmentVariableNameRegex();
}
