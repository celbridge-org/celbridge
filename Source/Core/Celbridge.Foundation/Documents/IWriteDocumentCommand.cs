using Celbridge.Commands;

namespace Celbridge.Documents;

/// <summary>
/// Writes text content to a document. Creates the file if it does not exist.
/// For existing files, replaces the entire content.
/// When OpenDocument is true (default) or the document is already open, the content is applied
/// through the editor with full undo support. When OpenDocument is false and the document is
/// not open, the content is written directly to disk.
/// </summary>
public interface IWriteDocumentCommand : IExecutableCommand
{
    /// <summary>
    /// The resource key of the file to write.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// The new text content for the document.
    /// </summary>
    string Content { get; set; }

    /// <summary>
    /// When true (default), opens the document in the editor and applies content with undo support.
    /// When false and the document is not already open, writes directly to disk.
    /// When false but the document is already open, routes through the editor to avoid auto-save race conditions.
    /// </summary>
    bool OpenDocument { get; set; }
}
