namespace Celbridge.Entities;

/// <summary>
/// Describes the attributes and properties of a component type.
/// </summary>
public record ComponentSchema(
    string ComponentType, 
    int ComponentVersion, 
    IReadOnlySet<string> Tags,
    IReadOnlyDictionary<string, string> Attributes, 
    IReadOnlyList<ComponentPropertyInfo> Properties,
    IComponentDescriptor Descriptor)
{
    /// <summary>
    /// Returns true if the component type has the specified tag.
    /// Tags are defined at design time and are used to categorize component types.
    /// </summary>
    public bool HasTag(string tag) => Tags.Contains(tag);

    /// <summary>
    /// Gets a boolean attribute value.
    /// Returns false if the attribute is not found or cannot be parsed.
    /// </summary>
    public bool GetBooleanAttribute(string attributeName) => Attributes.TryGetValue(attributeName, out var value) && bool.TryParse(value, out var result) && result;

    /// <summary>
    /// Gets a string attribute value.
    /// Returns an empty string if the attribute is not found.
    /// </summary>
    public string GetStringAttribute(string attributeName) => Attributes.TryGetValue(attributeName, out var value) ? value : string.Empty;

    /// <summary>
    /// Gets an integer attribute value.
    /// Returns 0 if the attribute is not found or cannot be parsed.
    /// </summary>
    public int GetIntAttribute(string attributeName) => Attributes.TryGetValue(attributeName, out var value) && int.TryParse(value, out var result) ? result : 0;

    /// <summary>
    /// Gets a double attribute value.
    /// Returns 0 if the attribute is not found or cannot be parsed.
    /// </summary>
    public double GetDoubleAttribute(string attributeName) => Attributes.TryGetValue(attributeName, out var value) && double.TryParse(value, out var result) ? result : 0.0;
}
