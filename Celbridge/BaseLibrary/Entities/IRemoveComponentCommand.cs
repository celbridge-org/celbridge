using Celbridge.Commands;

namespace Celbridge.Entities;

/// <summary>
/// Command to remove an entity component associated with a resource.
/// </summary>
public interface IRemoveComponentCommand : IExecutableCommand
{
    /// <summary>
    /// The resource and component index to remove the component from.
    /// </summary>
    ComponentKey ComponentKey { get; set; }
}
