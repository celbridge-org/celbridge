using Celbridge.Commands;

namespace Celbridge.Entities;

/// <summary>
/// Command to copy an entity component from a source index to a destination index.
/// </summary>
public interface ICopyComponentCommand : IExecutableCommand
{
    /// <summary>
    /// The resource associated with the entity data to be modified.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// The source index of the component to copy.
    /// </summary>
    int SourceComponentIndex { get; set; }

    /// <summary>
    /// The destination index of the component to copy.
    /// </summary>
    int DestComponentIndex { get; set; }
}
