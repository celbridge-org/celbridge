using Celbridge.Commands;

namespace Celbridge.Entities;

/// <summary>
/// Command to modify an entity.
/// </summary>
public interface IModifyEntityCommand : IExecutableCommand
{
    /// <summary>
    /// The resource associated with the entity data to be modified.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// A JSON Patch (RFC 6902) string representing the changes to be made to the entity data.
    /// </summary>
    string Patch { get; set; }
}
