namespace Celbridge.Resources.Services;

/// <summary>
/// A subscription to the changes to a list of keys, in any roots.
/// </summary>
internal sealed class ResourceKeySubscription : IResourceKeySubscription
{
    private readonly ResourceChangeHandlers _handlers;
    private readonly Action<ResourceKeySubscription> _onDisposed;
    private readonly IReadOnlyList<ResourceKey> _resources;
    private volatile bool _isDisposed;

    /// <summary>
    /// Creates a subscription to the changes to the keys, each key once. The onDisposed action is called once, when the
    /// subscription is disposed.
    /// </summary>
    public ResourceKeySubscription(
        IReadOnlyList<ResourceKey> resources,
        ResourceChangeHandlers handlers,
        Action<ResourceKeySubscription> onDisposed)
    {
        _resources = resources.Distinct().ToList();
        _handlers = handlers;
        _onDisposed = onDisposed;
    }

    public IReadOnlyList<ResourceKey> Resources => _resources;

    /// <summary>
    /// Returns true once the subscription has been disposed.
    /// </summary>
    public bool IsDisposed => _isDisposed;

    /// <summary>
    /// Returns true if one of the subscribed keys is in the root.
    /// </summary>
    public bool HasKeysIn(string rootName)
    {
        foreach (var resource in _resources)
        {
            if (resource.Root == rootName)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the subscribed keys in the root that a touched path reaches, in the subscription's order. A touched
    /// path reaches a key when it is the key's path, or the path of a folder above the key. The comparer of the set
    /// of touched paths decides whether letter case matters.
    /// </summary>
    public IReadOnlyList<ResourceKey> FindReachedKeys(string rootName, IReadOnlySet<string> touchedPaths)
    {
        var reachedResources = new List<ResourceKey>();
        foreach (var resource in _resources)
        {
            if (resource.Root == rootName
                && IsReached(resource.Path, touchedPaths))
            {
                reachedResources.Add(resource);
            }
        }

        return reachedResources;
    }

    /// <summary>
    /// Calls the subscriber's changes handler with the batch, unless the subscription has been disposed.
    /// </summary>
    public void DeliverChanges(ResourceChangeBatch batch)
    {
        if (_isDisposed)
        {
            return;
        }

        _handlers.OnChanges(batch);
    }

    /// <summary>
    /// Calls the subscriber's resync handler, unless the subscription has been disposed.
    /// </summary>
    public void DeliverResync(ResourceResync resync)
    {
        if (_isDisposed)
        {
            return;
        }

        _handlers.OnResync(resync);
    }

    // Looks up the path, then the path of each folder above it.
    private static bool IsReached(string resourcePath, IReadOnlySet<string> touchedPaths)
    {
        var currentPath = resourcePath;
        while (true)
        {
            if (touchedPaths.Contains(currentPath))
            {
                return true;
            }

            var slashIndex = currentPath.LastIndexOf('/');
            if (slashIndex < 0)
            {
                return false;
            }

            currentPath = currentPath.Substring(0, slashIndex);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _onDisposed(this);
    }
}
