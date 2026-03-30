using Celbridge.Commands;

namespace Celbridge.Documents;

/// <summary>
/// Activate an opened document in the documents panel, making it the active tab.
/// </summary>
public interface IActivateDocumentCommand : IExecutableCommand
{
    /// <summary>
    /// The resource key of the document to activate.
    /// </summary>
    ResourceKey FileResource { get; set; }
}
