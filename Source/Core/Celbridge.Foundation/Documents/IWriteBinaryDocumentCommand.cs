using Celbridge.Commands;

namespace Celbridge.Documents;

/// <summary>
/// Replace the content of a binary document from base64 data. The decoded
/// bytes are written directly to disk. Any open document reloads its buffer
/// from disk after the write.
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
}
