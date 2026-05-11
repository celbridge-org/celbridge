using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// A single text-match edit within a multi-edit batch. Each edit has the same
/// shape as IFileEditCommand's parameters.
/// </summary>
public record FileEditOperation(string OldString, string NewString, bool ReplaceAll = false);

/// <summary>
/// Result returned by IFileMultiEditCommand. AppliedCount is the number of
/// edits in the batch (all of them, since the batch is atomic). AffectedRanges
/// locates each edit's replacement in the post-edit file, sorted by FromLine
/// ascending.
/// </summary>
public record FileMultiEditResult(int AppliedCount, IReadOnlyList<FileEditAffectedRange> AffectedRanges);

/// <summary>
/// Applies an atomic batch of text-match edits to a single file. Edits apply
/// sequentially against an in-memory buffer in array order; later edits anchor
/// against the post-previous-edit state. If any edit fails its match or
/// uniqueness check, the entire batch fails and nothing is written. Writes
/// directly to disk; any open document reloads its buffer from disk after the
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
