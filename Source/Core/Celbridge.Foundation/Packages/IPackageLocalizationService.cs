namespace Celbridge.Packages;

/// <summary>
/// Manages localization data for packages.
/// </summary>
public interface IPackageLocalizationService
{
    /// <summary>
    /// Loads localization strings for the supplied package. Uses the convention
    /// {package.PackageFolder}/localization/{locale}.json, with package.Origin
    /// selecting whether the underlying read crosses the IFileStorage chokepoint.
    /// If locale is null, uses the current UI culture. Falls back to "en.json"
    /// if the requested locale is not found, then to an empty dictionary.
    /// </summary>
    Dictionary<string, string> LoadStrings(PackageInfo package, string? locale = null);
}
