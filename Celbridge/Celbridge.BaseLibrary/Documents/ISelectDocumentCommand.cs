using Celbridge.Commands;
using Celbridge.Resources;

namespace Celbridge.Documents;

/// <summary>
/// Select an opened document in the documents panel.
/// </summary>
public interface ISelectDocumentCommand : IExecutableCommand
{
    /// <summary>
    /// The resource key of the document to select.
    /// </summary>
    ResourceKey FileResource { get; set; }
}
