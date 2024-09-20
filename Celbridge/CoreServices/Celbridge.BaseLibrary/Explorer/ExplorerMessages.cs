namespace Celbridge.Explorer;

/// <summary>
/// A message sent when the resource registry has been updated.
/// </summary>
public record ResourceRegistryUpdatedMessage;

/// <summary>
/// A message sent when a resource has been moved or renamed.
/// </summary>
public record ResourceKeyChangedMessage(ResourceKey SourceResource, ResourceKey DestResource, string DestPath);

/// <summary>
/// A message sent when the selected resource in the Explorer Panel has changed.
/// </summary>
public record SelectedResourceChangedMessage(ResourceKey Resource);

