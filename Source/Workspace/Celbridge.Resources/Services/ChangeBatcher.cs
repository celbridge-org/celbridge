using Celbridge.Logging;

namespace Celbridge.Resources.Services;

/// <summary>
/// Collects the keys touched in one root, and delivers them in batches to the subscriptions that may want them. A
/// batch closes a fixed interval after its first key. At delivery, each key that a subscription wants is checked on
/// disk, so each change says whether an item exists at its key. Batches for one root are delivered one at a time, in
/// order.
/// </summary>
internal sealed class ChangeBatcher : IDisposable
{
    // Long enough to collect the events of one file operation, a save or a checkout, into one batch.
    internal static readonly TimeSpan BatchInterval = TimeSpan.FromMilliseconds(100);

    private enum ItemState
    {
        File,
        Folder,
        Gone,
    }

    private sealed record ReachedKeys(ResourceKeySubscription Subscription, IReadOnlyList<ResourceKey> Resources);

    private readonly IResourceRootHandler _handler;
    private readonly ResourceSubscriptions _subscriptions;
    private readonly ILocalFileSystem _fileSystem;
    private readonly IDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    // Guards every field below.
    private readonly object _lock = new();

    // The pending keys in the order they were first touched, each once.
    private List<ResourceKey> _pendingKeys = new();
    private HashSet<ResourceKey> _pendingKeySet = new();
    private long _firstPendingTimestamp;
    private bool _isResyncPending;
    private bool _isDelivering;
    private ITimer? _timer;
    private bool _isDisposed;

