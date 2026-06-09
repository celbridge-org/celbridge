using Celbridge.Logging;
using Tomlyn;
using Tomlyn.Model;

namespace Celbridge.Resources.Helpers;

/// <summary>
/// Parse, compose, and on-disk inspection for the .cel sidecar format. A
/// sidecar file is plain TOML; the helpers here are a thin wrapper around
/// Tomlyn for read and around SidecarTomlEncoder for deterministic write.
/// </summary>
public static class SidecarHelper
{
    /// <summary>
    /// The file extension used for sidecar files.
    /// </summary>
    public const string Extension = ".cel";

    /// <summary>
    /// The on-disk field name carrying the tag list. Reserved root-level
    /// field; the agent-facing tools surface its values under the domain key
    /// "tags".
    /// </summary>
    public const string TagsFieldName = "_tags";

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

    /// <summary>
    /// Reads a sidecar file at absolutePath and classifies it as Healthy
    /// (parses cleanly) or Broken (any parse or read failure). The bytes on
    /// disk are never modified.
    /// </summary>
    public static CelParseStatus Inspect(string absolutePath, ILogger logger)
    {
        // Inspect runs synchronously inside ResourceClassifier during
        // UpdateResourceRegistry. In production the call routes through the
        // gateway; the direct-read fallback is for the test paths that
        // construct a registry without standing up the DI host.
        var text = ReadSidecarText(absolutePath);
        if (text is null)
        {
            logger.LogWarning($"sidecar pairing: failed to read '{absolutePath}'");
            return CelParseStatus.Broken;
        }

        var parseResult = Parse(text);
        if (parseResult.IsFailure)
        {
            logger.LogWarning($"sidecar pairing: '{absolutePath}' has unparseable content");
            return CelParseStatus.Broken;
        }

        return CelParseStatus.Healthy;
    }

    [AllowDirectFileSystemAccess]
    private static string? ReadSidecarText(string absolutePath)
    {
        if (ServiceLocator.ServiceProvider is not null)
        {
            var fileSystem = ServiceLocator.AcquireService<ILocalFileSystem>();
            var readResult = SyncRunner.Run(() => fileSystem.ReadAllTextAsync(absolutePath));
            return readResult.IsSuccess ? readResult.Value : null;
        }

        try
        {
            return File.ReadAllText(absolutePath);
        }
        catch
        {
            return null;
        }
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

            var parseResult = Toml.Parse(tomlText);
            if (parseResult.HasErrors)
            {
                var diagnostics = string.Join("; ", parseResult.Diagnostics.Select(d => d.ToString()));
                return Result<IReadOnlyDictionary<string, object>>.Fail($"TOML parse error(s): {diagnostics}");
            }

            var table = (TomlTable)parseResult.ToModel();
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
