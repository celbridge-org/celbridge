namespace Celbridge.Resources;

/// <summary>
/// A message sent when the resource registry has been updated.
/// </summary>
public record ResourceRegistryUpdatedMessage;

/// <summary>
/// A message sent when a resource has been moved or renamed.
/// </summary>
public record ResourceKeyChangedMessage(ResourceKey SourceResource, ResourceKey DestResource);

/// <summary>
/// A message sent when the selected resource in the Explorer Panel has changed.
/// </summary>
public record SelectedResourceChangedMessage(ResourceKey Resource);

/// <summary>
/// A message sent when a monitored resource has been created in the file system.
/// </summary>
public record MonitoredResourceCreatedMessage(ResourceKey Resource);

/// <summary>
/// A message sent when a monitored resource has been modified in the file system.
/// </summary>
public record MonitoredResourceChangedMessage(ResourceKey Resource);

/// <summary>
/// A message sent when a monitored resource has been deleted from the file system.
/// </summary>
public record MonitoredResourceDeletedMessage(ResourceKey Resource);

/// <summary>
/// A message sent when a monitored resource has been renamed or moved in the file system.
/// </summary>
public record MonitoredResourceRenamedMessage(ResourceKey OldResource, ResourceKey NewResource);

/// <summary>
/// A message sent when resource updates are requested after command execution.
/// </summary>
public record ResourceUpdateRequestedMessage(bool ForceImmediate);
