using Celbridge.Commands;

namespace Celbridge.Entities;

/// <summary>
/// Command to undo the most recent modification to the Entity Data associated with a resource.
/// </summary>
public interface IUndoEntityCommand : IExecutableCommand
{
    /// <summary>
    /// The resource associated with the Entity Data to be modified.
    /// </summary>
    ResourceKey Resource { get; set; }
}
