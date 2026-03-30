using Celbridge.Commands;

namespace Celbridge.Documents;

/// <summary>
/// Deletes complete lines from a document, removing them entirely including their line terminators.
/// Uses 1-based line numbers. Both StartLine and EndLine are inclusive.
/// </summary>
public interface IDeleteLinesCommand : IExecutableCommand
{
    /// <summary>
    /// The resource key of the document to delete lines from.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// First line to delete (1-based, inclusive).
    /// </summary>
    int StartLine { get; set; }

    /// <summary>
    /// Last line to delete (1-based, inclusive).
    /// </summary>
    int EndLine { get; set; }

    /// <summary>
    /// When true (default), opens the document in the editor and applies the deletion with undo support.
    /// When false and the document is not already open, deletes lines directly from the file on disk.
    /// When false but the document is already open, routes through the editor to avoid auto-save race conditions.
    /// </summary>
    bool OpenDocument { get; set; }
}
