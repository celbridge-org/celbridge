namespace Celbridge.Entities;

/// <summary>
/// A message describing a change made to an entity property.
/// </summary>
public record EntityPropertyChangedMessage(ResourceKey Resource, string PropertyPath, EntityPropertyChangeType ChangeType);
