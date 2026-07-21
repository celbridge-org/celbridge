namespace Celbridge.Utilities;

/// <summary>
/// Shared coercions for boxed TOML values into the closed raw-value set (string, bool, long, double,
/// list of strings) used by project config and editor manifests.
/// </summary>
public static class TomlValueConverter
{
    /// <summary>
    /// Converts a TOML array's boxed entries to a list of strings. Returns false, with an empty list,
    /// when any entry is not a string, leaving the caller to decide how to report the mismatch.
    /// </summary>
    public static bool TryConvertStringList(IEnumerable<object?> entries, out List<string> values)
    {
        var converted = new List<string>();
        foreach (var entry in entries)
        {
            if (entry is not string stringEntry)
            {
                values = new List<string>();
                return false;
            }
            converted.Add(stringEntry);
        }

        values = converted;
        return true;
    }
}
