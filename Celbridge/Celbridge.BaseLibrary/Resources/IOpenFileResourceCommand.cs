using Celbridge.Commands;

namespace Celbridge.Resources;

public interface IOpenFileResourceCommand : IExecutableCommand
{
    /// <summary>
    /// The resource key of the file to open.
    /// </summary>
    ResourceKey FileResource { get; set; }
}
