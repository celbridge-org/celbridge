namespace Celbridge.Resources.Services;

/// <summary>
/// The subscriptions that may want the changes in one root. These are the matcher subscriptions of the root, and the
/// key subscriptions with a key in the root.
/// </summary>
internal sealed record RootSubscriptions(
    IReadOnlyList<MatcherSubscription> MatcherSubscriptions,
    IReadOnlyList<ResourceKeySubscription> KeySubscriptions)
{
    public bool IsEmpty => MatcherSubscriptions.Count == 0 && KeySubscriptions.Count == 0;
}

/// <summary>
/// The pattern and key subscriptions of a resource monitor. The batchers of every root share one instance.
/// </summary>
internal sealed class ResourceSubscriptions
{
    // Guards the writes. Each array is replaced as a whole, so a reader takes it without the lock.
    private readonly object _lock = new();

    private MatcherSubscription[] _matcherSubscriptions = Array.Empty<MatcherSubscription>();
    private ResourceKeySubscription[] _keySubscriptions = Array.Empty<ResourceKeySubscription>();

    public void Add(MatcherSubscription subscription)
    {
        lock (_lock)
        {
            _matcherSubscriptions = _matcherSubscriptions.Append(subscription).ToArray();
        }
    }

    public void Add(ResourceKeySubscription subscription)
    {
        lock (_lock)
        {
            _keySubscriptions = _keySubscriptions.Append(subscription).ToArray();
        }
    }

    public void Remove(MatcherSubscription subscription)
    {
        lock (_lock)
        {
            _matcherSubscriptions = _matcherSubscriptions.Where(item => item != subscription).ToArray();
        }
    }

    public void Remove(ResourceKeySubscription subscription)
    {
        lock (_lock)
        {
            _keySubscriptions = _keySubscriptions.Where(item => item != subscription).ToArray();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _matcherSubscriptions = Array.Empty<MatcherSubscription>();
            _keySubscriptions = Array.Empty<ResourceKeySubscription>();
        }
    }

    /// <summary>
    /// Returns true if a subscription may want a change in the root.
    /// </summary>
    public bool HasSubscriptionsFor(string rootName)
    {
        var matcherSubscriptions = Volatile.Read(ref _matcherSubscriptions);
        foreach (var subscription in matcherSubscriptions)
        {
            if (subscription.RootName == rootName)
            {
                return true;
            }
        }

        var keySubscriptions = Volatile.Read(ref _keySubscriptions);
        foreach (var subscription in keySubscriptions)
        {
            if (subscription.HasKeysIn(rootName))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the subscriptions that may want a change in the root.
    /// </summary>
    public RootSubscriptions GetSubscriptionsFor(string rootName)
    {
        var matcherSubscriptions = Volatile.Read(ref _matcherSubscriptions)
            .Where(subscription => subscription.RootName == rootName)
            .ToList();

        var keySubscriptions = Volatile.Read(ref _keySubscriptions)
            .Where(subscription => subscription.HasKeysIn(rootName))
            .ToList();

        return new RootSubscriptions(matcherSubscriptions, keySubscriptions);
    }
}
