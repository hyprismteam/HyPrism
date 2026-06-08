using System.Text.Json;
using HyPrism.Models;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Tests.Core.Infrastructure;

/// <summary>
/// Tests for <see cref="ConfigService"/> — covers load/save/reset and migration logic.
/// All tests use temporary directories to isolate I/O.
/// </summary>
public class ConfigServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HyPrismTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }


    [Fact]
    public void Constructor_NoConfigFile_CreatesDefaultConfig()
    {
        var svc = new ConfigService(_tempDir);

        Assert.NotNull(svc.Configuration);
        Assert.False(string.IsNullOrEmpty(svc.Configuration.UUID));
        Assert.NotEmpty(svc.Configuration.Nick);
        Assert.True(File.Exists(Path.Combine(_tempDir, "config.json")));
    }

    [Fact]
    public void Constructor_NoConfigFile_GeneratesValidUUID()
    {
        var svc = new ConfigService(_tempDir);
        Assert.True(Guid.TryParse(svc.Configuration.UUID, out _), "UUID should be a valid GUID");
    }


    [Fact]
    public void SaveConfig_PersistsToDisk()
    {
        var svc = new ConfigService(_tempDir);
        svc.Configuration.Nick = "TestPlayer";
        svc.SaveConfig();

        var json = File.ReadAllText(Path.Combine(_tempDir, "config.json"));
        Assert.Contains("TestPlayer", json);
    }

    [Fact]
    public void Constructor_ExistingConfig_LoadsPersistedValues()
    {
        var cfg = new Config { Nick = "SavedPlayer", UUID = Guid.NewGuid().ToString(), Language = "en-US" };
        File.WriteAllText(
            Path.Combine(_tempDir, "config.json"),
            JsonSerializer.Serialize(cfg));

        var svc = new ConfigService(_tempDir);
        Assert.Equal("SavedPlayer", svc.Configuration.Nick);
    }


    [Fact]
    public void ResetConfig_ReplacesConfigWithDefaults()
    {
        var svc = new ConfigService(_tempDir);
        svc.Configuration.Nick = "CustomNick";
        svc.Configuration.MusicEnabled = false;

        svc.ResetConfig();

        // Default MusicEnabled is true
        Assert.True(svc.Configuration.MusicEnabled);
    }


    [Fact]
    public async Task SetInstanceDirectoryAsync_ValidPath_SetsAndPersists()
    {
        var svc = new ConfigService(_tempDir);
        var target = Path.Combine(_tempDir, "custom_instances");

        var result = await svc.SetInstanceDirectoryAsync(target);

        Assert.Equal(target, result);
        Assert.Equal(target, svc.Configuration.InstanceDirectory);
        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public async Task SetInstanceDirectoryAsync_EmptyPath_ClearsDirectory()
    {
        var svc = new ConfigService(_tempDir);
        await svc.SetInstanceDirectoryAsync(Path.Combine(_tempDir, "some_dir"));

        var result = await svc.SetInstanceDirectoryAsync("");

        Assert.Null(result);
        Assert.True(string.IsNullOrEmpty(svc.Configuration.InstanceDirectory));
    }


    [Fact]
    public void Constructor_ConfigWithoutUUID_MigratesAndSavesUUID()
    {
        var cfg = new Config { UUID = "", Nick = "OldPlayer", Language = "en-US" };
        File.WriteAllText(
            Path.Combine(_tempDir, "config.json"),
            JsonSerializer.Serialize(cfg));

        var svc = new ConfigService(_tempDir);

        Assert.False(string.IsNullOrEmpty(svc.Configuration.UUID));
        Assert.True(Guid.TryParse(svc.Configuration.UUID, out _));
    }


    [Fact]
    public void Constructor_InvalidLanguage_FallsBackToEnUS()
    {
        var cfg = new Config { UUID = Guid.NewGuid().ToString(), Nick = "Player", Language = "xx-XX" };
        File.WriteAllText(
            Path.Combine(_tempDir, "config.json"),
            JsonSerializer.Serialize(cfg));

        var svc = new ConfigService(_tempDir);

        // Invalid language code should fall back to "en-US"
        Assert.Equal("en-US", svc.Configuration.Language);
    }


    [Fact]
    public void Constructor_CorruptJson_CreatesDefaultConfig()
    {
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), "{ this is not json }}}");

        var svc = new ConfigService(_tempDir);

        Assert.NotNull(svc.Configuration);
        Assert.False(string.IsNullOrEmpty(svc.Configuration.UUID));
    }
}
