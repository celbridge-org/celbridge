namespace Celbridge.Entities;

/// <summary>
/// A message sent when a change is made to an entity component.
/// </summary>
public record ComponentChangedMessage(ResourceKey Resource, string ComponentType, int ComponentIndex, string PropertyPath, string Operation);
