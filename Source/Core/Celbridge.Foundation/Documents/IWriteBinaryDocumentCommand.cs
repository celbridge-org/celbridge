using Celbridge.Commands;

namespace Celbridge.Documents;

/// <summary>
/// Replace the content of a binary document from base64 data.
/// When OpenDocument is true (default) or the document is already open, the content is updated
/// through the document's specialized editor. When OpenDocument is false and the document is
/// not open, the decoded bytes are written directly to disk.
/// </summary>
public interface IWriteBinaryDocumentCommand : IExecutableCommand
{
    /// <summary>
    /// The resource key of the file to write.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// The new content as a base64-encoded string.
    /// </summary>
    string Base64Content { get; set; }

    /// <summary>
    /// When true (default), opens the document in the editor and updates content through the specialized editor.
    /// When false and the document is not already open, decodes and writes bytes directly to disk.
    /// When false but the document is already open, routes through the editor to avoid auto-save race conditions.
    /// </summary>
    bool OpenDocument { get; set; }
}
