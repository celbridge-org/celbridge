using Celbridge.Commands;

namespace Celbridge.Entities;

/// <summary>
/// Command to redo the most recent property change for a resource.
/// </summary>
public interface IRedoPropertyCommand : IExecutableCommand
{
    /// <summary>
    /// The resource associated with the Entity Data to be modified.
    /// </summary>
    ResourceKey Resource { get; set; }
}
