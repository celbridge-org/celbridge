namespace Celbridge.Entities;

/// <summary>
/// A message sent when a change is made to an entity component.
/// </summary>
public record ComponentChangedMessage(ComponentKey ComponentKey, string ComponentType, string PropertyPath, string Operation);

/// <summary>
/// A message sent when the component annotations are updated for an entity.
/// </summary>
public record AnnotatedEntityMessage(ResourceKey Resource, IEntityAnnotation EntityAnnotation);

/// <summary>
/// A message sent when a new entity is created.
/// </summary>
public record EntityCreatedMessage(ResourceKey Resource);
