namespace Celbridge.Extensions;

/// <summary>
/// Manages localization data for extensions.
/// </summary>
public interface IExtensionLocalizationService
{
    /// <summary>
    /// Loads localization strings from an extension's localization directory.
    /// Uses convention: {extensionFolder}/localization/{locale}.json
    /// If locale is null, uses the current UI culture.
    /// Falls back to "en.json" if the requested locale is not found, then to an empty dictionary.
    /// </summary>
    Dictionary<string, string> LoadStrings(string extensionFolder, string? locale = null);
}
