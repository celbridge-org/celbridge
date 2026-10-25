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
    /// The search pattern: a regex in ripgrep's dialect, or plain text when Literal is true.
    /// </summary>
    string Pattern { get; set; }

    /// <summary>
    /// The replacement. For a regex it follows ripgrep's --replace, so '$1' and '$name' write a group. For plain
    /// text it is plain text.
    /// </summary>
    string Replacement { get; set; }

    /// <summary>
    /// If true, the pattern is plain text.
    /// </summary>
    bool Literal { get; set; }

    /// <summary>
    /// If true, the search matches letter case exactly.
    /// </summary>
    bool MatchCase { get; set; }

    /// <summary>
    /// If true, a match counts only when no word character touches it on either side.
    /// </summary>
    bool WholeWord { get; set; }

    /// <summary>
    /// If true, the pattern can match across lines. Otherwise it matches within one line.
    /// </summary>
    bool Multiline { get; set; }

    /// <summary>
    /// First line number (1-based, inclusive) to include in the replacement scope.
    /// Zero means no lower bound, so the scope starts at the first line.
    /// </summary>
    int FromLine { get; set; }

    /// <summary>
    /// Last line number (1-based, inclusive) to include in the replacement scope.
    /// Zero means no upper bound, so the scope runs to the end of the file.
    /// </summary>
    int ToLine { get; set; }
}
