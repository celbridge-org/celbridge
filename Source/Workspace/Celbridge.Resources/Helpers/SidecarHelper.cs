using Celbridge.Logging;
using Tomlyn;
using Tomlyn.Model;

namespace Celbridge.Resources.Helpers;

/// <summary>
/// The parsed result of a sidecar file: the frontmatter dictionary and the body string.
/// </summary>
public record SidecarParseResult(
    IReadOnlyDictionary<string, object> Frontmatter,
    string Body);

/// <summary>
/// Parse, compose, and on-disk inspection for the TOML-frontmatter-plus-body
/// sidecar format used by .cel files. The frontmatter is fenced by lines
/// containing only +++; the body that follows is opaque text.
/// </summary>
public static class SidecarHelper
{
    /// <summary>
    /// The file extension for sidecar files.
    /// </summary>
    public const string Extension = ".cel";

    /// <summary>
    /// The fence delimiter for the frontmatter section.
    /// </summary>
    public const string Delimiter = "+++";

    /// <summary>
    /// Parses sidecar content into its frontmatter dictionary and body string.
    /// Frontmatter is parsed as TOML; the body is returned verbatim.
    /// Fails if the leading +++ fence is missing or no closing fence is found.
    /// </summary>
    public static Result<SidecarParseResult> Parse(string text)
    {
        if (text is null)
        {
            return Result<SidecarParseResult>.Fail("Sidecar content is null.");
        }

        // Strip an optional UTF-8 BOM so the fence detector sees the +++ on the
        // first byte. Tomlyn is BOM-tolerant; this normalises the body too.
        if (text.Length > 0
            && text[0] == '﻿')
        {
            text = text.Substring(1);
        }

        // Find the opening fence. The fence must be the first non-empty content
        // in the file; leading whitespace before the fence is not permitted.
        var openingFenceLineEnd = FindFenceLineEnd(text, startIndex: 0);
        if (openingFenceLineEnd < 0)
        {
            return Result<SidecarParseResult>.Fail("Sidecar content does not start with a '+++' fence line.");
        }

        // The frontmatter starts immediately after the opening fence's line terminator.
        var frontmatterStart = openingFenceLineEnd;
        var closingFenceStart = FindClosingFence(text, frontmatterStart);
        if (closingFenceStart < 0)
        {
            return Result<SidecarParseResult>.Fail("Sidecar content has no closing '+++' fence.");
        }

        var frontmatterToml = text.Substring(frontmatterStart, closingFenceStart - frontmatterStart);

        // Advance past the closing fence line to find the body start.
        var closingFenceLineEnd = FindFenceLineEnd(text, closingFenceStart);
        if (closingFenceLineEnd < 0)
        {
            // The closing fence is the final line of the file with no trailing
            // line terminator. The body is empty.
            closingFenceLineEnd = text.Length;
        }

        var body = text.Substring(closingFenceLineEnd);

        var parseResult = ParseFrontmatterToml(frontmatterToml);
        if (parseResult.IsFailure)
        {
            return Result.Fail(parseResult);
        }

        return Result<SidecarParseResult>.Ok(new SidecarParseResult(parseResult.Value, body));
    }

    /// <summary>
    /// Composes a sidecar text from a frontmatter dictionary and a body string.
    /// The frontmatter is emitted as TOML between '+++' fence lines; the body
    /// follows the closing fence.
    /// </summary>
    public static string Compose(IReadOnlyDictionary<string, object> frontmatter, string body)
    {
        ArgumentNullException.ThrowIfNull(frontmatter);
        body ??= string.Empty;

        var tomlTable = new TomlTable();
        foreach (var (key, value) in frontmatter)
        {
            tomlTable[key] = ConvertToTomlValue(value);
        }

        var tomlText = Toml.FromModel(tomlTable);

        // Toml.FromModel emits a trailing newline; trim trailing whitespace so
        // the composed output has predictable fence-line spacing.
        tomlText = tomlText.TrimEnd('\r', '\n');

        var separator = "\n";
        var hasBody = body.Length > 0;

        var composed = Delimiter + separator;
        if (tomlText.Length > 0)
        {
            composed += tomlText + separator;
        }
        composed += Delimiter + separator;
        if (hasBody)
        {
            composed += body;
        }

        return composed;
    }

