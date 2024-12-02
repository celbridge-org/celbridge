using Celbridge.Commands;

namespace Celbridge.Entities;

/// <summary>
/// Command to set a property of an entity component.
/// The change is validated against the component schema before being applied.
/// </summary>
public interface ISetPropertyCommand : IExecutableCommand
{
    /// <summary>
    /// The resource associated with the entity data to be modified.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// The index of the component that contains the property to be set.
    /// </summary>
    int ComponentIndex { get; set; }

    /// <summary>
    /// A JSON Pointer (RFC 6901) string representing the component property to set.
    /// </summary>
    string PropertyPath { get; set; }

    /// <summary>
    /// The JSON formatted value to set the property to.
    /// </summary>
    string JsonValue { get; set; }
}
