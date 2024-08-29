namespace Celbridge.Resources;

/// <summary>
/// A message sent when the resource registry has been updated.
/// </summary>
public record ResourceRegistryUpdatedMessage;

/// <summary>
/// A message sent when a resource has been renamed.
/// </summary>
public record ResourceKeyChangedMessage(ResourceKey SourceResource, ResourceKey DestResource, string DestPath);
