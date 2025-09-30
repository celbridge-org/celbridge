namespace Celbridge.Entities;

/// <summary>
/// A factory for creating component schema readers.
/// </summary>
public interface IComponentSchemaReaderFactory
{
    /// <summary>
    /// Factory method for creating a component schema reader.
    /// </summary>
    IComponentSchemaReader Create(ComponentSchema schema);
}

/// <summary>
/// A helper utility for reading component schema information.
/// </summary>
public interface IComponentSchemaReader
{
    /// <summary>
    /// Returns the schema of the component.
    /// </summary>
    ComponentSchema Schema { get; }

    /// <summary>
    /// Returns true if the component schema has the specified tag.
    /// Tags are defined at design time and are used to categorize component types.
    /// </summary>
    bool HasTag(string tag);

    /// <summary>
    /// Returns the property information for the specified property name
    /// </summary>
    Result<ComponentPropertyInfo> GetPropertyInfo(string propertyName);

    /// <summary>
    /// Gets a boolean attribute value for the specified property name.
    /// If property name is empty, the entity attributes are searched instead.
    /// Returns false if the attribute is not found or cannot be parsed.
    /// </summary>
    bool GetBooleanAttribute(string attributeName, string propertyName = "");

    /// <summary>
    /// Gets a string attribute value for the specified property name.
    /// If property name is empty, the entity attributes are searched instead.
    /// Returns an empty string if the attribute is not found.
    /// </summary>
    string GetStringAttribute(string attributeName, string propertyPath = "");

    /// <summary>
    /// Gets an integer attribute value for the specified property name.
    /// If property name is empty, the entity attributes are searched instead.
    /// Returns 0 if the attribute is not found or cannot be parsed.
    /// </summary>    
    int GetIntAttribute(string attributeName, string propertyPath = "");

    /// <summary>
    /// Gets a double attribute value for the specified property name.
    /// If property name is empty, the entity attributes are searched instead.
    /// Returns 0 if the attribute is not found or cannot be parsed.
    /// </summary>
    double GetDoubleAttribute(string attributeName, string propertyPath = "");

    /// <summary>
    /// Gets an JSON serialized object attribute value of type T for the specified property name.
    /// If property name is empty, the entity attributes are searched instead.
    /// </summary>
    Result<T> GetObjectAttribute<T>(string attributeName, string propertyPath = "") where T : notnull;
}

