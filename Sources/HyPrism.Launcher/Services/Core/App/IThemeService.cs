namespace HyPrism.Services.Core.App;

/// <summary>
/// Manages application theming including accent color application with smooth transitions.
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// Applies the specified accent color to the application theme with an animated transition.
    /// </summary>
    /// <param name="hexColor">The accent color in hexadecimal format (e.g., "#FF5500" or "#F50").</param>
    void ApplyAccentColor(string hexColor);
    
    /// <summary>
    /// Initializes the theme service with the specified initial accent color.
    /// Should be called once during application startup after UI is ready.
    /// </summary>
    /// <param name="initialColor">The initial accent color in hexadecimal format.</param>
    void Initialize(string initialColor);
}
