using Celbridge.Commands;
using Celbridge.Foundation;

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
}
