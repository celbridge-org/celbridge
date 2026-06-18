namespace Celbridge.Packages;

/// <summary>
/// Validation rules for package names. The package name is the package's
/// unique identifier and matches the name the workshop server knows it by
/// (e.g. "my-widget").
/// </summary>
public static class PackageName
{
    /// <summary>
    /// Returns true if the string is a well-formed package name.
    /// A valid name is lowercase ASCII letters and digits with single interior
    /// hyphens as the only separator, at most PackageConstants.MaxNameLength
    /// characters. The ASCII-only character set is deliberate: it blocks
    /// Unicode homograph attacks where a lookalike (e.g. Cyrillic 'o')
    /// masquerades as its ASCII counterpart. Do not relax this to
    /// char.IsLetter or similar.
    /// </summary>
    public static bool IsValid(string name)
    {
        if (string.IsNullOrEmpty(name) ||
            name.Length > PackageConstants.MaxNameLength)
        {
            return false;
        }

        // A leading or trailing hyphen produces an empty word on one side.
        if (name[0] == '-' || name[^1] == '-')
        {
            return false;
        }

        char previousCharacter = '\0';
        foreach (var character in name)
        {
            if (character == '-')
            {
                if (previousCharacter == '-')
                {
                    // Consecutive hyphens produce an empty word.
                    return false;
                }
            }
            else if (!char.IsAsciiLetterLower(character) &&
                     !char.IsAsciiDigit(character))
            {
                return false;
            }

            previousCharacter = character;
        }

        return true;
    }

    /// <summary>
    /// Returns true if the string is a well-formed bundled package name: one
    /// or more valid package name segments separated by single dots (e.g.
    /// "celbridge.notes"). Dotted names are internal to first-party bundled
    /// packages and are never published to a workshop.
    /// </summary>
    public static bool IsValidBundledName(string name)
    {
        if (string.IsNullOrEmpty(name) ||
            name.Length > PackageConstants.MaxNameLength)
        {
            return false;
        }

        // An empty segment (leading, trailing, or consecutive dots) fails the
        // per-segment check.
        var segments = name.Split('.');
        foreach (var segment in segments)
        {
            if (!IsValid(segment))
            {
                return false;
            }
        }

        return true;
    }
}
