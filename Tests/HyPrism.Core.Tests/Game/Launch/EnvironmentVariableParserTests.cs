// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using HyPrism.Core.Game.Launch;

namespace HyPrism.Core.Tests.Game.Launch;

public class EnvironmentVariableParserTests
{
    [Fact]
    public void Parse_SingleAssignment_ParsesKeyAndValue()
    {
        var variables = EnvironmentVariableParser.Parse("MY_VAR=value");

        var variable = Assert.Single(variables);
        Assert.Equal("MY_VAR", variable.Key);
        Assert.Equal("value", variable.Value);
    }

    [Theory]
    [InlineData("KEY=\"spaced value\"", "spaced value")]
    [InlineData("KEY='spaced value'", "spaced value")]
    public void Parse_QuotedValues_StripsSurroundingQuotes(string input, string expectedValue)
    {
        var variable = Assert.Single(EnvironmentVariableParser.Parse(input));
        Assert.Equal("KEY", variable.Key);
        Assert.Equal(expectedValue, variable.Value);
    }

    [Fact]
    public void Parse_ValueMayContainSpacesAndEqualsSigns()
    {
        var variable = Assert.Single(EnvironmentVariableParser.Parse("KEY=hello world = 2"));

        Assert.Equal("KEY", variable.Key);
        Assert.Equal("hello world = 2", variable.Value);
    }

    [Fact]
    public void Parse_MultiVariableLine_ParsesEveryAssignment()
    {
        var variables = EnvironmentVariableParser.Parse("A=1 B=\"two words\" C=3");

        Assert.Equal(
            [
                new EnvironmentVariableParser.EnvironmentVariable("A", "1"),
                new EnvironmentVariableParser.EnvironmentVariable("B", "two words"),
                new EnvironmentVariableParser.EnvironmentVariable("C", "3")
            ],
            variables);
    }

    [Fact]
    public void Parse_SkipsBlankAndCommentLines()
    {
        var variables = EnvironmentVariableParser.Parse("\r\n# comment\n   \nKEY=value\n# ANOTHER=hidden");

        var variable = Assert.Single(variables);
        Assert.Equal("KEY", variable.Key);
    }

    [Theory]
    [InlineData("1INVALID=value")]
    [InlineData("HAS-DASH=value")]
    [InlineData("NO_VALUE_AT_ALL")]
    [InlineData("=value")]
    public void Parse_SkipsAssignmentsWithInvalidKeys(string input)
    {
        Assert.Empty(EnvironmentVariableParser.Parse(input));
    }

    [Fact]
    public void Parse_SkipsValuesContainingNullCharacters()
    {
        Assert.Empty(EnvironmentVariableParser.Parse("KEY=value\0value"));
    }

    [Fact]
    public void Parse_ReturnsDuplicateKeysInOrderSoTheLastOneWinsOnApply()
    {
        var variables = EnvironmentVariableParser.Parse("KEY=first\nKEY=second");

        Assert.Equal(2, variables.Count);
        Assert.Equal("first", variables[0].Value);
        Assert.Equal("second", variables[1].Value);
    }

    [Fact]
    public void ApplyToProcess_WritesVariablesToTheProcessEnvironment()
    {
        var startInfo = new ProcessStartInfo { FileName = "game" };
        startInfo.Environment["EXISTING"] = "built-in";

        var applied = EnvironmentVariableParser.ApplyToProcess(
            startInfo,
            "EXISTING=overridden\nNEW=added");

        Assert.Equal(2, applied);
        Assert.Equal("overridden", startInfo.Environment["EXISTING"]);
        Assert.Equal("added", startInfo.Environment["NEW"]);
    }

    [Fact]
    public void ApplyToProcess_WithoutVariables_AppliesNothing()
    {
        var startInfo = new ProcessStartInfo { FileName = "game" };
        var keysBefore = startInfo.Environment.Keys.ToArray();

        Assert.Equal(0, EnvironmentVariableParser.ApplyToProcess(startInfo, "  \n# none\n"));

        Assert.Equal(keysBefore, startInfo.Environment.Keys.ToArray());
    }
}
