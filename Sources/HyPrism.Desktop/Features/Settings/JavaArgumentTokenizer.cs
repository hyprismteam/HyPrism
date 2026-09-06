// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text;

namespace HyPrism.Desktop.Features.Settings;

internal static class JavaArgumentTokenizer
{
    public static IReadOnlyList<string> Split(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return [];

        var result = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';
        var escaped = false;

        foreach (var character in arguments)
        {
            if (char.IsWhiteSpace(character) && quote == '\0')
            {
                AddCurrent(result, current);
                escaped = false;
                continue;
            }

            if (character is '\'' or '"' && !escaped)
            {
                quote = quote == character
                    ? '\0'
                    : quote == '\0' ? character : quote;
            }

            current.Append(character);
            escaped = character == '\\' && !escaped;
            if (character != '\\')
                escaped = false;
        }

        AddCurrent(result, current);
        return result;
    }

    public static string Join(IEnumerable<JavaArgumentItemViewModel> arguments)
        => string.Join(' ', arguments.Select(argument => argument.Value));

    private static void AddCurrent(ICollection<string> result, StringBuilder current)
    {
        if (current.Length == 0)
            return;

        result.Add(current.ToString());
        current.Clear();
    }
}
