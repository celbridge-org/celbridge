using System.Text;
using System.Text.RegularExpressions;
using Celbridge.Logging;
using Tomlyn;
using Tomlyn.Model;

namespace Celbridge.Resources.Helpers;

/// <summary>
/// Parse, compose, and on-disk inspection for the .cel sidecar format: TOML
/// frontmatter at the top, optionally followed by zero-or-more named content
/// blocks delimited by lines of the form '+++ "block-name"'. Format constants
/// and pure utility helpers used by the rest of the resources subsystem live
/// here; the workspace-scoped ISidecarService exposes the surface that crosses
/// project boundaries.
/// </summary>
public static class SidecarHelper
{
    /// <summary>
    /// The file extension used for sidecar files.
    /// </summary>
    public const string Extension = ".cel";

    /// <summary>
    /// The standardised list-of-string frontmatter field that the data tools
    /// surface as tags.
    /// </summary>
    public const string TagsFieldName = "tags";

    // Fence line: '+++' then one space, then a double-quoted block name, with
    // optional trailing whitespace. The block name is lowercase letters and
    // digits with optional dotted segments separated by '.', and hyphens
    // permitted inside a segment.
    private static readonly Regex FenceLineRegex = new(
        @"^\+\+\+\s+""([a-z][a-z0-9-]*(?:\.[a-z][a-z0-9-]*)*)""\s*$",
        RegexOptions.Compiled);

    // Block name regex: same shape as the fence's capture group, applied to
    // candidate names at write time so a malformed block ID is caught before
    // it lands on disk.
    private static readonly Regex BlockNameRegex = new(
        @"^[a-z][a-z0-9-]*(?:\.[a-z][a-z0-9-]*)*$",
        RegexOptions.Compiled);

    /// <summary>
    /// True when the candidate string matches the block-naming rules
    /// (lowercase letters, digits, hyphens, dotted segments).
    /// </summary>
    public static bool IsValidBlockName(string name)
    {
        return !string.IsNullOrEmpty(name)
            && BlockNameRegex.IsMatch(name);
    }

    /// <summary>
    /// True when the value can be written through the structured frontmatter
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
    /// Extracts a string list from a frontmatter value (e.g. the tags field).
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
    /// Parses sidecar content into its frontmatter dictionary and ordered
    /// block list. Frontmatter is parsed as TOML; block bodies are opaque text.
    /// Fails if the TOML prefix is malformed or any block name appears twice.
    /// </summary>
    public static Result<SidecarContent> Parse(string text)
    {
        if (text is null)
        {
            return Result<SidecarContent>.Fail("Sidecar content is null.");
        }

        // Strip an optional UTF-8 BOM so the first byte is the first content
        // character. Tomlyn is BOM-tolerant; this normalises the block split.
        if (text.Length > 0
            && text[0] == '﻿')
        {
            text = text.Substring(1);
        }

        var lines = SplitLines(text);

        // Walk the lines once and record the index of every fence line. The
        // frontmatter spans lines [0, firstFence); each block spans the lines
        // from (fence + 1) to the next fence (or end of input).
        var fenceIndexes = new List<int>();
        var fenceNames = new List<string>();
        for (int i = 0; i < lines.Count; i++)
        {
            var match = FenceLineRegex.Match(lines[i].Text);
            if (match.Success)
            {
                fenceIndexes.Add(i);
                fenceNames.Add(match.Groups[1].Value);
            }
        }

        var frontmatterEnd = fenceIndexes.Count > 0 ? fenceIndexes[0] : lines.Count;
        var frontmatterText = JoinLines(lines, 0, frontmatterEnd);

        var parseResult = ParseFrontmatterToml(frontmatterText);
        if (parseResult.IsFailure)
        {
            return Result.Fail(parseResult);
        }
        var frontmatter = parseResult.Value;

        var blocks = new List<SidecarBlock>(fenceIndexes.Count);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        for (int b = 0; b < fenceIndexes.Count; b++)
        {
            var name = fenceNames[b];
            if (!seenNames.Add(name))
            {
                return Result<SidecarContent>.Fail($"Sidecar contains duplicate block name '{name}'.");
            }

            var contentStart = fenceIndexes[b] + 1;
            var contentEnd = b + 1 < fenceIndexes.Count ? fenceIndexes[b + 1] : lines.Count;
            var content = JoinLines(lines, contentStart, contentEnd);

            // Block content is line-oriented: the terminator that follows the
            // last content line is a separator (between blocks or to EOF), not
            // part of the block's semantic content. Stripping a single trailing
            // \n or \r\n makes the SidecarBlock.Content value position-
            // independent so its byte count is stable as adjacent blocks are
            // added or removed.
            content = StripTrailingTerminator(content);
            blocks.Add(new SidecarBlock(name, content));
        }

        return Result<SidecarContent>.Ok(new SidecarContent(frontmatter, blocks));
    }

