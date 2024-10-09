using Celbridge.Commands;
using Celbridge.Foundation;

namespace Celbridge.Documents;

/// <summary>
/// Close an opened document.
/// </summary>
public interface ICloseDocumentCommand : IExecutableCommand
{
    /// <summary>
    /// The resource key of the opened document to close.
    /// </summary>
    ResourceKey FileResource { get; set; }

    /// <summary>
    /// Forces the document to close without allowing the document to cancel the close operation.
    /// </summary>
    bool ForceClose { get; set; }
}
