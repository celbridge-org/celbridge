using System.Collections;

namespace Celbridge.Utilities;

/// <summary>
/// Reads typed values out of a config table held as loosely typed key/value pairs, as a TOML or JSON
/// deserializer produces for a table whose shape the host does not model. A value of the wrong shape reads
/// as absent, so a malformed entry falls back to the default rather than failing the document.
/// </summary>
public static class ConfigTableHelper
{
    /// <summary>
    /// Returns the string held at the given key, or the default when the key is absent, blank, or holds
    /// something other than a string.
    /// </summary>
    public static string ReadText(
        IReadOnlyDictionary<string, object?> table,
        string key,
        string defaultValue = "")
    {
        if (!table.TryGetValue(key, out var value) ||
            value is not string text ||
            string.IsNullOrWhiteSpace(text))
        {
            return defaultValue;
        }

        return text;
    }

    /// <summary>
    /// Returns the strings held in the array at the given key, skipping entries that are not strings or are
    /// blank. An absent key, or a value that is not an array, reads as an empty list.
    /// </summary>
    public static IReadOnlyList<string> ReadTextList(
        IReadOnlyDictionary<string, object?> table,
        string key)
    {
        var items = new List<string>();

        // A string is itself enumerable, so it is excluded rather than read one character at a time.
        if (!table.TryGetValue(key, out var value) ||
            value is string ||
            value is not IEnumerable values)
        {
            return items;
        }

        foreach (var entry in values)
        {
            if (entry is string text &&
                !string.IsNullOrWhiteSpace(text))
            {
                items.Add(text);
            }
        }

        return items;
    }

    /// <summary>
    /// Returns the value read as a nested table, or null when it is not one. Deserializers model a table as
    /// a dictionary keyed by string, so anything else is a scalar or an array.
    /// </summary>
    public static IReadOnlyDictionary<string, object?>? ReadTable(object? value)
    {
        if (value is IReadOnlyDictionary<string, object?> readOnlyTable)
        {
            return readOnlyTable;
        }

        if (value is not IEnumerable entries ||
            value is string)
        {
            return null;
        }

        // Tomlyn models a table as IDictionary<string, object>, which is neither the read-only interface nor
        // assignable to it, so the pairs are copied out through the non-generic enumerator.
        var table = new Dictionary<string, object?>();
        foreach (var entry in entries)
        {
            if (entry is not KeyValuePair<string, object> pair)
            {
                return null;
            }

            table[pair.Key] = pair.Value;
        }

        return table;
    }
}
