namespace Celbridge.Entities;

/// <summary>
/// A reference to a component, consisting of a resource key and a component index.
/// </summary>
public record struct ComponentKey(ResourceKey Resource, int ComponentIndex)
{
    public static ComponentKey Empty => new ComponentKey(ResourceKey.Empty, -1);
}
