using System.Globalization;
using System.Text.Json;

namespace Celbridge.Extensions;

/// <summary>
/// Helper for loading localized strings from an extension's localization directory.
/// Extension localization files are flat key-value JSON dictionaries (e.g., en.json).
/// </summary>
public static class ExtensionLocalizationHelper
{
    private const string FallbackLocale = "en";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Loads localization strings from an extension's localization directory.
    /// Tries the current UI culture first, falls back to "en", then returns an empty dictionary.
    /// </summary>
    public static Dictionary<string, string> LoadStrings(
        string extensionDirectory,
        string localizationFolder)
    {
        var locale = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return LoadStrings(extensionDirectory, localizationFolder, locale);
    }

    /// <summary>
    /// Loads localization strings for a specific locale from an extension's localization directory.
    /// Falls back to "en.json" if the requested locale is not found, then to an empty dictionary.
    /// </summary>
    public static Dictionary<string, string> LoadStrings(
        string extensionDirectory,
        string localizationFolder,
        string locale)
    {
        var localizationDir = Path.Combine(extensionDirectory, localizationFolder);

        // Try the requested locale
        var localePath = Path.Combine(localizationDir, $"{locale}.json");
        var result = TryLoadJsonFile(localePath);
        if (result is not null)
        {
            return result;
        }

        // Fall back to English if not already requested
        if (locale != FallbackLocale)
        {
            var fallbackPath = Path.Combine(localizationDir, $"{FallbackLocale}.json");
            result = TryLoadJsonFile(fallbackPath);
            if (result is not null)
            {
                return result;
            }
        }

        // No localization files found
        return new Dictionary<string, string>();
    }

    /// <summary>
    /// Attempts to load a JSON localization file. Returns null on any failure.
    /// </summary>
    private static Dictionary<string, string>? TryLoadJsonFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, _jsonOptions);
            return dict;
        }
        catch
        {
            return null;
        }
    }
}
