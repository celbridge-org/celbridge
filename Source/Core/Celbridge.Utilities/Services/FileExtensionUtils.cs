namespace Celbridge.Utilities;

/// <summary>
/// Shared validation for file extensions declared in editor manifests and project config.
/// </summary>
public static class FileExtensionUtils
{
    /// <summary>
    /// Whether the value is a well-formed file extension: a leading dot followed by one or more
    /// dot-separated segments (so multi-part extensions like ".tar.gz" are allowed), each segment
    /// non-empty and made only of ASCII letters, digits, hyphens, or underscores. Callers lowercase
    /// the value so extension matching is case-insensitive.
    /// </summary>
    public static bool IsWellFormedFileExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension)
            || extension.Length < 2
            || extension[0] != '.')
        {
            return false;
        }

        var segments = extension.Substring(1).Split('.');
        foreach (var segment in segments)
        {
            if (segment.Length == 0)
            {
                return false;
            }

            foreach (var character in segment)
            {
                var isAllowed = char.IsAsciiLetterOrDigit(character)
                    || character == '-'
                    || character == '_';
                if (!isAllowed)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
