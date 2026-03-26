using Celbridge.Commands;

namespace Celbridge.Documents;

/// <summary>
/// A single text edit to apply to a document.
/// Uses 1-based line and column numbers (Monaco editor convention).
/// 
/// Edit operations:
/// - Replace: Specify a range spanning existing text. The range is replaced with NewText.
/// - Insert: Use a zero-width range (Line == EndLine and Column == EndColumn). NewText is inserted at that position.
/// - Delete: Specify a range spanning text to delete. Set NewText to empty string.
/// - Append to line: Set Column and EndColumn to one past the last character of the line.
/// </summary>
public record TextEdit(
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string NewText)
{
    /// <summary>
    /// Creates an insert edit that inserts text at the specified position.
    /// </summary>
    public static TextEdit Insert(int line, int column, string text)
        => new(line, column, line, column, text);

    /// <summary>
    /// Creates a delete edit that removes text in the specified range.
    /// </summary>
    public static TextEdit Delete(int line, int column, int endLine, int endColumn)
        => new(line, column, endLine, endColumn, string.Empty);

    /// <summary>
    /// Creates a replace edit that replaces text in the specified range with new text.
    /// </summary>
    public static TextEdit Replace(int line, int column, int endLine, int endColumn, string newText)
        => new(line, column, endLine, endColumn, newText);
}

/// <summary>
/// A batch of text edits to apply to a single document.
/// </summary>
public record DocumentEdit(
    ResourceKey Resource,
    List<TextEdit> Edits);

/// <summary>
/// Applies batch text edits to documents via Monaco editor.
/// Opens documents if needed, applies edits as a single undo unit per document.
/// This enables undo support for replace operations.
/// </summary>
public interface IApplyEditsCommand : IExecutableCommand
{
    /// <summary>
    /// The list of document edits to apply.
    /// Each DocumentEdit contains a resource key and a list of text edits.
    /// </summary>
    List<DocumentEdit> Edits { get; set; }

    /// <summary>
    /// When true (default), opens the document in the editor and applies edits with undo support.
    /// When false and the document is not already open, applies edits directly to the file on disk.
    /// When false but the document is already open, routes through the editor to avoid auto-save race conditions.
    /// </summary>
    bool OpenDocument { get; set; }
}
