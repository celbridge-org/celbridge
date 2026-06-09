using System.Globalization;
using System.Text;
using Tomlyn.Model;

namespace Celbridge.Resources.Helpers;

/// <summary>
/// Pure-function TOML encoder for sidecar fields. Emits a deterministic,
/// human-readable on-disk representation: reserved root fields appear first in
/// the canonical order; remaining keys at each scope sort alphabetically; each
/// string value picks its quote style by content so that round-trip is byte
/// perfect on read.
/// </summary>
public static class SidecarTomlEncoder
{
    /// <summary>
    /// Canonical ordering for the reserved root-level fields. Reserved entries
    /// present in the value are emitted in this order; remaining root keys
    /// follow alphabetically. Append new reserved field names here when they
    /// are introduced; the constant is the single source of truth.
    /// </summary>
    public static readonly IReadOnlyList<string> ReservedFieldOrder = new[]
    {
        SidecarFieldNames.Editor,
        SidecarFieldNames.Tags,
    };

    /// <summary>
    /// Returns the on-disk TOML literal for an arbitrary string value. The
    /// selection rule is applied in order: bare basic string for single-line
    /// content with no special characters; literal triple-quoted string for
    /// multi-line content that does not contain three consecutive single
    /// quotes; basic triple-quoted string with minimal escaping otherwise.
    /// The function is referentially transparent.
    /// </summary>
    public static string EncodeTomlStringValue(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        if (raw.Length == 0)
        {
            return "\"\"";
        }

        if (CanUseBasicString(raw))
        {
            return "\"" + raw + "\"";
        }

        if (CanUseLiteralTriple(raw))
        {
            // The newline immediately after the opening fence is stripped by
            // the parser, so the byte sequence inside the delimiters is the
            // exact raw value.
            return "'''\n" + raw + "'''";
        }

        var escaped = EscapeForBasicTriple(raw);
        return "\"\"\"\n" + escaped + "\"\"\"";
    }

    /// <summary>
    /// Encodes a field dictionary as a TOML document. Root-level keys are
    /// ordered reserved-first then alphabetical; nested tables are emitted in
    /// standard table form alphabetically; each string value runs through
    /// EncodeTomlStringValue.
    /// </summary>
    public static string EncodeFields(IReadOnlyDictionary<string, object> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var builder = new StringBuilder();
        EncodeTable(builder, fields, isRoot: true, parentPath: string.Empty);
        return builder.ToString();
    }

    private static void EncodeTable(
        StringBuilder builder,
        IReadOnlyDictionary<string, object> table,
        bool isRoot,
        string parentPath)
    {
        var scalarKeys = new List<string>();
        var tableKeys = new List<string>();
        foreach (var (key, value) in table)
        {
            if (IsNestedTable(value))
            {
                tableKeys.Add(key);
            }
            else
            {
                scalarKeys.Add(key);
            }
        }

        var orderedScalars = isRoot
            ? OrderRootKeys(scalarKeys)
            : OrderAlphabetical(scalarKeys);

        foreach (var key in orderedScalars)
        {
            builder.Append(EncodeKey(key));
            builder.Append(" = ");
            builder.Append(EncodeValue(table[key]));
            builder.Append('\n');
        }

        var orderedTables = isRoot
            ? OrderRootKeys(tableKeys)
            : OrderAlphabetical(tableKeys);

        foreach (var key in orderedTables)
        {
            var childPath = string.IsNullOrEmpty(parentPath)
                ? EncodeKey(key)
                : parentPath + "." + EncodeKey(key);

            if (builder.Length > 0
                && builder[builder.Length - 1] != '\n')
            {
                builder.Append('\n');
            }
            builder.Append('\n');
            builder.Append('[');
            builder.Append(childPath);
            builder.Append("]\n");

            var child = ToDictionary(table[key]);
            EncodeTable(builder, child, isRoot: false, parentPath: childPath);
        }
    }

    // Reserved root entries first in the canonical order; unknown
    // underscore-prefixed names next, alphabetical; user keys last, alphabetical.
    private static List<string> OrderRootKeys(IEnumerable<string> keys)
    {
        var present = new HashSet<string>(keys, StringComparer.Ordinal);
        var reserved = new List<string>();
        foreach (var name in ReservedFieldOrder)
        {
            if (present.Contains(name))
            {
                reserved.Add(name);
            }
        }

        var unknownReserved = new List<string>();
        var userKeys = new List<string>();
        foreach (var name in present)
        {
            if (ReservedFieldOrder.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }
            if (name.StartsWith("_", StringComparison.Ordinal))
            {
                unknownReserved.Add(name);
            }
            else
            {
                userKeys.Add(name);
            }
        }
        unknownReserved.Sort(StringComparer.Ordinal);
        userKeys.Sort(StringComparer.Ordinal);

        var ordered = new List<string>(reserved.Count + unknownReserved.Count + userKeys.Count);
        ordered.AddRange(reserved);
        ordered.AddRange(unknownReserved);
        ordered.AddRange(userKeys);
        return ordered;
    }

    private static List<string> OrderAlphabetical(IEnumerable<string> keys)
    {
        var sorted = new List<string>(keys);
        sorted.Sort(StringComparer.Ordinal);
        return sorted;
    }

    private static string EncodeKey(string key)
    {
        // Bare keys allow letters, digits, underscores, hyphens. Anything else
        // is quoted as a basic string literal.
        if (key.Length == 0)
        {
            return "\"\"";
        }
        foreach (var c in key)
        {
            bool bare = (c >= 'A' && c <= 'Z')
                || (c >= 'a' && c <= 'z')
                || (c >= '0' && c <= '9')
                || c == '_'
                || c == '-';
            if (!bare)
            {
                return EncodeTomlStringValue(key);
            }
        }
        return key;
    }

