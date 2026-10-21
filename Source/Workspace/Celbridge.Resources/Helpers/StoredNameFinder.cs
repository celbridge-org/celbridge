using System.IO.Enumeration;
using System.Text;

namespace Celbridge.Resources.Helpers;

/// <summary>
/// Finds the names stored on disk for a path that reaches existing items by other forms of their names. A stored
/// name matches when it is equal in Unicode normalization form C, ignoring case.
/// </summary>
[AllowDirectFileSystemAccess]
internal static class StoredNameFinder
{
    // FileSystemInfo.Attributes reports every flag for an item that does not exist.
    private const System.IO.FileAttributes MissingItemAttributes = (System.IO.FileAttributes)(-1);

    private static readonly char[] PathSeparators =
    {
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar,
    };

    /// <summary>
    /// Returns the path below the folder with each existing part's stored name, from fresh listings. The missing
    /// parts stay as given. Returns null when an existing part matches no stored name, as for a Windows short
    /// name.
    /// </summary>
    public static string? FindStoredPath(string folderPath, string relativePath)
    {
        var segments = relativePath.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);

        var storedPath = folderPath;
        bool partExists = true;

        foreach (var segment in segments)
        {
            var name = segment;
            if (partExists)
            {
                var partPath = Path.Combine(storedPath, segment);
                if (new FileInfo(partPath).Attributes == MissingItemAttributes)
                {
                    partExists = false;
                }
                else
                {
                    var storedName = FindStoredName(storedPath, segment);
                    if (storedName is null)
                    {
                        return null;
                    }
                    name = storedName;
                }
            }

            storedPath = Path.Combine(storedPath, name);
        }

        return storedPath;
    }

    /// <summary>
    /// Returns the stored name in the folder that matches the name. An exact match comes first. Returns null for a
    /// name with no match.
    /// </summary>
    public static string? FindStoredName(string folderPath, string name)
    {
        var normalizedName = NormalizeName(name);
        string? match = null;

        try
        {
            var listing = new FileSystemEnumerable<string>(
                folderPath,
                (ref System.IO.Enumeration.FileSystemEntry entry) => entry.FileName.ToString(),
                FolderCache.ListingOptions);

            foreach (var storedName in listing)
            {
                if (string.Equals(storedName, name, StringComparison.Ordinal))
                {
                    return storedName;
                }

                if (match is null
                    && string.Equals(NormalizeName(storedName), normalizedName, StringComparison.OrdinalIgnoreCase))
                {
                    match = storedName;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return match;
    }

    // A name with an unpaired surrogate has no normalization form, so the name is compared as given.
    private static string NormalizeName(string name)
    {
        try
        {
            return name.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return name;
        }
    }
}
