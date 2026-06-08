using System.Text.Json;
using HyPrism.Models;
using HyPrism.Services.Core.App;

namespace HyPrism.Services.Core.Infrastructure;

/// <summary>
/// Manages launcher configuration persistence including loading, saving, and automatic migrations.
/// Configuration is stored as JSON in the application data directory.
/// </summary>
public class ConfigService : IConfigService
{
  private readonly string _configPath;
  private Config _config;

  /// <inheritdoc/>
  public Config Configuration => _config;

  /// <summary>
  /// Initializes a new instance of the <see cref="ConfigService"/> class.
  /// Loads existing configuration or creates a new one with default values.
  /// </summary>
  /// <param name="appDataPath">The application data directory path where config.json is stored.</param>
  public ConfigService(string appDataPath)
  {
    _configPath = Path.Combine(appDataPath, "config.json");
    _config = LoadConfig();
  }

  /// <summary>
  /// Loads configuration from disk and applies any necessary migrations.
  /// Creates a new configuration with defaults if file doesn't exist or is invalid.
  /// </summary>
  /// <returns>The loaded or newly created configuration.</returns>
  private Config LoadConfig()
  {
    Config config;

    try
    {
      if (File.Exists(_configPath))
      {
        var json = File.ReadAllText(_configPath);
        config = JsonSerializer.Deserialize<Config>(json) ?? new Config();

        Logger.Info("Config", $"Loaded config - Language: '{config.Language}'");

        // Apply migrations
        bool needsSave = false;

        // Migration: Ensure UUID exists
        if (string.IsNullOrEmpty(config.UUID))
        {
          config.UUID = Guid.NewGuid().ToString();
          needsSave = true;
          Logger.Info("Config", $"Generated UUID during migration: {config.UUID}");
        }

        // Validate language code exists in available languages
        var availableLanguages = LocalizationService.GetAvailableLanguages();
        if (!string.IsNullOrEmpty(config.Language) && !availableLanguages.ContainsKey(config.Language))
        {
          // Basic fallback for legacy short codes (e.g. "ru" -> "ru-RU") only if exact match fails
          var bestMatch = availableLanguages.Keys.FirstOrDefault(k => k.StartsWith(config.Language + "-"));
          if (bestMatch != null)
          {
            config.Language = bestMatch;
          }
          else
          {
            // Final fallback if totally invalid
            config.Language = "en-US";
          }
          needsSave = true;
        }

        // Default nick to random name if empty or placeholder

        // Migration: Migrate legacy "latest" branch to release
#pragma warning disable CS0618 // Using obsolete fields for migration
        if (config.VersionType == "latest")
        {
          config.VersionType = "release";
          needsSave = true;
        }
#pragma warning restore CS0618

        // Default nick to random name if empty or placeholder
        if (string.IsNullOrWhiteSpace(config.Nick) || config.Nick == "Player" || config.Nick == "Hyprism" || config.Nick == "HyPrism")
        {
          config.Nick = UtilityService.GenerateRandomUsername();
          needsSave = true;
          Logger.Info("Config", $"Generated random username: {config.Nick}");
        }

        if (needsSave)
        {
          _config = config;
          SaveConfig();
        }

        return config;
      }
    }
    catch (Exception ex)
    {
      Logger.Error("Config", $"Failed to load config: {ex.Message}");
    }

    // New config - generate UUID and defaults
    config = new Config();
    if (string.IsNullOrEmpty(config.UUID))
    {
      config.UUID = Guid.NewGuid().ToString();
    }

    // Default nick to random name
    if (string.IsNullOrWhiteSpace(config.Nick) || config.Nick == "Player")
    {
      config.Nick = UtilityService.GenerateRandomUsername();
    }

    // Validate default language
    var defaultAvailableLanguages = LocalizationService.GetAvailableLanguages();
    if (!defaultAvailableLanguages.ContainsKey(config.Language))
    {
      config.Language = "en-US";
    }

    _config = config;
    SaveConfig();
    return config;
  }

  /// <inheritdoc/>
  public void SaveConfig()
  {
    try
    {
      var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions
      {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
      });

      File.WriteAllText(_configPath, json);
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Failed to save config: {ex.Message}");
    }
  }

  /// <inheritdoc/>
  public void ResetConfig()
  {
    _config = new Config();
    SaveConfig();
  }

  /// <inheritdoc/>
  public Task<string?> SetInstanceDirectoryAsync(string path)
  {
    try
    {
      // If path is empty or whitespace, clear the custom instance directory
      if (string.IsNullOrWhiteSpace(path))
      {
        _config.InstanceDirectory = null!;
        SaveConfig();
        Logger.Success("Config", "Instance directory cleared, using default");
        return Task.FromResult<string?>(null);
      }

      var expanded = Environment.ExpandEnvironmentVariables(path.Trim());

      // If not rooted, combine with app directory (parent of config file)
      if (!Path.IsPathRooted(expanded))
      {
        expanded = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_configPath)!, expanded));
      }

      Directory.CreateDirectory(expanded);

      _config.InstanceDirectory = expanded;
      SaveConfig();

      Logger.Success("Config", $"Instance directory set to {expanded}");
      return Task.FromResult<string?>(expanded);
    }
    catch (Exception ex)
    {
      Logger.Error("Config", $"Failed to set instance directory: {ex.Message}");
      return Task.FromResult<string?>(null);
    }
  }
}
