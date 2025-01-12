namespace Celbridge.Entities;

/// <summary>
/// A message sent when a change is made to an entity component.
/// </summary>
public record ComponentChangedMessage(ComponentKey ComponentKey, string ComponentType, string PropertyPath, string Operation);

/// <summary>
/// A message sent when the component annotation data is updated.
/// </summary>
public record ComponentAnnotationUpdatedMessage(ComponentKey ComponentKey);

/// <summary>
/// A message sent when a new entity is created.
/// </summary>
public record EntityCreatedMessage(ResourceKey Resource);
