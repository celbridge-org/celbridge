using Celbridge.Commands;

namespace Celbridge.Entities;

/// <summary>
/// Command to undo the most recent property change for a resource.
/// </summary>
public interface IUndoPropertyCommand : IExecutableCommand
{
    /// <summary>
    /// The resource associated with the Entity Data to be modified.
    /// </summary>
    ResourceKey Resource { get; set; }
}
