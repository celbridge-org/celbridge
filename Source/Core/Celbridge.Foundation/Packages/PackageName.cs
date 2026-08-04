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

}