    /// <summary>
    /// Composes a sidecar text from the frontmatter dictionary and named blocks.
    /// The output is the inverse of Parse for any cleanly-parsed input.
    /// </summary>
    public static string Compose(SidecarContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Compose(content.Frontmatter, content.Blocks);
    }

    /// <summary>
    /// Composes a sidecar text from a frontmatter dictionary and an ordered
    /// list of named blocks.
    /// </summary>
    public static string Compose(
        IReadOnlyDictionary<string, object> frontmatter,
        IReadOnlyList<SidecarBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(frontmatter);
        blocks ??= Array.Empty<SidecarBlock>();

        var builder = new StringBuilder();

        if (frontmatter.Count > 0)
        {
            var tomlTable = new TomlTable();
            foreach (var (key, value) in frontmatter)
            {
                tomlTable[key] = ConvertToTomlValue(value);
            }

            var tomlText = Toml.FromModel(tomlTable);
            // Toml.FromModel emits a trailing newline. Trim it so the join with
            // the first fence (if any) is predictable; we add an explicit
            // separator below.
            tomlText = tomlText.TrimEnd('\r', '\n');
            builder.Append(tomlText);
        }

        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (!IsValidBlockName(block.Name))
            {
                throw new ArgumentException($"Block name '{block.Name}' does not match the block-naming rules.");
            }

            // Each fence line starts on its own line. If we already wrote
            // frontmatter or a prior block, ensure a newline before this fence.
            if (builder.Length > 0
                && builder[builder.Length - 1] != '\n')
            {
                builder.Append('\n');
            }

            builder.Append("+++ \"");
            builder.Append(block.Name);
            builder.Append("\"\n");
            builder.Append(block.Content);

            // Ensure each non-empty block contributes a trailing newline so
            // the next fence (or EOF) starts on its own line and so the
            // block's on-disk byte footprint is independent of position.
            // Parse strips this terminator back off, restoring round-trip
            // equivalence between "X" and "X\n" input.
            if (block.Content.Length > 0
                && block.Content[block.Content.Length - 1] != '\n')
            {
                builder.Append('\n');
            }
        }

        // When only frontmatter is present, leave a single trailing newline so
        // the file ends on a newline boundary. When blocks are present, each
        // block now guarantees its own terminator (see the per-block append
        // above), so the file already ends on \n.
        if (blocks.Count == 0
            && builder.Length > 0
            && builder[builder.Length - 1] != '\n')
        {
            builder.Append('\n');
        }

        return builder.ToString();
    }

    // Removes a single trailing line terminator (\r\n or \n) from a block
    // content slice extracted by Parse. The terminator is the separator
    // between blocks (or to EOF), not part of the block's content.
    private static string StripTrailingTerminator(string content)
    {
        if (content.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return content.Substring(0, content.Length - 2);
        }
        if (content.EndsWith("\n", StringComparison.Ordinal))
        {
            return content.Substring(0, content.Length - 1);
        }
        return content;
    }

    /// <summary>
    /// Reads a sidecar file at absolutePath and classifies it as Healthy
    /// (parses cleanly) or Broken (any parse or read failure). The bytes on
    /// disk are never modified.
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
            logger.LogWarning($"sidecar pairing: '{absolutePath}' has unparseable content");
            return SidecarStatus.Broken;
        }

        return SidecarStatus.Healthy;
    }

    // One physical line plus the line terminator that follows it. The
    // terminator is preserved so JoinLines reproduces the original bytes.
    private readonly record struct PhysicalLine(string Text, string Terminator);

    private static List<PhysicalLine> SplitLines(string text)
    {
        var lines = new List<PhysicalLine>();
        int start = 0;
        int i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\r')
            {
                var lineText = text.Substring(start, i - start);
                string terminator;
                if (i + 1 < text.Length
                    && text[i + 1] == '\n')
                {
                    terminator = "\r\n";
                    i += 2;
                }
                else
                {
                    terminator = "\r";
                    i += 1;
                }
                lines.Add(new PhysicalLine(lineText, terminator));
                start = i;
                continue;
            }
            if (c == '\n')
            {
                var lineText = text.Substring(start, i - start);
                lines.Add(new PhysicalLine(lineText, "\n"));
                i += 1;
                start = i;
                continue;
            }
            i++;
        }
        if (start < text.Length)
        {
            lines.Add(new PhysicalLine(text.Substring(start), string.Empty));
        }
        return lines;
    }

    private static string JoinLines(List<PhysicalLine> lines, int startInclusive, int endExclusive)
    {
        if (startInclusive >= endExclusive)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (int i = startInclusive; i < endExclusive; i++)
        {
            builder.Append(lines[i].Text);
            builder.Append(lines[i].Terminator);
        }
        return builder.ToString();
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
