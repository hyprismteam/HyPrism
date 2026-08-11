// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using HyPrism.Desktop.Features.Settings;
using HyPrism.Core.Infrastructure;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class DesktopSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"HyPrismDesktopSettings_{Guid.NewGuid():N}");
    private readonly JsonConfigStore _config;
    private readonly DesktopSettingsStore _settings;

    public DesktopSettingsStoreTests()
    {
        _config = new JsonConfigStore(_directory);
        _settings = new DesktopSettingsStore(_config);
    }

    [Fact]
    public void Language_PersistsSupportedPreference()
    {
        _settings.Language = "ru-RU";

        Assert.Equal("ru-RU", _config.Configuration.Language);
    }

    [Fact]
    public void GpuPreference_NormalizesUnknownValue()
    {
        _settings.GpuPreference = "unsupported";

        Assert.Equal("dedicated", _config.Configuration.GpuPreference);
    }

    [Fact]
    public void BackgroundMode_RaisesChangeAfterSaving()
    {
        string? received = null;
        _settings.BackgroundChanged += value => received = value;

        _settings.BackgroundMode = "bg_4.png";

        Assert.Equal("bg_4.png", _config.Configuration.BackgroundMode);
        Assert.Equal("bg_4.png", received);
    }

    [Fact]
    public void AvailableBackgrounds_ContainsAutoPickerAssets()
    {
        Assert.Contains("bg_1.jpg", _settings.AvailableBackgrounds);
        Assert.Contains("bg_4.png", _settings.AvailableBackgrounds);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }
}
