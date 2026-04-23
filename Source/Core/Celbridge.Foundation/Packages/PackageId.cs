namespace Celbridge.Packages;

/// <summary>
/// Validation rules for package ids.
/// Package ids are lowercase kebab-case strings that may optionally use
/// dot-separated namespace segments (e.g. "my-mod" or "celbridge.notes").
/// </summary>
public static class PackageId
{
    /// <summary>
    /// Returns true if the string is a well-formed package id.
    /// A valid id is non-empty, uses only lowercase ASCII letters, digits,
    /// dots, and hyphens, and has no leading, trailing, or consecutive dots.
    /// The ASCII-only character set is deliberate: it blocks Unicode homograph
    /// attacks where a lookalike (e.g. Cyrillic 'o') masquerades as its ASCII
    /// counterpart. Do not relax this to char.IsLetter or similar.
    /// </summary>
    public static bool IsValid(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        // Leading or trailing dot would produce an empty namespace or name segment.
        if (id[0] == '.' || id[^1] == '.')
        {
            return false;
        }

        char previousCharacter = '\0';
        foreach (var character in id)
        {
            if (character == '.')
            {
                if (previousCharacter == '.')
                {
                    // Consecutive dots produce an empty segment.
                    return false;
                }
            }
            else if (!char.IsAsciiLetterLower(character) &&
                     !char.IsAsciiDigit(character) &&
                     character != '-')
            {
                return false;
            }

            previousCharacter = character;
        }

        return true;
    }
}
