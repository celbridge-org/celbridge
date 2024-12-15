namespace Celbridge.Inspector;

/// <summary>
/// A factory for creating field UI elements for editing properties.
/// </summary>
public interface IFieldFactory
{
    /// <summary>
    /// Creates a field UI element for editing a property on an entity component.
    /// </summary>
    public Result<IField> CreatePropertyField(ResourceKey resource, int componentIndex, string propertyName);
}
