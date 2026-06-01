using Celbridge.Core;

namespace Celbridge.Resources.Services;

/// <summary>
/// Result of parsing one reference literal: its position in the source text
/// (half-open range) and the resource key it encodes.
/// </summary>
public sealed partial record ParsedReference(int StartIndex, int EndIndex, ResourceKey Key);

/// <summary>
/// Parser for "project:" reference literals. The shared definition of what
/// counts as a tracked reference; detection and rewrite paths both go through
/// here.
/// </summary>
public static class ResourceReferenceParser
{
    /// <summary>
    /// The literal that marks the start of a reference.
    /// </summary>
    public const string ReferenceMarker = "project:";

    // References must always be quoted; a bare "project:" marker is not a
    // tracked reference even if the key syntax parses cleanly.
    private static readonly char[] SingleCharOpeners = { '"', '\'' };

    // Escaped-quote openers (\" and \'). Take precedence over single-char
    // openers, so `\"project:..\"` parses as the escaped-quote case rather
    // than as a plain quote preceded by a backslash.
    private static readonly (char First, char Second)[] EscapedQuoteOpeners =
    {
        ('\\', '"'),
        ('\\', '\''),
    };

    /// <summary>
    /// Attempts to parse a single reference at the given marker position.
    /// <paramref name="markerIndex"/> must point at the 'p' of a "project:"
    /// literal in the text; returns null if the surrounding quoted region is
    /// malformed or the key does not parse.
    /// </summary>
    public static ParsedReference? TryParseReferenceAt(string text, int markerIndex)
    {
        int keyStart = markerIndex + ReferenceMarker.Length;
        int keyEnd = -1;

        // Two-char escaped quote takes precedence over single-char quote.
        if (markerIndex >= 2)
        {
            char prevPrev = text[markerIndex - 2];
            char prev = text[markerIndex - 1];
            foreach (var opener in EscapedQuoteOpeners)
            {
                if (prevPrev == opener.First
                    && prev == opener.Second)
                {
                    keyEnd = ScanForTwoCharCloser(text, keyStart, opener.First, opener.Second);
                    break;
                }
            }
        }

        if (keyEnd < 0
            && markerIndex >= 1
            && Array.IndexOf(SingleCharOpeners, text[markerIndex - 1]) >= 0)
        {
            char closer = text[markerIndex - 1];
            keyEnd = ScanForSingleCharCloser(text, keyStart, closer);
        }

        // No bare fallback: an unquoted marker is not a tracked reference.
        if (keyEnd < 0
            || keyEnd <= keyStart)
        {
            return null;
        }

        var candidate = text.Substring(markerIndex, keyEnd - markerIndex);
        if (!ResourceKey.TryCreate(candidate, out var key))
        {
            return null;
        }

        return new ParsedReference(markerIndex, keyEnd, key);
    }

    // Returns the closer's index (end-exclusive) or -1 on newline, control
    // char, or end-of-text.
    private static int ScanForSingleCharCloser(string text, int start, char closer)
    {
        int cursor = start;
        while (cursor < text.Length)
        {
            char current = text[cursor];
            if (current == '\r'
                || current == '\n'
                || char.IsControl(current))
            {
                return -1;
            }
            if (current == closer)
            {
                return cursor;
            }
            cursor++;
        }
        return -1;
    }

    // Returns the index of the closer's first character (end-exclusive) or
    // -1 on newline, control char, or end-of-text.
    private static int ScanForTwoCharCloser(string text, int start, char first, char second)
    {
        int cursor = start;
        while (cursor < text.Length)
        {
            char current = text[cursor];
            if (current == '\r'
                || current == '\n'
                || char.IsControl(current))
            {
                return -1;
            }
            if (current == first
                && cursor + 1 < text.Length
                && text[cursor + 1] == second)
            {
                return cursor;
            }
            cursor++;
        }
        return -1;
    }
}
