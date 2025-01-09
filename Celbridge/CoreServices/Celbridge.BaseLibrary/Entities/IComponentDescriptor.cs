namespace Celbridge.Entities;

/// <summary>
/// Defines the schema and presentation code for a component type.
/// </summary>
public interface IComponentDescriptor
{
    /// <summary>
    /// Returns the JSON schema text for the component.
    /// </summary>
    string SchemaJson { get; }
}
