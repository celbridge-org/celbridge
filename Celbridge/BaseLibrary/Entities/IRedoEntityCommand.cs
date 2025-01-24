using Celbridge.Commands;

namespace Celbridge.Entities;

/// <summary>
/// Command to redo the most recent entity change for a resource.
/// </summary>
public interface IRedoEntityCommand : IExecutableCommand
{
    /// <summary>
    /// The resource associated with the Entity Data to be modified.
    /// </summary>
    ResourceKey Resource { get; set; }
}
