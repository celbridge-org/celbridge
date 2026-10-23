using Celbridge.Resources;

namespace Celbridge.Tests.Helpers;

/// <summary>
/// A resource monitor that records the subscriptions made to it, and delivers inline the changes and resyncs a test
/// raises. The test says what is at each key, in place of a watcher and a disk check.
/// </summary>
public sealed class FakeResourceMonitor : IResourceMonitor
{
    private sealed class MatcherSubscription : IMatcherSubscription
    {
        private readonly FakeResourceMonitor _owner;

        public MatcherSubscription(FakeResourceMonitor owner, string rootName, IPathMatcher matcher, ResourceChangeHandlers handlers)
        {
            _owner = owner;
            RootName = rootName;
            Matcher = matcher;
            Handlers = handlers;
        }

        public string RootName { get; }

        public IPathMatcher Matcher { get; }

        public ResourceChangeHandlers Handlers { get; }

        public void Dispose()
        {
            _owner._matcherSubscriptions.Remove(this);
        }
    }

    private sealed class KeySubscription : IResourceKeySubscription
    {
        private readonly FakeResourceMonitor _owner;

        public KeySubscription(FakeResourceMonitor owner, IReadOnlyList<ResourceKey> resources, ResourceChangeHandlers handlers)
        {
            _owner = owner;
            Resources = resources;
            Handlers = handlers;
        }

        public IReadOnlyList<ResourceKey> Resources { get; }

        public ResourceChangeHandlers Handlers { get; }

        public void Dispose()
        {
            _owner._keySubscriptions.Remove(this);
        }
    }

    private readonly List<MatcherSubscription> _matcherSubscriptions = new();
    private readonly List<KeySubscription> _keySubscriptions = new();

    /// <summary>
    /// Returns the matcher subscriptions that have not been disposed.
    /// </summary>
    public IReadOnlyList<IMatcherSubscription> MatcherSubscriptions => _matcherSubscriptions.ToList();

    /// <summary>
    /// Returns the key subscriptions that have not been disposed.
    /// </summary>
    public IReadOnlyList<IResourceKeySubscription> KeySubscriptions => _keySubscriptions.ToList();

    public Result Initialize()
    {
        return Result.Ok();
    }

    public Result<IMatcherSubscription> SubscribeToMatcher(string rootName, IPathMatcher matcher, ResourceChangeHandlers handlers)
    {
        var subscription = new MatcherSubscription(this, rootName, matcher, handlers);
        _matcherSubscriptions.Add(subscription);

        return Result<IMatcherSubscription>.Ok(subscription);
    }

    public Result<IResourceKeySubscription> SubscribeToResources(IReadOnlyList<ResourceKey> resources, ResourceChangeHandlers handlers)
    {
        var subscription = new KeySubscription(this, resources, handlers);
        _keySubscriptions.Add(subscription);

        return Result<IResourceKeySubscription>.Ok(subscription);
    }

    /// <summary>
    /// Returns the keys of each report, in the order the reports arrived. A test delivers the changes that a report
    /// would cause with DeliverChange.
    /// </summary>
    public List<IReadOnlyList<ResourceKey>> Reports { get; } = new();

    public void ReportOperationChanges(IReadOnlyList<ResourceKey> resources)
    {
        Reports.Add(resources);
    }

    public void Shutdown()
    {
        _matcherSubscriptions.Clear();
        _keySubscriptions.Clear();
    }

    /// <summary>
    /// Delivers a change to each subscription that follows the key. A key subscription follows the keys it lists. A
    /// matcher subscription follows each key in the subscription's root that the matcher matches. A folder or an absent
    /// key also reaches a matcher that can match a path inside the folder.
    /// </summary>
    public void DeliverChange(ResourceKey resource, ResourceItemState state)
    {
        var change = new ResourceChange(resource, state);
        var batch = new ResourceChangeBatch(resource.Root, new[] { change });

        bool mayBeFolder = state != ResourceItemState.File;
        foreach (var subscription in _matcherSubscriptions.ToList())
        {
            if (subscription.RootName != resource.Root)
            {
                continue;
            }

            bool matches = subscription.Matcher.IsMatch(resource.Path, mayBeFolder)
                || (mayBeFolder && subscription.Matcher.CanMatchInside(resource.Path));
            if (matches)
            {
                subscription.Handlers.OnChanges(batch);
            }
        }

        foreach (var subscription in _keySubscriptions.ToList())
        {
            if (subscription.Resources.Contains(resource))
            {
                subscription.Handlers.OnChanges(batch);
            }
        }
    }

    /// <summary>
    /// Delivers a resync to each subscription that follows the root.
    /// </summary>
    public void DeliverResync(string rootName)
    {
        var resync = new ResourceResync(rootName);

        foreach (var subscription in _matcherSubscriptions.ToList())
        {
            if (subscription.RootName == rootName)
            {
                subscription.Handlers.OnResync(resync);
            }
        }

        foreach (var subscription in _keySubscriptions.ToList())
        {
            if (subscription.Resources.Any(resource => resource.Root == rootName))
            {
                subscription.Handlers.OnResync(resync);
            }
        }
    }
}
