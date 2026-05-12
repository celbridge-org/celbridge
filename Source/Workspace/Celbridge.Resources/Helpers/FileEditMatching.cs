using System.Text;

namespace Celbridge.Resources.Helpers;

internal record FileEditApplyResult(string NewContent, List<int> ReplacementStarts);

internal record CappedRanges(IReadOnlyList<FileEditAffectedRange> Ranges, bool Truncated);

/// <summary>
/// Shared text-match helpers for EditFileCommand and MultiEditFileCommand.
/// Both work on a single string buffer with byte-equal matching after line
/// ending normalisation has been applied by the caller.
/// </summary>
internal static class FileEditMatching
{
    /// <summary>
    /// Match counts at or below this threshold return the full affected-range
    /// list. Above it, the caller caps to a first + last sample.
    /// </summary>
    public const int VerboseRangeThreshold = 5;

    /// <summary>
    /// Number of ranges to retain from the start of the list when capping.
    /// </summary>
    public const int VerboseRangeFirstSampleSize = 3;

    /// <summary>
    /// Number of ranges to retain from the end of the list when capping.
    /// </summary>
    public const int VerboseRangeLastSampleSize = 1;

    /// <summary>
    /// Collapses ranges that share the same (FromLine, ToLine) into a single
    /// entry whose MatchCount sums the per-match counts. A two-hit line under
    /// replaceAll becomes one entry with MatchCount=2 rather than two duplicate
    /// entries. Returns a new list sorted ascending by FromLine. The input is
    /// not mutated. Sorting is done internally because the merge needs
    /// (FromLine, ToLine) duplicates to be adjacent and tying that precondition
    /// to the caller is a silent footgun.
    /// </summary>
    public static List<FileEditAffectedRange> MergeSameLineRanges(IReadOnlyList<FileEditAffectedRange> ranges)
    {
        if (ranges.Count == 0)
        {
            return new List<FileEditAffectedRange>(0);
        }

        var sortedRanges = new List<FileEditAffectedRange>(ranges);
        sortedRanges.Sort((a, b) => a.FromLine.CompareTo(b.FromLine));

        var merged = new List<FileEditAffectedRange>(sortedRanges.Count);
        var current = sortedRanges[0];
        var currentCount = current.MatchCount;

        for (var i = 1; i < sortedRanges.Count; i++)
        {
            var next = sortedRanges[i];
            if (next.FromLine == current.FromLine && next.ToLine == current.ToLine)
            {
                currentCount += next.MatchCount;
            }
            else
            {
                merged.Add(current with { MatchCount = currentCount });
                current = next;
                currentCount = next.MatchCount;
            }
        }
        merged.Add(current with { MatchCount = currentCount });

        return merged;
    }

    /// <summary>
    /// Caps a sorted affected-range list to a first + last sample when its
    /// length exceeds VerboseRangeThreshold. The total match count stays
    /// accurate via the caller's MatchCount field. The cap only affects the
    /// returned range list size. Returns the original list unchanged when at
    /// or below the threshold.
    /// </summary>
    public static CappedRanges CapVerboseRanges(IReadOnlyList<FileEditAffectedRange> sortedRanges)
    {
        if (sortedRanges.Count <= VerboseRangeThreshold)
        {
            return new CappedRanges(sortedRanges, false);
        }

        var sampled = new List<FileEditAffectedRange>(VerboseRangeFirstSampleSize + VerboseRangeLastSampleSize);
        for (var i = 0; i < VerboseRangeFirstSampleSize; i++)
        {
            sampled.Add(sortedRanges[i]);
        }
        sampled.Add(sortedRanges[^1]);

        return new CappedRanges(sampled, true);
    }

    /// <summary>
    /// Returns every match position of oldString in content, scanning forward
    /// without overlap. Caller has already converted oldString line endings to
    /// the file's separator.
    /// </summary>
    public static List<int> FindAllMatches(string content, string oldString)
    {
        var positions = new List<int>();
        var searchIndex = 0;
        while (searchIndex <= content.Length)
        {
            var matchIndex = content.IndexOf(oldString, searchIndex, StringComparison.Ordinal);
            if (matchIndex < 0)
            {
                break;
            }
            positions.Add(matchIndex);
            searchIndex = matchIndex + oldString.Length;
        }
        return positions;
    }

    /// <summary>
    /// Builds a new buffer by replacing each match at the given positions with
    /// newString. Records the start index of each replacement in the new buffer
    /// so callers can compute post-edit line ranges without rescanning.
    /// </summary>
    public static FileEditApplyResult ApplyMatches(
        string content,
        List<int> matchPositions,
        string oldString,
        string newString)
    {
        var sb = new StringBuilder();
        var replacementStarts = new List<int>(matchPositions.Count);
        var lastEnd = 0;

        foreach (var pos in matchPositions)
        {
            sb.Append(content, lastEnd, pos - lastEnd);
            replacementStarts.Add(sb.Length);
            sb.Append(newString);
            lastEnd = pos + oldString.Length;
        }
        sb.Append(content, lastEnd, content.Length - lastEnd);

        return new FileEditApplyResult(sb.ToString(), replacementStarts);
    }

    /// <summary>
    /// Computes the 1-based inclusive line range that a replacement occupies in
    /// the final buffer. For a non-empty newString, the range covers every line
    /// any character of newString lands on. For an empty newString (deletion),
    /// the range is a single line where the deletion landed — clamped to the
    /// final content line if the deletion was at end-of-file.
    /// </summary>
    public static FileEditAffectedRange RangeForReplacement(string newContent, int start, string newString)
    {
        var fromLine = LineNumberAt(newContent, start);

        if (newString.Length == 0)
        {
            var totalLines = LineEndingHelper.CountLines(newContent);
            if (totalLines == 0)
            {
                return new FileEditAffectedRange(1, 1);
            }
            if (fromLine > totalLines)
            {
                return new FileEditAffectedRange(totalLines, totalLines);
            }
            return new FileEditAffectedRange(fromLine, fromLine);
        }

        var newlinesInNewString = 0;
        for (var i = 0; i < newString.Length; i++)
        {
            if (newString[i] == '\n')
            {
                newlinesInNewString++;
            }
        }

        // A trailing newline terminates the last content line. It does not
        // open a new line whose content belongs to this replacement.
        if (newString[^1] == '\n')
        {
            newlinesInNewString--;
        }

        var toLine = fromLine + newlinesInNewString;

        return new FileEditAffectedRange(fromLine, toLine);
    }

    /// <summary>
    /// Counts the lone-LF terminator inside both LF and CRLF line breaks before
    /// the given index. Returns the 1-based line number that contains the
    /// character at that index.
    /// </summary>
    public static int LineNumberAt(string content, int index)
    {
        var newlineCount = 0;
        var limit = Math.Min(index, content.Length);
        for (var i = 0; i < limit; i++)
        {
            if (content[i] == '\n')
            {
                newlineCount++;
            }
        }
        return newlineCount + 1;
    }

    /// <summary>
    /// Escapes a snippet for inclusion in an error message: control characters
    /// become C-style escapes so the agent sees what the file actually
    /// contains, and the output is truncated with an ellipsis at maxChars.
    /// </summary>
    public static string TruncateForQuote(string text, int maxChars)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }

        var escaped = sb.ToString();
        if (escaped.Length <= maxChars)
        {
            return escaped;
        }
        return string.Concat(escaped.AsSpan(0, Math.Max(0, maxChars - 3)), "...");
    }
}
