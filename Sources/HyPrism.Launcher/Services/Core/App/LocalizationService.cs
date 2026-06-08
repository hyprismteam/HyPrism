using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.Core.App;

/// <summary>
/// Provides localization and internationalization services for the application.
/// Manages language preference tracking and validation.
/// </summary>
/// <remarks>
/// Translations are handled entirely by the React frontend (i18next).
/// The backend only tracks the current language preference and validates language codes.
/// </remarks>
public class LocalizationService : ILocalizationService
{
    // Static accessor for convenience (set from DI during initialization).
    internal static LocalizationService? Current { get; set; }

    /// <summary>
    /// Raised when the current language changes. Subscribers can refresh their UI.
    /// </summary>
    public event Action<string>? LanguageChanged;

    private string _currentLanguage = "en-US";

    /// <summary>
    /// Static list of supported languages. Must match the locales bundled in the frontend
    /// (Frontend/src/assets/locales/*.json). The backend does not load translations —
    /// this list is only used for language code validation.
    /// </summary>
    private static readonly Dictionary<string, string> SupportedLanguages = new()
    {
        ["en-US"] = "English",
        ["ru-RU"] = "Русский",
        ["de-DE"] = "Deutsch",
        ["es-ES"] = "Español",
        ["fr-FR"] = "Français",
        ["ja-JP"] = "日本語",
        ["ko-KR"] = "한국어",
        ["pt-BR"] = "Português (Brasil)",
        ["tr-TR"] = "Türkçe",
        ["uk-UA"] = "Українська",
        ["zh-CN"] = "简体中文",
        ["be-BY"] = "Беларуская",
    };

    /// <summary>
    /// Gets available languages.
    /// Returns Dictionary where Key = language code (e.g. "ru-RU"), Value = native name (e.g. "Русский").
    /// </summary>
    public static Dictionary<string, string> GetAvailableLanguages() => new(SupportedLanguages);

    /// <summary>
    /// Gets or sets the current language code (e.g., "en-US", "ru-RU").
    /// Setting this property notifies subscribers so the frontend can update.
    /// </summary>
    /// <value>The BCP 47 language tag of the current language.</value>
    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
             if (!SupportedLanguages.ContainsKey(value))
             {
                 Logger.Warning("Localization", $"Invalid language code: {value}, keeping: {_currentLanguage}");
                 return;
             }

            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                Logger.Info("Localization", $"Language changed to: {value}");
                LanguageChanged?.Invoke(value);
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalizationService"/> class.
    /// </summary>
    public LocalizationService() { }

    /// <inheritdoc/>
    public string GetTranslation(string key) => Translate(key);

    /// <summary>
    /// Returns the key as-is. Translations are handled by the frontend.
    /// Kept for interface compatibility.
    /// </summary>
    public string Translate(string key, params object[] args) => key;

    /// <summary>
    /// Returns the key as-is. Translations are handled by the frontend.
    /// Kept for interface compatibility.
    /// </summary>
    public string this[string key] => key;
}
