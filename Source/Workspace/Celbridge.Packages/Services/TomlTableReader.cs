using Tomlyn.Model;

namespace Celbridge.Packages;

/// <summary>
/// Typed accessors for reading values out of a Tomlyn TomlTable, each returning a safe default when
/// the key is absent or the value has the wrong shape. Shared by the package and editor manifest
/// loaders.
/// </summary>
internal static class TomlTableReader
{
    public static string GetString(TomlTable table, string key)
    {
        if (table.TryGetValue(key, out var value) && value is string str)
        {
            return str;
        }

        return string.Empty;
    }

    public static string? GetStringOrNull(TomlTable table, string key)
    {
        if (table.TryGetValue(key, out var value) && value is string str)
        {
            return str;
        }

        return null;
    }

    public static bool GetBool(TomlTable table, string key)
    {
        if (table.TryGetValue(key, out var value) && value is bool b)
        {
            return b;
        }

        return false;
    }

    public static bool? GetBoolOrNull(TomlTable table, string key)
    {
        if (table.TryGetValue(key, out var value) && value is bool b)
        {
            return b;
        }

        return null;
    }

    public static double? GetDoubleOrNull(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value))
        {
            return null;
        }

        // TOML numbers parse as long when written without a decimal point, so accept both.
        return value switch
        {
            double d => d,
            long l => l,
            _ => null
        };
    }

    public static IReadOnlyList<string> GetStringArray(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value) || value is not TomlArray array)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>(array.Count);
        foreach (var element in array)
        {
            if (element is string str && !string.IsNullOrEmpty(str))
            {
                result.Add(str);
            }
        }

        return result.AsReadOnly();
    }
}
