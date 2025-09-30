namespace Celbridge.Entities;

/// <summary>
/// Describes the name and type of a component property, and its associated attributes.
/// </summary>
public record ComponentPropertyInfo(string PropertyName, string PropertyType, IReadOnlyDictionary<string, string> Attributes);
