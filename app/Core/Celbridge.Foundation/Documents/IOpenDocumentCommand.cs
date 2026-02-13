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

    /// <summary>
    /// Optional location within the document to navigate to when opening.
    /// </summary>
    string Location { get; set; }

    /// <summary>
    /// Optional target section index (0, 1, or 2) to open the document in.
    /// If null, the document opens in the active section.
    /// </summary>
    int? TargetSectionIndex { get; set; }
}