    public ChangeBatcher(
        IResourceRootHandler handler,
        ResourceSubscriptions subscriptions,
        ILocalFileSystem fileSystem,
        IDispatcher dispatcher,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _handler = handler;
        _subscriptions = subscriptions;
        _fileSystem = fileSystem;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Adds a touched key to the pending batch, each key once. Drops the key when no subscription follows the root.
    /// </summary>
    public void AddTouchedKey(ResourceKey resource)
    {
        if (!_subscriptions.HasSubscriptionsFor(_handler.RootName))
        {
            return;
        }

        lock (_lock)
        {
            if (_isDisposed)
            {
                return;
            }

            if (!_pendingKeySet.Add(resource))
            {
                return;
            }

            if (_pendingKeys.Count == 0)
            {
                _firstPendingTimestamp = _timeProvider.GetTimestamp();
            }
            _pendingKeys.Add(resource);

            if (_timer is null
                && !_isDelivering)
            {
                _timer = CreateTimer(BatchInterval);
            }
        }
    }

    /// <summary>
    /// Sends each subscription a resync, after any pending changes. The resync goes out without waiting for
    /// the batch interval.
    /// </summary>
    public void RequestResync()
    {
        lock (_lock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isResyncPending = true;

            if (_isDelivering)
            {
                return;
            }

            if (_timer is null)
            {
                _timer = CreateTimer(TimeSpan.Zero);
            }
            else
            {
                _timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private ITimer CreateTimer(TimeSpan dueTime)
    {
        return _timeProvider.CreateTimer(
            callback: _ => OnTimerElapsed(),
            state: null,
            dueTime: dueTime,
            period: Timeout.InfiniteTimeSpan);
    }

    // The delivery logs its own failures, so the task is not awaited.
    private void OnTimerElapsed()
    {
        _ = DeliverPendingAsync();
    }

    private async Task DeliverPendingAsync()
    {
        List<ResourceKey> keys;
        bool isResync;

        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;

            if (_isDisposed
                || _isDelivering)
            {
                return;
            }

            keys = _pendingKeys;
            _pendingKeys = new();
            _pendingKeySet = new();
            isResync = _isResyncPending;
            _isResyncPending = false;
            _isDelivering = true;
        }

        try
        {
            var subscriptions = _subscriptions.GetSubscriptionsFor(_handler.RootName);

            await DeliverChangesAsync(keys, subscriptions);

            if (isResync)
            {
                DeliverResync(subscriptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver the changes in root '{RootName}'", _handler.RootName);
        }
        finally
        {
            lock (_lock)
            {
                _isDelivering = false;

                if (!_isDisposed)
                {
                    ArmTimerForPendingWork();
                }
            }
        }
    }

    // Starts the timer for the work that arrived during a delivery. Call it under the lock.
    private void ArmTimerForPendingWork()
    {
        if (_isResyncPending)
        {
            _timer = CreateTimer(TimeSpan.Zero);
            return;
        }

        if (_pendingKeys.Count == 0)
        {
            return;
        }

        var elapsed = _timeProvider.GetElapsedTime(_firstPendingTimestamp);
        var dueTime = BatchInterval - elapsed;
        if (dueTime < TimeSpan.Zero)
        {
            dueTime = TimeSpan.Zero;
        }

        _timer = CreateTimer(dueTime);
    }

    private async Task DeliverChangesAsync(List<ResourceKey> keys, RootSubscriptions subscriptions)
    {
        if (keys.Count == 0
            || subscriptions.IsEmpty)
        {
            return;
        }

        // Keys are matched before the disk is checked, so only the keys that a subscription wants are checked. Until
        // the check, any key may name a folder.
        var wantedKeys = new List<ResourceKey>();
        foreach (var key in keys)
        {
            bool isWanted = subscriptions.MatcherSubscriptions
                .Any(subscription => subscription.Matches(key.Path, mayBeFolder: true));
            if (isWanted)
            {
                wantedKeys.Add(key);
            }
        }

        // On a volume that ignores letter case, a touched path reaches a subscribed key in any letter case. The watcher
        // on such a volume can report a delete in the letter case of the call that made it.
        var touchedPaths = new HashSet<string>(keys.Select(key => key.Path), PathComparison.Comparer);
        var reachedKeys = new List<ReachedKeys>();
        foreach (var subscription in subscriptions.KeySubscriptions)
        {
            var resources = subscription.FindReachedKeys(_handler.RootName, touchedPaths);
            if (resources.Count > 0)
            {
                reachedKeys.Add(new ReachedKeys(subscription, resources));
            }
        }

        var itemStates = new Dictionary<ResourceKey, ItemState>();
        var keysToCheck = wantedKeys.Concat(reachedKeys.SelectMany(reached => reached.Resources));
        foreach (var key in keysToCheck)
        {
            if (!itemStates.ContainsKey(key))
            {
                itemStates[key] = await CheckItemAsync(key);
            }
        }

        foreach (var subscription in subscriptions.MatcherSubscriptions)
        {
            var changes = new List<ResourceChange>();
            foreach (var key in wantedKeys)
            {
                var state = itemStates[key];
                bool mayBeFolder = state != ItemState.File;
                if (subscription.Matches(key.Path, mayBeFolder))
                {
                    changes.Add(new ResourceChange(key, Exists: state != ItemState.Gone));
                }
            }

            if (changes.Count > 0)
            {
                var batch = new ResourceChangeBatch(_handler.RootName, changes);
                EnqueueDelivery(() => subscription.DeliverChanges(batch));
            }
        }

        foreach (var reached in reachedKeys)
        {
            var changes = reached.Resources
                .Select(key => new ResourceChange(key, Exists: itemStates[key] != ItemState.Gone))
                .ToList();
            var batch = new ResourceChangeBatch(_handler.RootName, changes);
            EnqueueDelivery(() => reached.Subscription.DeliverChanges(batch));
        }
    }

    private void DeliverResync(RootSubscriptions subscriptions)
    {
        var resync = new ResourceResync(_handler.RootName);
        foreach (var subscription in subscriptions.MatcherSubscriptions)
        {
            EnqueueDelivery(() => subscription.DeliverResync(resync));
        }
        foreach (var subscription in subscriptions.KeySubscriptions)
        {
            EnqueueDelivery(() => subscription.DeliverResync(resync));
        }
    }

    // Calls the subscriber on the UI thread. The dispatcher runs actions in the order they were enqueued, so batches
    // reach each subscriber in order.
    private void EnqueueDelivery(Action deliver)
    {
        _dispatcher.TryEnqueue(() =>
        {
            try
            {
                deliver();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A subscriber to the changes in root '{RootName}' failed", _handler.RootName);
            }
        });
    }

    // An item exists only when its key resolves. The resolver refuses a link, and a key that differs from the name
    // stored on disk.
    private async Task<ItemState> CheckItemAsync(ResourceKey resource)
    {
        var resolveResult = _handler.Resolve(resource);
        if (resolveResult.IsFailure)
        {
            return ItemState.Gone;
        }
        var path = resolveResult.Value;

        var infoResult = await _fileSystem.GetInfoAsync(path);
        if (infoResult.IsFailure)
        {
            return ItemState.Gone;
        }
        var info = infoResult.Value;

        return info.Kind switch
        {
            StorageItemKind.File => ItemState.File,
            StorageItemKind.Folder => ItemState.Folder,
            _ => ItemState.Gone,
        };
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _isDisposed = true;
            _timer?.Dispose();
            _timer = null;
            _pendingKeys.Clear();
            _pendingKeySet.Clear();
        }
    }
}
