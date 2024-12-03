using Celbridge.Commands;

namespace Celbridge.Entities;

/// <summary>
/// Command to move an entity component from a source index to a destination index.
/// </summary>
public interface IMoveComponentCommand : IExecutableCommand
{
    /// <summary>
    /// The resource associated with the entity data to be modified.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// The source index of the component to move.
    /// The component at this index will be removed, shifting all components after it down by one.
    /// </summary>
    int SourceComponentIndex { get; set; }

    /// <summary>
    /// The destination index of the component to move.
    /// The source component is removed first, and then it is inserted at this index.
    /// </summary>
    int DestComponentIndex { get; set; }
}
