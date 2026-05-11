using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// The post-edit line range occupied by a text-match edit, in 1-based inclusive
/// line numbers locating the replacement within the final on-disk file.
/// </summary>
public record FileEditAffectedRange(int FromLine, int ToLine);

/// <summary>
/// Result returned by IFileEditCommand. MatchCount is the number of occurrences
/// of OldString that were replaced. AffectedRanges locates each replacement in
/// the post-edit file, sorted by FromLine ascending.
/// </summary>
public record FileEditResult(int MatchCount, IReadOnlyList<FileEditAffectedRange> AffectedRanges);

/// <summary>
/// Replaces an exact text snippet inside a single file. The snippet must match
/// uniquely unless ReplaceAll is set. Writes directly to disk; any open document
/// reloads its buffer from disk after the write.
/// </summary>
public interface IFileEditCommand : IExecutableCommand<FileEditResult>
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
