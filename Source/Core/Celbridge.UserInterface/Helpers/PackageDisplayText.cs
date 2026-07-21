using Celbridge.Packages;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Resolves the display text for a package or contribution from its manifest strings. A manifest value
/// may be a package-localization key or a plain string; these helpers look it up in the owning package's
/// localization files and fall back sensibly so a raw key is never shown to the user.
/// </summary>
public static class PackageDisplayText
{
    /// <summary>
    /// Resolves a manifest string to display text: the package-localized value when the string is a key
    /// with an entry, otherwise the string unchanged (a plain display value, or empty when blank).
    /// </summary>
    public static string Resolve(IPackageLocalizationService localizationService, PackageInfo package, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var localizationStrings = localizationService.LoadStrings(package);
        if (localizationStrings.TryGetValue(value, out var localized))
        {
            return localized;
        }

        return value;
    }

    /// <summary>
    /// Title-cases a kebab-case or dotted identifier into display words (e.g. "note-editor" becomes
    /// "Note Editor").
    /// </summary>
    public static string Humanize(string identifier)
    {
        var words = identifier
            .Split('-', '.')
            .Where(word => word.Length > 0)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]);

        return string.Join(" ", words);
    }

    /// <summary>
    /// Humanizes the last dot-separated segment of an identifier (e.g. "celbridge.notepad" becomes
    /// "Notepad").
    /// </summary>
    public static string HumanizeLastSegment(string identifier)
    {
        var segments = identifier.Split('.');
        return Humanize(segments[^1]);
    }
}
