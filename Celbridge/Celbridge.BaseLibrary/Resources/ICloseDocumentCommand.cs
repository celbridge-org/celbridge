using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Close an opened document.
/// </summary>
public interface ICloseDocumentCommand : IExecutableCommand
{
    /// <summary>
    /// The resource key of the opened document to close.
    /// </summary>
    ResourceKey FileResource { get; set; }
}
