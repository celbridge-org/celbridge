namespace Celbridge.Entities;

/// <summary>
/// Represents an instance of a component.
/// Provides convenient access to the component's data and configuration info.
/// </summary>
public interface IComponentProxy
{
    /// <summary>
    /// Returns true if the component proxy reference is still valid.
    /// Proxies are invalidated when any structural changes are made to the entity.
    /// </summary>
    bool IsValid { get; }

    /// <summary>
    /// Returns the component key used to identify the component.
    /// </summary>
    ComponentKey Key { get; }

    /// <summary>
    /// Returns the schema of the component.
    /// </summary>
    ComponentSchema Schema { get; }

    /// <summary>
    /// Raised when a component property changes.
    /// </summary>
    event Action<string>? ComponentPropertyChanged;

    /// <summary>
    /// The validation status of the component.
    /// </summary>
    ComponentStatus Status { get; }

    /// <summary>
    /// The description of the component displayed in the editor UI.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// The tooltip displayed in the editor UI.
    /// </summary>
    string Tooltip { get; }

    /// <summary>
    /// Gets the value of a string property.
    /// propertyPath is a JSON Pointer (RFC 6901).
    /// Fails if the property cannot be found, or is of the wrong type.
    /// </summary>
    Result<T> GetProperty<T>(string propertyPath) where T : notnull;

    /// <summary>
    /// Gets the value of a property from a component.
    /// propertyPath is a JSON Pointer (RFC 6901).
    /// Returns the default value if the component or property cannot be found.
    /// </summary>
    T? GetProperty<T>(string propertyPath, T? defaultValue) where T : notnull;

    /// <summary>
    /// Convenience method to get a string property with minimal boilerplate.
    /// Returns the default value if the property cannot be found.
    /// </summary>
    string GetString(string propertyPath, string defaultValue = "");

    /// <summary>
    /// Replaces the value of an existing entity property for a component.
    /// If insert is true then the value is inserted at the specified key/index, rather than replacing the existing entry.
    /// propertyPath is a JSON Pointer (RFC 6901).
    /// </summary>
    Result SetProperty<T>(string propertyPath, T newValue, bool insert = false) where T : notnull;
}
