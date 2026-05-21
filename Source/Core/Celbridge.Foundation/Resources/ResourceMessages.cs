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
    Create,
    Archive,
    Extract
}

/// <summary>
/// A message sent to request a synchronous resource registry update after command execution.
/// </summary>
public record RequestResourceRegistryUpdateMessage;

/// <summary>
/// A message sent when the resource registry has been updated.
/// </summary>
public record ResourceRegistryUpdatedMessage;

/// <summary>
/// A message sent to request a resource tree view refresh without updating the resource registry.
/// </summary>
public record RefreshResourceTreeMessage;

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
/// Broadcast when a resource has appeared at the given key. Fired by the
/// filesystem watcher and by structural operations that have already applied
/// the change on disk.
/// </summary>
public record ResourceCreatedMessage(ResourceKey Resource);

/// <summary>
/// Broadcast when an existing resource's bytes have changed.
/// </summary>
public record ResourceChangedMessage(ResourceKey Resource);

/// <summary>
/// Broadcast when a resource has been removed from the given key. Fired by the
/// filesystem watcher and by structural operations that have already applied
/// the change on disk.
/// </summary>
public record ResourceDeletedMessage(ResourceKey Resource);

/// <summary>
/// Broadcast when a resource has moved from one key to another.
/// </summary>
public record ResourceRenamedMessage(ResourceKey OldResource, ResourceKey NewResource);
