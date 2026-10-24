using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;

namespace Celbridge.Resources.Helpers;

/// <summary>
/// Parses and composes the .cel sidecar format. A sidecar file is plain TOML.
/// The helpers wrap Tomlyn for reads and SidecarTomlEncoder for deterministic
/// writes.
/// </summary>
public static class SidecarHelper
{
    /// <summary>
    /// True when the value can be written through the structured field
    /// surface: scalars (string, numeric, bool, datetime) and lists of those.
    /// Nested objects and mixed lists are rejected.
    /// </summary>
    public static bool IsIndexableValue(object? value)
    {
        if (value is null)
        {
            return false;
        }
        if (IsScalar(value))
        {
            return true;
        }
        if (value is System.Collections.IEnumerable enumerable
            && value is not string)
        {
            foreach (var item in enumerable)
            {
                if (item is null
                    || !IsScalar(item))
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Extracts a string list from a field value (e.g. the tags field).
    /// Returns an empty list when the value is missing or not a list-of-string.
    /// </summary>
    public static IReadOnlyList<string> ExtractStringList(object? value)
    {
        var result = new List<string>();
        if (value is null
            || value is string)
        {
            return result;
        }
        if (value is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is string s)
                {
                    result.Add(s);
                }
            }
        }
        return result;
    }

    private static bool IsScalar(object value)
    {
        return value is string
            || value is bool
            || value is long
            || value is int
            || value is double
            || value is float
            || value is decimal
            || value is DateTime
            || value is DateTimeOffset
            || value is DateOnly
            || value is TimeOnly;
    }

    /// <summary>
    /// Parses sidecar text as TOML. Returns the field dictionary on success
    /// and a typed failure describing the TOML diagnostics on rejection.
    /// </summary>
    public static Result<SidecarContent> Parse(string text)
    {
        if (text is null)
        {
            return Result<SidecarContent>.Fail("Sidecar content is null.");
        }

        // Strip an optional UTF-8 BOM so Tomlyn sees the first content character.
        if (text.Length > 0
            && text[0] == '﻿')
        {
            text = text.Substring(1);
        }

        var parseResult = ParseFieldsToml(text);
        if (parseResult.IsFailure)
        {
            return Result.Fail(parseResult);
        }

        return Result<SidecarContent>.Ok(new SidecarContent(parseResult.Value));
    }

    /// <summary>
    /// Composes a sidecar text from its field dictionary using the
    /// deterministic encoder.
    /// </summary>
    public static string Compose(SidecarContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Compose(content.Fields);
    }

    /// <summary>
    /// Composes a sidecar text from a field dictionary using the deterministic
    /// encoder. Same input always produces byte-identical output.
    /// </summary>
    public static string Compose(IReadOnlyDictionary<string, object> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (fields.Count == 0)
        {
            return string.Empty;
        }

        var encoded = SidecarTomlEncoder.EncodeFields(fields);
        if (encoded.Length > 0
            && encoded[encoded.Length - 1] != '\n')
        {
            encoded += "\n";
        }
        return encoded;
    }

    private static Result<IReadOnlyDictionary<string, object>> ParseFieldsToml(string tomlText)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(tomlText))
            {
                return Result<IReadOnlyDictionary<string, object>>.Ok(
                    new Dictionary<string, object>());
            }

            var parseResult = SyntaxParser.Parse(tomlText);
            if (parseResult.HasErrors)
            {
                var diagnostics = string.Join("; ", parseResult.Diagnostics.Select(d => d.ToString()));
                return Result<IReadOnlyDictionary<string, object>>.Fail($"TOML parse error(s): {diagnostics}");
            }

            var table = TomlSerializer.Deserialize<TomlTable>(tomlText);
            if (table is null)
            {
                return Result<IReadOnlyDictionary<string, object>>.Ok(
                    new Dictionary<string, object>());
            }

            var dictionary = new Dictionary<string, object>();
            foreach (var (key, value) in table)
            {
                dictionary[key] = value!;
            }

            return Result<IReadOnlyDictionary<string, object>>.Ok(dictionary);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyDictionary<string, object>>.Fail("An exception occurred when parsing TOML.")
                .WithException(ex);
        }
    }
}
