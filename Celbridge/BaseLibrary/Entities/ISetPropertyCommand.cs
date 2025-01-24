using Celbridge.Commands;

namespace Celbridge.Entities;

/// <summary>
/// Command to set a property of an entity component.
/// The change is validated against the component schema before being applied.
/// </summary>
public interface ISetPropertyCommand : IExecutableCommand
{
    /// <summary>
    /// The component to query.
    /// </summary>
    ComponentKey ComponentKey { get; set; }

    /// <summary>
    /// A JSON Pointer (RFC 6901) string representing the component property to set.
    /// </summary>
    string PropertyPath { get; set; }

    /// <summary>
    /// The JSON formatted value to set the property to.
    /// </summary>
    string JsonValue { get; set; }

    /// <summary>
    /// Specifies whether to insert a new value rather than replacing the existing value at the specified key/index.
    /// This is primarily used to insert new values into arrays, shifting the existing values to the right.
    /// </summary>
    bool Insert { get; set; }
}
