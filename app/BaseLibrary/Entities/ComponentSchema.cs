namespace Celbridge.Entities;

/// <summary>
/// Describes the attributes and properties of a component type.
/// </summary>
public record ComponentSchema(
    string ComponentType, 
    int ComponentVersion, 
    IReadOnlySet<string> Tags,
    IReadOnlyDictionary<string, string> Attributes, 
    IReadOnlyList<ComponentPropertyInfo> Properties);
