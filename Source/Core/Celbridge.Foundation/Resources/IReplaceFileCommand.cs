using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Result returned by IReplaceFileCommand. ReplacementCount is the total
/// number of matches replaced. AffectedRanges locates each replacement in the
/// post-edit file, sorted by FromLine ascending. When ReplacementCount exceeds
/// the verbose threshold AffectedRanges is capped to a first + last sample and
/// Truncated is set to true; ReplacementCount still reflects the real total.
/// </summary>
public record ReplaceFileResult(
    int ReplacementCount,
    IReadOnlyList<FileEditAffectedRange> AffectedRanges,
    bool Truncated = false);

/// <summary>
/// Find and replace text within a file. Replacements are written directly
/// to disk. Any open document reloads its buffer from disk after the write.
/// </summary>
public interface IReplaceFileCommand : IExecutableCommand<ReplaceFileResult>
{
    /// <summary>
    /// The resource key of the file to perform find and replace on.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// The text to search for.
    /// </summary>
    string SearchText { get; set; }

    /// <summary>
    /// The replacement text.
    /// </summary>
    string ReplaceText { get; set; }

    /// <summary>
    /// If true, the search is case-sensitive.
    /// </summary>
    bool MatchCase { get; set; }

    /// <summary>
    /// If true, the search matches whole words only — the match must start and
    /// end on a word boundary. Ignored when UseRegex is true; regex users add
    /// their own \b anchors. Has no effect when UseRegex is false and the
    /// search text already starts and ends on non-word characters.
    /// </summary>
    bool MatchWord { get; set; }

    /// <summary>
    /// If true, the search text is treated as a regular expression.
    /// </summary>
    bool UseRegex { get; set; }

    /// <summary>
    /// First line number (1-based, inclusive) to include in the replacement scope.
    /// Zero means no lower bound — all lines from the start of the file are included.
    /// </summary>
    int FromLine { get; set; }

    /// <summary>
    /// Last line number (1-based, inclusive) to include in the replacement scope.
    /// Zero means no upper bound — all lines to the end of the file are included.
    /// </summary>
    int ToLine { get; set; }
}
