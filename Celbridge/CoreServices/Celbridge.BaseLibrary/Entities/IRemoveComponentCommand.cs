using Celbridge.Commands;

namespace Celbridge.Entities;

/// <summary>
/// Command to remove an entity component associated with a resource.
/// </summary>
public interface IRemoveComponentCommand : IExecutableCommand
{
    /// <summary>
    /// The resource associated with the entity data to be modified.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// The index of the component to remove.
    /// </summary>
    int ComponentIndex { get; set; }
}
