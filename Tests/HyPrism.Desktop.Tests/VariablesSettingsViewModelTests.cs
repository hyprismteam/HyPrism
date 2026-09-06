// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Headless.XUnit;
using HyPrism.Desktop.Features.Settings;
using HyPrism.Desktop.Localization;
using HyPrism.Desktop.Platform;
using Moq;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class VariablesSettingsViewModelTests
{
    [AvaloniaFact]
    public void PersistedVariablesAreLoadedAsTableRows()
    {
        var settings = CreateSettingsStore("SDL_VIDEODRIVER=x11\nCUSTOM=\"spaced value\"");
        using var viewModel = new SettingsViewModel(
            settings.Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"));

        Assert.Equal(2, viewModel.EnvironmentVariableItems.Count);
        Assert.Equal("SDL_VIDEODRIVER", viewModel.EnvironmentVariableItems[0].Key);
        Assert.Equal("x11", viewModel.EnvironmentVariableItems[0].Value);
        Assert.Equal("CUSTOM=spaced value", viewModel.EnvironmentVariableItems[1].Display);
        Assert.True(viewModel.HasEnvironmentVariables);
        Assert.False(viewModel.HasNoEnvironmentVariables);
        Assert.True(viewModel.EnvironmentVariableItems[^1].IsLast);
    }

    [AvaloniaFact]
    public void VariablesCanBeAddedAndRemovedAsTableRows()
    {
        var settings = CreateSettingsStore(string.Empty);
        using var viewModel = new SettingsViewModel(
            settings.Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"));

        viewModel.ShowAddEnvironmentVariableCommand.Execute(null);
        Assert.True(viewModel.IsAddingEnvironmentVariable);

        viewModel.NewEnvironmentVariable = "MY_VAR=1";
        viewModel.AddEnvironmentVariableCommand.Execute(null);

        Assert.False(viewModel.IsAddingEnvironmentVariable);
        Assert.True(viewModel.HasEnvironmentVariables);
        Assert.Equal("MY_VAR", viewModel.EnvironmentVariableItems[0].Key);
        Assert.Contains("MY_VAR=1", settings.Object.GameEnvironmentVariables, StringComparison.Ordinal);

        viewModel.RemoveEnvironmentVariableCommand.Execute(viewModel.EnvironmentVariableItems[0]);

        Assert.False(viewModel.HasEnvironmentVariables);
        Assert.True(viewModel.HasNoEnvironmentVariables);
        Assert.Equal(string.Empty, settings.Object.GameEnvironmentVariables);
    }

    [AvaloniaFact]
    public void AddingMultipleVariablesPersistsEveryAssignment()
    {
        var settings = CreateSettingsStore(string.Empty);
        using var viewModel = new SettingsViewModel(
            settings.Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"));

        viewModel.ShowAddEnvironmentVariableCommand.Execute(null);
        viewModel.NewEnvironmentVariable = "A=1 B=\"two words\"";
        viewModel.AddEnvironmentVariableCommand.Execute(null);

        Assert.Equal(2, viewModel.EnvironmentVariableItems.Count);
        Assert.Equal(
            "A=1\nB=two words",
            settings.Object.GameEnvironmentVariables);
    }

    [AvaloniaFact]
    public void AddingInvalidInputShowsAnErrorAndKeepsTheModalOpen()
    {
        var settings = CreateSettingsStore(string.Empty);
        using var viewModel = new SettingsViewModel(
            settings.Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"));

        viewModel.ShowAddEnvironmentVariableCommand.Execute(null);
        viewModel.NewEnvironmentVariable = "not an assignment";
        viewModel.AddEnvironmentVariableCommand.Execute(null);

        Assert.True(viewModel.IsAddingEnvironmentVariable);
        Assert.True(viewModel.HasEnvironmentVariablesError);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.EnvironmentVariablesError));
        Assert.False(viewModel.HasEnvironmentVariables);
        Assert.Equal(string.Empty, settings.Object.GameEnvironmentVariables);
    }

    private static Mock<IDesktopSettingsStore> CreateSettingsStore(string environmentVariables)
    {
        var settings = new Mock<IDesktopSettingsStore>();
        settings.SetupGet(service => service.Language).Returns("en-US");
        settings.SetupGet(service => service.BackgroundMode).Returns("auto");
        settings.SetupGet(service => service.GpuPreference).Returns("auto");
        settings.SetupProperty(service => service.JavaArguments, string.Empty);
        settings.SetupProperty(service => service.GameEnvironmentVariables, environmentVariables);
        settings.SetupGet(service => service.AuthDomain).Returns(string.Empty);
        settings.SetupGet(service => service.CustomJavaPath).Returns(string.Empty);
        settings.SetupGet(service => service.AvailableBackgrounds).Returns(
            ["bg_1.jpg", "bg_2.jpg", "bg_3.jpg"]);
        return settings;
    }
}
