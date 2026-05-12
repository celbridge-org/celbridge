using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// A single text-match edit within a multi-edit batch. Each edit has the same
/// shape as IFileEditCommand's parameters.
/// </summary>
public record FileEditOperation(string OldString, string NewString, bool ReplaceAll = false);

/// <summary>
/// The post-edit line range occupied by one or more matches from a single edit
/// within a multi-edit batch. EditIndex identifies which edit in the input batch
/// produced this range. MatchCount is the number of individual matches from that
/// edit collapsed into this range. Same-(FromLine, ToLine) hits within the
/// edit's replaceAll pass merge into one entry with MatchCount summing the
/// per-match total. Entries from different edits are never merged across edits,
/// so the agent can group ranges back to their originating edit.
/// </summary>
public record FileMultiEditAffectedRange(int EditIndex, int FromLine, int ToLine, int MatchCount = 1);

/// <summary>
/// Per-edit summary for a multi-edit batch. MatchCount is the total number of
/// matches the edit found at its turn in the sequence (before any later edit
/// could overwrite that region). Truncated is true when the edit's contribution
/// to AffectedRanges was capped to a sample because it exceeded the verbose
/// threshold.
/// </summary>
public record FileMultiEditEditSummary(int MatchCount, bool Truncated);

/// <summary>
/// Result returned by IFileMultiEditCommand. AppliedCount is the number of
/// edits in the batch (all of them, since the batch is atomic). Edits carries
/// per-edit MatchCount and Truncated, indexed by edit order in the input.
/// AffectedRanges locates each match in the post-batch file, tagged with its
/// EditIndex and sorted by FromLine ascending.
/// </summary>
public record FileMultiEditResult(
    int AppliedCount,
    IReadOnlyList<FileMultiEditEditSummary> Edits,
    IReadOnlyList<FileMultiEditAffectedRange> AffectedRanges);

/// <summary>
/// Applies an atomic batch of text-match edits to a single file. Edits apply
/// sequentially against an in-memory buffer in array order. Later edits anchor
/// against the post-previous-edit state. If any edit fails its match or
/// uniqueness check, the entire batch fails and nothing is written. Writes
/// directly to disk. Any open document reloads its buffer from disk after the
/// write.
/// </summary>
public interface IFileMultiEditCommand : IExecutableCommand<FileMultiEditResult>
{
    /// <summary>
    /// The resource key of the file to edit.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// The ordered list of edits to apply. Each edit is matched against the
    /// in-memory buffer state produced by the previous edits in the list.
    /// </summary>
    List<FileEditOperation> Edits { get; set; }
}
