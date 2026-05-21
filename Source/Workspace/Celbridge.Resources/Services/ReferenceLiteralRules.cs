using Celbridge.Core;

namespace Celbridge.Resources.Services;

/// <summary>
/// One reference parse result: the half-open byte range [StartIndex, EndIndex)
/// in the original text that holds the reference literal, plus the validated
/// resource key it encodes.
/// </summary>
public sealed partial record ParsedReference(int StartIndex, int EndIndex, ResourceKey Key);

/// <summary>
/// Shared rules for parsing "project:" reference literals in text. The
/// detection pass in <see cref="ResourceMetaData"/> and the rewrite cascade in
/// <see cref="ResourceFileSystem"/> both consume this module so they cannot
/// drift on what constitutes a valid reference. A symmetry test in
/// Celbridge.Tests asserts that every position the scanner records is a
/// position the rewrite primitive accepts.
/// </summary>
public static class ReferenceLiteralRules
{
    /// <summary>
    /// The literal that marks the start of a reference. Always the bytes of the
    /// default-root prefix; non-project: roots are not tracked.
    /// </summary>
    public const string ReferenceMarker = "project:";

    // Single-character openers that enter delimited-scan mode. Per the agreed
    // design (Option C in the resources redesign), references must always be
    // wrapped in ASCII double or single quotes — there is no bare-prose form
    // of a reference. A "project:" marker not preceded by an opener is not a
    // tracked reference, even if it parses as a valid ResourceKey.
    private static readonly char[] SingleCharOpeners = { '"', '\'' };

    // Two-character openers — the escaped-quote forms used by JSON, TOML basic
    // strings, and every C-family string literal. The closer is the same
    // two-char sequence. These take precedence over the single-char openers
    // (checked first), so a "project:" preceded by \" is treated as the
    // escaped-quote case, not the plain-quote case.
    private static readonly (char First, char Second)[] EscapedQuoteOpeners =
    {
        ('\\', '"'),
        ('\\', '\''),
    };

    /// <summary>
    /// Returns true if the character can legitimately sit immediately before
    /// or after a tracked reference literal. Only the characters that wrap a
    /// quoted or escaped-quoted reference qualify:
    ///   '"' / '\''  — the single-char openers and closers.
    ///   '\\'        — the first char of a \" or \' escape closer.
    /// Other characters (whitespace, brackets, parens, etc.) are NOT boundaries,
    /// because references must always be quoted — anything not adjacent to a
    /// quote is not a reference by definition.
    /// </summary>
    public static bool IsNonKeyBoundary(char c)
    {
        switch (c)
        {
            case '"':
            case '\'':
            case '\\':
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to parse a single reference at the given marker position. The
    /// marker index must point at the 'p' of a "project:" literal in the text.
    /// Returns null if no valid ResourceKey can be extracted (invalid key
    /// syntax, unterminated delimited region, empty key, etc.).
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

        // No bare fallback: a marker without a preceding opener is not a
        // tracked reference. References must always be quoted.
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

    // Walks until the matching closing delimiter and returns its index (the
    // end-exclusive boundary of the key). Returns -1 if the region is
    // unterminated — newline, control char, or end-of-text reached first.
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

    // Walks until the two-char closing sequence and returns the index of its
    // first character (the end-exclusive boundary of the key). Returns -1 if
    // the region is unterminated. Used for the escaped-quote case where a
    // literal \" or \' both opens and closes the delimited region.
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
