namespace Celbridge.Resources;

/// <summary>
/// A key that a change touched, and whether an item exists at the key when the change is delivered.
/// </summary>
public record ResourceChange(ResourceKey Resource, bool Exists);

/// <summary>
/// The changes delivered to a subscription at one time, all in the named root. A batch lists at least one change.
/// </summary>
public record ResourceChangeBatch(string RootName, IReadOnlyList<ResourceChange> Changes);

/// <summary>
/// Says that the watcher of the named root lost events. Any item in the root may have changed.
/// </summary>
public record ResourceResync(string RootName);

/// <summary>
/// The handlers of a subscription, both called on the UI thread. OnChanges receives each batch of changes. OnResync
/// is called when the watcher of a root that the subscription follows lost events.
/// </summary>
public record ResourceChangeHandlers(
    Action<ResourceChangeBatch> OnChanges,
    Action<ResourceResync> OnResync);

/// <summary>
/// A subscription to the changes in one root that the subscription's matcher matches. Dispose the subscription to
/// stop receiving changes.
/// </summary>
public interface IMatcherSubscription : IDisposable
{
    /// <summary>
    /// Returns the name of the root that the subscription watches.
    /// </summary>
    string RootName { get; }

    /// <summary>
    /// Returns the matcher that decides which changes reach the subscription.
    /// </summary>
    IPathMatcher Matcher { get; }
}

/// <summary>
/// A subscription to the changes to a list of keys, in any roots. Dispose the subscription to stop receiving changes.
/// </summary>
public interface IResourceKeySubscription : IDisposable
{
    /// <summary>
    /// Returns the keys that the subscription follows.
    /// </summary>
    IReadOnlyList<ResourceKey> Resources { get; }
}

/// <summary>
/// Watches each root that sets Capabilities.IsWatched, and reports the changes in it to subscriptions. Also schedules
/// updates of the resource registry.
/// </summary>
public interface IResourceMonitor
{
    /// <summary>
    /// Initializes the resource monitor and starts watching for file system changes
    /// across every registered root whose Capabilities.IsWatched is true.
    /// </summary>
    Result Initialize();

    /// <summary>
    /// Subscribes to the changes in a root that the matcher matches. A change to a folder also reaches the
    /// subscription when the matcher can match a path inside the folder. The matcher is called on a worker thread,
    /// so it must not change after the call. To change the matcher, subscribe again before disposing the old
    /// subscription. A change can then reach both subscriptions, and none is missed. Fails for a root that is not
    /// registered.
    /// </summary>
    Result<IMatcherSubscription> SubscribeToMatcher(
        string rootName,
        IPathMatcher matcher,
        ResourceChangeHandlers handlers);

    /// <summary>
    /// Subscribes to the changes to the keys. A change reaches the subscription when it touches one of the keys, or a
    /// folder above one. On a volume that ignores letter case, a touch in any letter case counts. Each batch lists the
    /// subscribed keys that it reaches, in their subscribed spelling, and whether an item exists at each. To change the
    /// keys, subscribe again before disposing the old subscription. Fails for a root key or a key in a root that is not
    /// registered.
    /// </summary>
    Result<IResourceKeySubscription> SubscribeToResources(
        IReadOnlyList<ResourceKey> resources,
        ResourceChangeHandlers handlers);

    /// <summary>
    /// Shuts down the resource monitor and stops watching for file system changes.
    /// </summary>
    void Shutdown();

    /// <summary>
    /// Schedules a resource update with debouncing to coalesce rapid calls.
    /// </summary>
    void ScheduleResourceUpdate();
}
