using System.Globalization;
using System.Text.Json;
using Celbridge.Logging;

namespace Celbridge.Extensions;

/// <summary>
/// Loads localized strings from an extension's localization directory.
/// Uses convention: extensions store localization files in a "localization" subdirectory
/// as flat key-value JSON dictionaries (e.g., en.json, fr.json).
/// </summary>
public class ExtensionLocalizationService
{
    /// <summary>
    /// Convention: all extensions use "localization" as the folder name.
    /// </summary>
    public const string LocalizationFolder = "localization";

    private const string FallbackLocale = "en";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<ExtensionLocalizationService> _logger;

    public ExtensionLocalizationService(ILogger<ExtensionLocalizationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Loads localization strings from an extension's localization directory.
    /// Uses convention: {extensionDirectory}/localization/{locale}.json
    /// Tries the current UI culture first, falls back to "en", then returns an empty dictionary.
    /// </summary>
    public Dictionary<string, string> LoadStrings(string extensionDirectory)
    {
        var locale = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return LoadStrings(extensionDirectory, locale);
    }

    /// <summary>
    /// Loads localization strings for a specific locale from an extension's localization directory.
    /// Uses convention: {extensionDirectory}/localization/{locale}.json
    /// Falls back to "en.json" if the requested locale is not found, then to an empty dictionary.
    /// </summary>
    public Dictionary<string, string> LoadStrings(string extensionDirectory, string locale)
    {
        var localizationFolder = Path.Combine(extensionDirectory, LocalizationFolder);

        var localePath = Path.Combine(localizationFolder, $"{locale}.json");
        var result = TryLoadJsonFile(localePath);
        if (result is not null)
        {
            return result;
        }

        if (locale != FallbackLocale)
        {
            var fallbackPath = Path.Combine(localizationFolder, $"{FallbackLocale}.json");
            result = TryLoadJsonFile(fallbackPath);
            if (result is not null)
            {
                return result;
            }
        }

        return new Dictionary<string, string>();
    }

    private Dictionary<string, string>? TryLoadJsonFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            var dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(json, _jsonOptions);
            return dictionary;
        }
        catch (Exception exception)
        {
            _logger.LogWarning($"Failed to load localization file: {path}. {exception.Message}");
            return null;
        }
    }
}
