namespace Celbridge.Resources;

/// <summary>
/// Types of resource operations that can fail.
/// </summary>
public enum ResourceOperationType
{
    Delete,
    Copy,
    Move,
    Rename,
    Create
}

/// <summary>
/// A message sent to request a resource registry update after command execution.
/// </summary>
public record RequestResourceRegistryUpdateMessage(bool ForceImmediate);

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
/// A message sent when a resource operation fails.
/// </summary>
public record ResourceOperationFailedMessage(ResourceOperationType OperationType, List<string> FailedItems);

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
