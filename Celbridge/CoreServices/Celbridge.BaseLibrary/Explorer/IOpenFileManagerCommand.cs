using Celbridge.Commands;

namespace Celbridge.Explorer;

/// <summary>
/// Open a resource in the system file manager.
/// </summary>
public interface IOpenFileManagerCommand : IExecutableCommand
{
    /// <summary>
    /// The resource to open in the system file manager.
    /// </summary>
    ResourceKey Resource { get; set; }
}
