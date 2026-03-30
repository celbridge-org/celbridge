using System.Globalization;
using System.Text.Json;
using Celbridge.Logging;

namespace Celbridge.Packages;

/// <summary>
/// Loads localized strings from a package's localization folder.
/// Uses convention: packages store localization files in a "localization" subfolder
/// as flat key-value JSON dictionaries (e.g., en.json, fr.json).
/// </summary>
public class PackageLocalizationService : IPackageLocalizationService
{
    /// <summary>
    /// Convention: all packages use "localization" as the folder name.
    /// </summary>
    public const string LocalizationFolder = "localization";

    private const string FallbackLocale = "en";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<PackageLocalizationService> _logger;

    public PackageLocalizationService(ILogger<PackageLocalizationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Loads localization strings from a package's localization folder.
    /// Uses convention: {packageFolder}/localization/{locale}.json
    /// If locale is null, uses the current UI culture.
    /// Falls back to "en.json" if the requested locale is not found, then to an empty dictionary.
    /// </summary>
    public Dictionary<string, string> LoadStrings(string packageFolder, string? locale = null)
    {
        locale ??= CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        var localizationFolder = Path.Combine(packageFolder, LocalizationFolder);

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
