using System.Globalization;
using Tomlyn.Model;

namespace Celbridge.Projects.Services;

/// <summary>
/// Minimal JSON Pointer style reader over a Tomlyn table.
/// </summary>

internal static class JsonPointerToml
{
    public static string Escape(string token)
        => token.Replace("~", "~0").Replace("/", "~1");

    private static string Unescape(string token)
        => token.Replace("~1", "/").Replace("~0", "~");

    private static string[] Parse(string pointer)
    {
        if (pointer is null) throw new ArgumentNullException(nameof(pointer));
        if (pointer.Length == 0) return Array.Empty<string>();
        if (pointer[0] != '/') throw new ArgumentException("Pointer must start with '/'.", nameof(pointer));
        return pointer.Split('/', StringSplitOptions.RemoveEmptyEntries)
                        .Select(Unescape)
                        .ToArray();
    }

    public static bool TryResolve(TomlTable root, string pointer, out object? node, out object? parent)
    {
        node = root; parent = null;
        if (pointer == "") return true;

        var segs = Parse(pointer);
        object? current = root;
        object? prev = null;

        foreach (var seg in segs)
        {
            prev = current;

            switch (current)
            {
                case TomlTable table:
                    if (!table.TryGetValue(seg, out current))
                    {
                        node = null; parent = prev; return false;
                    }
                    break;

                case TomlArray array:
                    if (seg == "-") { node = null; parent = prev; return false; }
                    if (!int.TryParse(seg, NumberStyles.None, CultureInfo.InvariantCulture, out var idx))
                    {
                        node = null; parent = prev; return false;
                    }
                    if (idx < 0 || idx >= array.Count)
                    {
                        node = null; parent = prev; return false;
                    }
                    current = array[idx];
                    break;

                default:
                    node = null; parent = prev; return false;
            }
        }

        node = current;
        parent = prev;
        return true;
    }
}
