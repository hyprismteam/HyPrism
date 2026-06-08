using System;

namespace HyPrism.Services.Core.App;

/// <summary>
/// Manages application theming. In the Electron/React architecture, themes are
/// applied via CSS custom properties on the frontend. This service stores the
/// current accent color and can notify the frontend of changes via IPC.
/// </summary>
public class ThemeService : IThemeService, IDisposable
{
    private string _currentAccentColor = "#7C5CFC";

    /// <summary>
    /// Raised when the accent color changes. IPC handler can subscribe
    /// to push the new color to the React frontend.
    /// </summary>
    public event Action<string>? AccentColorChanged;

    /// <summary>
    /// Gets the current accent color hex string.
    /// </summary>
    public string CurrentAccentColor => _currentAccentColor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemeService"/> class.
    /// </summary>
    public ThemeService()
    {
    }

    /// <inheritdoc/>
    public void ApplyAccentColor(string hexColor)
    {
        if (string.IsNullOrWhiteSpace(hexColor)) return;

        // Validate hex color format
        if (!hexColor.StartsWith('#')) hexColor = "#" + hexColor;

        _currentAccentColor = hexColor;
        AccentColorChanged?.Invoke(hexColor);
    }

    /// <inheritdoc/>
    public void Initialize(string initialColor)
    {
        if (!string.IsNullOrWhiteSpace(initialColor))
        {
            _currentAccentColor = initialColor.StartsWith('#') ? initialColor : "#" + initialColor;
        }
    }

    public void Dispose()
    {
        // No unmanaged resources in the Electron version
    }
}
