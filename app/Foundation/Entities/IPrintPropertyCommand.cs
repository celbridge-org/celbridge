using Celbridge.Commands;

namespace Celbridge.Entities;

/// <summary>
/// Command to print to the log a property from the JSON Entity Data of the entity associated with a resource.
/// </summary>
public interface IPrintPropertyCommand : IExecutableCommand
{
    /// <summary>
    /// The component to query.
    /// </summary>
    ComponentKey ComponentKey { get; set; }

    /// <summary>
    /// A JSON Pointer (RFC 6901) string representing the JSON property to be printed.
    /// </summary>
    string PropertyPath { get; set; }
}
