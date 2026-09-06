// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Headless.XUnit;
using HyPrism.Core.Application.Ports;
using HyPrism.Core.Game.Launch;
using HyPrism.Desktop.Features.Settings;
using HyPrism.Desktop.Localization;
using HyPrism.Desktop.Platform;
using Moq;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class GraphicsSettingsViewModelTests
{
    private static readonly GpuAdapterInfo Rtx = new()
    {
        Name = "NVIDIA GeForce RTX 4070",
        Vendor = "NVIDIA",
        Type = "dedicated",
        PciId = "0000:01:00.0"
    };

    private static readonly GpuAdapterInfo IntegratedUhd = new()
    {
        Name = "Intel(R) UHD Graphics 770",
        Vendor = "Intel",
        Type = "integrated",
        PciId = "0000:00:02.0"
    };

    private static readonly GpuAdapterInfo NameOnlyRadeon = new()
    {
        Name = "AMD Radeon RX 7800 XT",
        Vendor = "AMD",
        Type = "dedicated"
    };

    [AvaloniaFact]
    public void GpuPickerListsDetectedCardsAndDefaultsToDiscrete()
    {
        using var viewModel = CreateViewModel(Rtx, IntegratedUhd);

        Assert.Equal(
            ["pci:0000:01:00.0", "pci:0000:00:02.0", "auto"],
            viewModel.GpuPreferences.Select(choice => choice.Value).ToArray());
        Assert.Equal("NVIDIA GeForce RTX 4070", viewModel.GpuPreferences[0].Display);
        Assert.Equal("pci:0000:01:00.0", viewModel.SelectedGpuPreference.Value);
    }

    [AvaloniaFact]
    public void GpuPickerFallsBackToCardNameWhenPciIdIsUnknown()
    {
        using var viewModel = CreateViewModel(NameOnlyRadeon);

        Assert.Equal(
            ["AMD Radeon RX 7800 XT", "auto"],
            viewModel.GpuPreferences.Select(choice => choice.Value).ToArray());
        Assert.Equal("AMD Radeon RX 7800 XT", viewModel.SelectedGpuPreference.Value);
    }

    [AvaloniaFact]
    public void LegacyIntegratedPreferenceMapsOntoIntegratedCard()
    {
        var settings = CreateSettingsStore();
        settings.SetupGet(service => service.GpuPreference).Returns("integrated");

        using var viewModel = new SettingsViewModel(
            settings.Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"),
            gpuProvider: CreateGpuProvider(Rtx, IntegratedUhd));

        Assert.Equal("pci:0000:00:02.0", viewModel.SelectedGpuPreference.Value);
    }

    [AvaloniaFact]
    public void PersistedAdapterKeySurvivesReopen()
    {
        var settings = CreateSettingsStore();
        settings.SetupGet(service => service.GpuPreference).Returns("pci:0000:00:02.0");

        using var viewModel = new SettingsViewModel(
            settings.Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"),
            gpuProvider: CreateGpuProvider(Rtx, IntegratedUhd));

        Assert.Equal("pci:0000:00:02.0", viewModel.SelectedGpuPreference.Value);
    }

    [AvaloniaFact]
    public void PersistedAutoStaysAuto()
    {
        var settings = CreateSettingsStore();
        settings.SetupGet(service => service.GpuPreference).Returns("auto");

        using var viewModel = new SettingsViewModel(
            settings.Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"),
            gpuProvider: CreateGpuProvider(Rtx, IntegratedUhd));

        Assert.Equal("auto", viewModel.SelectedGpuPreference.Value);
    }

    [AvaloniaFact]
    public void UnknownAdapterFallsBackToDiscreteCard()
    {
        var settings = CreateSettingsStore();
        settings.SetupGet(service => service.GpuPreference).Returns("pci:0000:09:0f.0");

        using var viewModel = new SettingsViewModel(
            settings.Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"),
            gpuProvider: CreateGpuProvider(Rtx, IntegratedUhd));

        Assert.Equal("pci:0000:01:00.0", viewModel.SelectedGpuPreference.Value);
    }

    [AvaloniaFact]
    public void WithoutAdaptersOnlyAutoRemains()
    {
        using var viewModel = CreateViewModel();

        Assert.Equal(["auto"], viewModel.GpuPreferences.Select(choice => choice.Value).ToArray());
        Assert.Equal("auto", viewModel.SelectedGpuPreference.Value);
    }

    [AvaloniaFact]
    public void WithoutProviderOnlyAutoRemains()
    {
        using var viewModel = new SettingsViewModel(
            CreateSettingsStore().Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"));

        Assert.Equal(["auto"], viewModel.GpuPreferences.Select(choice => choice.Value).ToArray());
        Assert.Equal("auto", viewModel.SelectedGpuPreference.Value);
    }

    [AvaloniaTheory]
    [InlineData("Microsoft Basic Render Driver", true)]
    [InlineData("Microsoft Hyper-V Video", true)]
    [InlineData("VMware SVGA 3D", true)]
    [InlineData("NVIDIA GeForce RTX 4070", false)]
    [InlineData("AMD Radeon(TM) Graphics", false)]
    public void VirtualAdaptersAreMarkedForExclusion(string name, bool expected)
        => Assert.Equal(expected, GpuProvider.IsVirtualAdapter(name));

    [AvaloniaTheory]
    [InlineData("NVIDIA GeForce RTX 4070", "dedicated")]
    [InlineData("AMD Radeon RX 7800 XT", "dedicated")]
    [InlineData("Radeon RX Vega 64", "dedicated")]
    [InlineData("Intel(R) Arc(TM) A770", "dedicated")]
    [InlineData("Intel(R) UHD Graphics 770", "integrated")]
    [InlineData("AMD Radeon(TM) Graphics", "integrated")]
    [InlineData("AMD Radeon 780M", "integrated")]
    public void AdapterClassificationMatchesLineups(string name, string expected)
        => Assert.Equal(expected, GpuProvider.Classify(name));

    private static SettingsViewModel CreateViewModel(params GpuAdapterInfo[] adapters)
        => new(
            CreateSettingsStore().Object,
            new Mock<IExternalUriLauncher>().Object,
            new StringLocalizer("en-US"),
            gpuProvider: CreateGpuProvider(adapters));

    private static IGpuProvider CreateGpuProvider(params GpuAdapterInfo[] adapters)
    {
        var gpuProvider = new Mock<IGpuProvider>();
        gpuProvider
            .Setup(service => service.GetAdapters())
            .Returns(adapters.ToList());
        return gpuProvider.Object;
    }

    private static Mock<IDesktopSettingsStore> CreateSettingsStore()
    {
        var settings = new Mock<IDesktopSettingsStore>();
        settings.SetupGet(service => service.Language).Returns("en-US");
        settings.SetupGet(service => service.BackgroundMode).Returns("auto");
        settings.SetupGet(service => service.GpuPreference).Returns("dedicated");
        settings.SetupProperty(service => service.JavaArguments, string.Empty);
        settings.SetupGet(service => service.AuthDomain).Returns(string.Empty);
        settings.SetupGet(service => service.CustomJavaPath).Returns(string.Empty);
        settings.SetupGet(service => service.AvailableBackgrounds).Returns(
            ["bg_1.jpg", "bg_2.jpg", "bg_3.jpg"]);
        return settings;
    }
}
