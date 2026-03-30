namespace Celbridge.Packages;

/// <summary>
/// Manages localization data for packages.
/// </summary>
public interface IPackageLocalizationService
{
    /// <summary>
    /// Loads localization strings from a package's localization folder.
    /// Uses convention: {packageFolder}/localization/{locale}.json
    /// If locale is null, uses the current UI culture.
    /// Falls back to "en.json" if the requested locale is not found, then to an empty dictionary.
    /// </summary>
    Dictionary<string, string> LoadStrings(string packageFolder, string? locale = null);
}
