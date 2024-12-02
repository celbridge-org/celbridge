using Celbridge.Commands;

namespace Celbridge.Entities;

/// <summary>
/// Command to add a component to the entity associated with a resource.
/// </summary>
public interface IAddComponentCommand : IExecutableCommand
{
    /// <summary>
    /// The resource associated with the entity data to be modified.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// The type of component to add to the entity.
    /// </summary>
    string ComponentType { get; set; }

    /// <summary>
    /// The index to insert the component at.
    /// </summary>
    int ComponentIndex { get; set; }
}