    /// <summary>
    /// Reads a sidecar file at absolutePath and classifies it as Healthy
    /// (frontmatter parses cleanly) or Broken (any parse or read failure).
    /// The bytes on disk are never modified.
    /// </summary>
    public static SidecarStatus Inspect(string absolutePath, ILogger logger)
    {
        string text;
        try
        {
            text = File.ReadAllText(absolutePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, $"sidecar pairing: failed to read '{absolutePath}'");
            return SidecarStatus.Broken;
        }

        var parseResult = Parse(text);
        if (parseResult.IsFailure)
        {
            logger.LogWarning($"sidecar pairing: '{absolutePath}' has unparseable frontmatter");
            return SidecarStatus.Broken;
        }

        return SidecarStatus.Healthy;
    }

    // Returns the position immediately after the line terminator of the fence
    // line that starts at startIndex, or -1 if no fence line is found there.
    // A fence line is "+++" followed by an optional line terminator.
    private static int FindFenceLineEnd(string text, int startIndex)
    {
        if (startIndex < 0
            || startIndex > text.Length)
        {
            return -1;
        }

        if (startIndex + Delimiter.Length > text.Length)
        {
            return -1;
        }

        if (string.CompareOrdinal(text, startIndex, Delimiter, 0, Delimiter.Length) != 0)
        {
            return -1;
        }

        int after = startIndex + Delimiter.Length;

        // Allow trailing whitespace on the fence line up to but not including a
        // line terminator. Anything else after the +++ is not a fence line.
        while (after < text.Length)
        {
            var current = text[after];
            if (current == '\r'
                || current == '\n')
            {
                break;
            }
            if (current == ' '
                || current == '\t')
            {
                after++;
                continue;
            }
            return -1;
        }

        if (after >= text.Length)
        {
            return after;
        }

        if (text[after] == '\r')
        {
            after++;
            if (after < text.Length
                && text[after] == '\n')
            {
                after++;
            }
            return after;
        }

        if (text[after] == '\n')
        {
            after++;
            return after;
        }

        return after;
    }

    // Returns the start position of the next fence line at column 0, or -1 if
    // no closing fence is found.
    private static int FindClosingFence(string text, int searchStart)
    {
        int lineStart = searchStart;
        while (lineStart < text.Length)
        {
            if (FindFenceLineEnd(text, lineStart) >= 0)
            {
                return lineStart;
            }

            // Advance to the next line.
            int newlineIndex = text.IndexOfAny(new[] { '\r', '\n' }, lineStart);
            if (newlineIndex < 0)
            {
                return -1;
            }

            if (text[newlineIndex] == '\r')
            {
                if (newlineIndex + 1 < text.Length
                    && text[newlineIndex + 1] == '\n')
                {
                    lineStart = newlineIndex + 2;
                    continue;
                }
                lineStart = newlineIndex + 1;
                continue;
            }

            lineStart = newlineIndex + 1;
        }

        return -1;
    }

    private static Result<IReadOnlyDictionary<string, object>> ParseFrontmatterToml(string tomlText)
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
            return Result<IReadOnlyDictionary<string, object>>.Fail("An exception occurred when parsing TOML frontmatter.")
                .WithException(ex);
        }
    }

    private static object ConvertToTomlValue(object value)
    {
        switch (value)
        {
            case TomlTable:
            case TomlArray:
                return value;
            case IReadOnlyDictionary<string, object> dictionary:
                var table = new TomlTable();
                foreach (var (key, child) in dictionary)
                {
                    table[key] = ConvertToTomlValue(child);
                }
                return table;
            case System.Collections.IEnumerable enumerable when value is not string:
                var array = new TomlArray();
                foreach (var item in enumerable)
                {
                    array.Add(ConvertToTomlValue(item!));
                }
                return array;
            default:
                return value;
        }
    }
}
