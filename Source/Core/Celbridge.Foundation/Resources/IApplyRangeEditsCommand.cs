using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// A single coordinate-based edit to apply to a file. Uses 1-based line and
/// column numbers (Monaco editor convention) rather than text-match anchoring,
/// to distinguish it from the snippet-match edits used by the MCP file tools.
///
/// Edit operations:
/// - Replace: Specify a range spanning existing text. The range is replaced with NewText.
/// - Insert: Use a zero-width range (Line == EndLine and Column == EndColumn). NewText is inserted at that position.
/// - Delete: Specify a range spanning text to delete. Set NewText to empty string.
/// - Append to line: Set Column and EndColumn to one past the last character of the line.
/// - Replace to end of line: Set EndColumn to -1 as a sentinel meaning "end of the line".
///   This eliminates the need to know the exact character count of the line being edited.
/// </summary>
public record RangeEdit(
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string NewText)
{
    /// <summary>
    /// Creates an insert edit that inserts text at the specified position.
    /// </summary>
    public static RangeEdit Insert(int line, int column, string text)
        => new(line, column, line, column, text);

    /// <summary>
    /// Creates a delete edit that removes text in the specified range.
    /// </summary>
    public static RangeEdit Delete(int line, int column, int endLine, int endColumn)
        => new(line, column, endLine, endColumn, string.Empty);

    /// <summary>
    /// Creates a replace edit that replaces text in the specified range with new text.
    /// </summary>
    public static RangeEdit Replace(int line, int column, int endLine, int endColumn, string newText)
        => new(line, column, endLine, endColumn, newText);
}

/// <summary>
/// A batch of coordinate-based edits to apply to a single file. Distinct from
/// FileEditOperation, which is the snippet-match edit shape used by the MCP
/// file_edit and file_multi_edit tools.
/// </summary>
public record FileRangeEdit(
    ResourceKey Resource,
    List<RangeEdit> Edits);

/// <summary>
/// Applies coordinate-based batch edits across one or more files by writing
/// directly to disk. Any open document reloads its buffer from disk after the
/// write completes. The snippet-match counterparts for single-file edits are
/// IFileEditCommand and IFileMultiEditCommand.
/// </summary>
public interface IApplyRangeEditsCommand : IExecutableCommand
{
    /// <summary>
    /// The list of file edits to apply. Each entry pairs a resource key with
    /// a coordinate-based edit batch.
    /// </summary>
    List<FileRangeEdit> Edits { get; set; }
}
