// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Headless.XUnit;
using HyPrism.Desktop.Features.Settings;
using HyPrism.Desktop.Localization;
using HyPrism.Desktop.Platform;
using Moq;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class JavaSettingsViewModelTests
{
    [Fact]
    public void TokenizerPreservesQuotedArgumentValues()
    {
        var arguments = JavaArgumentTokenizer.Split(
            "-Dfile.encoding=UTF-8 -Dmessage=\"hello world\" '-Dsingle=two words'");

        Assert.Equal(
            ["-Dfile.encoding=UTF-8", "-Dmessage=\"hello world\"", "'-Dsingle=two words'"],
            arguments);
    }

    [AvaloniaFact]
    public void JavaArgumentsCanBeAddedAndRemovedAsTableRows()
    {
        var settings = CreateSettingsStore(
            "-Xms1G -Xmx4G -Dfile.encoding=UTF-8 -Dmessage=\"hello world\"");
        using var viewModel = new SettingsViewModel(
            settings.Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"));

        Assert.Equal(2, viewModel.JavaArgumentItems.Count);
        Assert.Equal("-Dmessage=\"hello world\"", viewModel.JavaArgumentItems[1].Value);
        Assert.True(viewModel.JavaArgumentItems[1].IsLast);

        viewModel.ShowAddJavaArgumentCommand.Execute(null);
        viewModel.NewJavaArgument = "-XX:+UseZGC";
        viewModel.AddJavaArgumentCommand.Execute(null);

        Assert.False(viewModel.IsAddingJavaArgument);
        Assert.Equal(3, viewModel.JavaArgumentItems.Count);
        Assert.Contains("-XX:+UseZGC", viewModel.JavaArguments, StringComparison.Ordinal);
        Assert.Contains("-XX:+UseZGC", settings.Object.JavaArguments, StringComparison.Ordinal);

        viewModel.RemoveJavaArgumentCommand.Execute(viewModel.JavaArgumentItems[0]);

        Assert.Equal(2, viewModel.JavaArgumentItems.Count);
        Assert.DoesNotContain("-Dfile.encoding=UTF-8", viewModel.JavaArguments, StringComparison.Ordinal);
        Assert.True(viewModel.JavaArgumentItems[^1].IsLast);
    }

    private static Mock<IDesktopSettingsStore> CreateSettingsStore(string javaArguments)
    {
        var settings = new Mock<IDesktopSettingsStore>();
        settings.SetupGet(service => service.Language).Returns("en-US");
        settings.SetupGet(service => service.BackgroundMode).Returns("auto");
        settings.SetupGet(service => service.GpuPreference).Returns("auto");
        settings.SetupProperty(service => service.JavaArguments, javaArguments);
        settings.SetupGet(service => service.AuthDomain).Returns(string.Empty);
        settings.SetupGet(service => service.CustomJavaPath).Returns(string.Empty);
        settings.SetupGet(service => service.GameEnvironmentVariables).Returns(string.Empty);
        settings.SetupGet(service => service.AvailableBackgrounds).Returns(
            ["bg_1.jpg", "bg_2.jpg", "bg_3.jpg"]);
        return settings;
    }
}
