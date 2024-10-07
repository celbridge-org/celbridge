using Celbridge.Commands;

namespace Celbridge.Explorer;

/// <summary>
/// Open a resource in the system file explorer.
/// </summary>
public interface IOpenExplorerCommand : IExecutableCommand
{
    /// <summary>
    /// The resource to open in the system file explorer.
    /// </summary>
    ResourceKey Resource { get; set; }
}