    private static string EncodeValue(object? value)
    {
        if (value is null)
        {
            return "\"\"";
        }

        switch (value)
        {
            case string s:
                return EncodeTomlStringValue(s);
            case bool b:
                return b ? "true" : "false";
            case long l:
                return l.ToString(CultureInfo.InvariantCulture);
            case int i:
                return i.ToString(CultureInfo.InvariantCulture);
            case short sh:
                return sh.ToString(CultureInfo.InvariantCulture);
            case ulong ul:
                return ul.ToString(CultureInfo.InvariantCulture);
            case uint ui:
                return ui.ToString(CultureInfo.InvariantCulture);
            case double d:
                return EncodeDouble(d);
            case float f:
                return EncodeDouble(f);
            case decimal m:
                return m.ToString(CultureInfo.InvariantCulture);
            case DateTimeOffset dto:
                return dto.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture);
            case DateTime dt:
                return dt.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
            case DateOnly dateOnly:
                return dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            case TimeOnly timeOnly:
                return timeOnly.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
        }

        if (value is System.Collections.IEnumerable enumerable
            && value is not string)
        {
            return EncodeArray(enumerable);
        }

        // Unknown shapes fall back to a quoted string of their text rep so the
        // encoder is total. The data-tool surface gates indexable shapes long
        // before this point.
        return EncodeTomlStringValue(value.ToString() ?? string.Empty);
    }

    private static string EncodeDouble(double value)
    {
        if (double.IsNaN(value))
        {
            return "nan";
        }
        if (double.IsPositiveInfinity(value))
        {
            return "inf";
        }
        if (double.IsNegativeInfinity(value))
        {
            return "-inf";
        }
        // Round-trip format. Append .0 when the value has no fractional or
        // exponent component so the TOML parser reads it back as a float.
        var formatted = value.ToString("R", CultureInfo.InvariantCulture);
        if (!formatted.Contains('.')
            && !formatted.Contains('e')
            && !formatted.Contains('E')
            && !formatted.Contains('n')
            && !formatted.Contains('i'))
        {
            formatted += ".0";
        }
        return formatted;
    }

    private static string EncodeArray(System.Collections.IEnumerable enumerable)
    {
        var builder = new StringBuilder();
        builder.Append('[');
        bool first = true;
        foreach (var item in enumerable)
        {
            if (!first)
            {
                builder.Append(", ");
            }
            builder.Append(EncodeValue(item));
            first = false;
        }
        builder.Append(']');
        return builder.ToString();
    }

    private static bool IsNestedTable(object? value)
    {
        return value is TomlTable
            || value is IReadOnlyDictionary<string, object>
            || value is IDictionary<string, object>;
    }

    private static IReadOnlyDictionary<string, object> ToDictionary(object value)
    {
        if (value is IReadOnlyDictionary<string, object> readOnly)
        {
            return readOnly;
        }
        if (value is IDictionary<string, object> mutable)
        {
            return new Dictionary<string, object>(mutable);
        }
        if (value is TomlTable tomlTable)
        {
            var copied = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var (key, child) in tomlTable)
            {
                copied[key] = child!;
            }
            return copied;
        }
        throw new InvalidOperationException($"Value of type '{value.GetType().FullName}' is not a table.");
    }

    private static bool CanUseBasicString(string raw)
    {
        foreach (var c in raw)
        {
            if (c == '\n'
                || c == '\r'
                || c == '\\'
                || c == '"')
            {
                return false;
            }
            if (c < 0x20
                || c == 0x7F)
            {
                return false;
            }
        }
        return true;
    }

    // Literal triple-quoted strings disallow runs of three or more single quotes
    // anywhere in the content and have no escape mechanism. A trailing single
    // quote also breaks the form because it merges with the closing delimiter.
    // Control characters other than tab, LF, and CR are not permitted in literal
    // strings.
    private static bool CanUseLiteralTriple(string raw)
    {
        if (raw.Contains("'''", StringComparison.Ordinal))
        {
            return false;
        }
        if (raw.EndsWith("'", StringComparison.Ordinal))
        {
            return false;
        }
        foreach (var c in raw)
        {
            if (c < 0x20
                && c != '\t'
                && c != '\n'
                && c != '\r')
            {
                return false;
            }
            if (c == 0x7F)
            {
                return false;
            }
        }
        return true;
    }

    // Escape backslashes, break any run of three or more consecutive double
    // quotes, and break the tail so trailing quotes don't merge with the
    // closing delimiter. Control characters other than tab, LF, and CR are
    // emitted as \uXXXX escapes.
    private static string EscapeForBasicTriple(string raw)
    {
        var builder = new StringBuilder(raw.Length + 8);
        int run = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (c == '\\')
            {
                builder.Append("\\\\");
                run = 0;
                continue;
            }
            if (c == '"')
            {
                run++;
                if (run == 3)
                {
                    builder.Append("\\\"");
                    run = 0;
                }
                else
                {
                    builder.Append('"');
                }
                continue;
            }
            if (c == '\n'
                || c == '\r'
                || c == '\t')
            {
                builder.Append(c);
                run = 0;
                continue;
            }
            if (c < 0x20
                || c == 0x7F)
            {
                builder.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:X4}", (int)c);
                run = 0;
                continue;
            }
            builder.Append(c);
            run = 0;
        }

        if (run > 0)
        {
            // The content tail is one or two consecutive quotes and would merge
            // with the closing delimiter. Replace the last emitted quote with
            // its escaped form to break the merge.
            int lastIndex = builder.Length - 1;
            builder.Remove(lastIndex, 1);
            builder.Append("\\\"");
        }

        return builder.ToString();
    }
}
