using Celbridge.Commands;

namespace Celbridge.Documents;

/// <summary>
/// Open a document in the documents panel.
/// </summary>
public interface IOpenDocumentCommand : IExecutableCommand
{
    /// <summary>
    /// The resource key of the file to open.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// Reload the document from the file, if the document is already open.
    /// </summary>
    bool ForceReload { get; set; }
}
