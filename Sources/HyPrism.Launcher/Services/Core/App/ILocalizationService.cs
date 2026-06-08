namespace HyPrism.Services.Core.App;

/// <summary>
/// Provides localization with event-based notifications for dynamic language switching.
/// Translations are handled by the React frontend (i18next); the backend only
/// tracks the current language preference and validates language codes.
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Gets or sets the current UI language code (e.g., "en-US", "ru-RU").
    /// Setting this property notifies all subscribers.
    /// </summary>
    string CurrentLanguage { get; set; }
    
    /// <summary>
    /// Gets a translated string for the specified key using the current language.
    /// Returns the key as-is since translations are handled by the frontend.
    /// </summary>
    /// <param name="key">The translation key (e.g., "dashboard.play").</param>
    /// <returns>The key itself (frontend handles actual translations).</returns>
    string this[string key] { get; }

    /// <summary>
    /// Raised when the current language changes. Subscribers should refresh their translations.
    /// The event argument is the new language code.
    /// </summary>
    event Action<string>? LanguageChanged;
    
    /// <summary>
    /// Returns the key as-is. Translations are handled by the frontend.
    /// Kept for interface compatibility.
    /// </summary>
    /// <param name="key">The translation key.</param>
    /// <param name="args">Ignored — kept for interface compatibility.</param>
    /// <returns>The key itself.</returns>
    string Translate(string key, params object[] args);
}
