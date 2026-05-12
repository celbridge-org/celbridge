using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// The post-edit line range occupied by one or more text-match edits, in 1-based
/// inclusive line numbers locating the replacement within the final on-disk
/// file. MatchCount is the number of individual matches collapsed into this
/// range. Same-(FromLine, ToLine) hits from a single replaceAll pass merge into
/// one entry with MatchCount summing the per-match total.
/// </summary>
public record FileEditAffectedRange(int FromLine, int ToLine, int MatchCount = 1);

/// <summary>
/// Result returned by IEditFileCommand. MatchCount is the total number of
/// occurrences of OldString that were replaced. AffectedRanges locates each
/// replacement in the post-edit file, sorted by FromLine ascending. When the
/// match count exceeds the verbose threshold the list is capped to a first +
/// last sample and Truncated is set to true; MatchCount still reflects the
/// real total.
/// </summary>
public record EditFileResult(
    int MatchCount,
    IReadOnlyList<FileEditAffectedRange> AffectedRanges,
    bool Truncated = false);

/// <summary>
/// Replaces an exact text snippet inside a single file. The snippet must match
/// uniquely unless ReplaceAll is set. Writes directly to disk. Any open document
/// reloads its buffer from disk after the write.
/// </summary>
public interface IEditFileCommand : IExecutableCommand<EditFileResult>
{
    /// <summary>
    /// The resource key of the file to edit.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// The exact text to match. Must be present in the file. Must be unique
    /// unless ReplaceAll is true.
    /// </summary>
    string OldString { get; set; }

    /// <summary>
    /// The replacement text. May be empty to delete the matched text. Include
    /// the trailing line terminator in OldString when deleting a whole line to
    /// avoid leaving a residual blank line behind.
    /// </summary>
    string NewString { get; set; }

    /// <summary>
    /// When true, every occurrence of OldString is replaced. When false (the
    /// default), the command fails if OldString matches more than once.
    /// </summary>
    bool ReplaceAll { get; set; }
}
