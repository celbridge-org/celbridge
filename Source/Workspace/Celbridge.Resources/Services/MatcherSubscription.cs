namespace Celbridge.Resources.Services;

/// <summary>
/// A subscription to the changes in one root that the subscription's matcher matches.
/// </summary>
internal sealed class MatcherSubscription : IMatcherSubscription
{
    private readonly ResourceChangeHandlers _handlers;
    private readonly Action<MatcherSubscription> _onDisposed;
    private readonly IPathMatcher _matcher;
    private volatile bool _isDisposed;

    /// <summary>
    /// Creates a subscription to the changes in the root that the matcher matches. The onDisposed action is called
    /// once, when the subscription is disposed.
    /// </summary>
    public MatcherSubscription(
        string rootName,
        IPathMatcher matcher,
        ResourceChangeHandlers handlers,
        Action<MatcherSubscription> onDisposed)
    {
        RootName = rootName;
        _matcher = matcher;
        _handlers = handlers;
        _onDisposed = onDisposed;
    }

    public string RootName { get; }

    public IPathMatcher Matcher => _matcher;

    /// <summary>
    /// Returns true once the subscription has been disposed.
    /// </summary>
    public bool IsDisposed => _isDisposed;

    /// <summary>
    /// Returns true if a change at the resource path matches. When the item at the path may be a folder, the change
    /// also matches if the matcher can match a path inside the folder. An absent item may have been a folder.
    /// </summary>
    public bool Matches(string resourcePath, bool mayBeFolder)
    {
        if (_matcher.IsMatch(resourcePath, isFolder: mayBeFolder))
        {
            return true;
        }

        return mayBeFolder
            && _matcher.CanMatchInside(resourcePath);
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
