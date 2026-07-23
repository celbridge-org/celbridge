using Celbridge.Logging;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Converts a FontFamily key to a FontFamily object
/// </summary>
public class FontFamilyConverter : IValueConverter
{
    private readonly ILogger<FontFamilyConverter> _logger = ServiceLocator.AcquireService<ILogger<FontFamilyConverter>>();

    /// <summary>
    /// Resolves a font family resource key to the font registered in the application resources, or null
    /// when the key is not registered.
    /// </summary>
    public static FontFamily? Resolve(string fontFamilyKey)
    {
        if (string.IsNullOrEmpty(fontFamilyKey) ||
            !Application.Current.Resources.ContainsKey(fontFamilyKey))
        {
            return null;
        }

        return Application.Current.Resources[fontFamilyKey] as FontFamily;
    }

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string fontFamilyKey)
        {
            var fontFamily = Resolve(fontFamilyKey);
            if (fontFamily is not null)
            {
                return fontFamily;
            }
        }

        // An unresolved key leaves the element on its inherited font, where an icon glyph renders as
        // some unrelated character rather than failing outright.
        _logger.LogWarning($"Font family key not found in application resources: '{value}'");

        return null;
    }

    public object? ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
