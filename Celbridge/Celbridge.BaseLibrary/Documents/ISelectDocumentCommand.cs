using Celbridge.Commands;
using Celbridge.Explorer;

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
