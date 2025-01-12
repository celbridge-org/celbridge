using Celbridge.Commands;

namespace Celbridge.Entities;

/// <summary>
/// Command to add a component to the entity associated with a resource.
/// </summary>
public interface IAddComponentCommand : IExecutableCommand
{
    /// <summary>
    /// The resource and component index to add the component to.
    /// </summary>
    ComponentKey ComponentKey { get; set; }

    /// <summary>
    /// The type of component to add to the entity.
    /// </summary>
    string ComponentType { get; set; }

}
