using Celbridge.Automation;
using Celbridge.Logging;

namespace Celbridge.Resources.Services;

/// <summary>
/// Collects the keys touched in one root, and delivers them in batches to the subscriptions that may want them. A
/// batch closes a fixed interval after the batch's first key, or at once when one of Celbridge's own operations reports
/// changes. At delivery, each key wanted by a subscription is checked on disk, so each change says what is at the key.
/// Batches for one root are delivered one at a time, in order.
/// </summary>
internal sealed class ChangeBatcher : IDisposable
{
    // Long enough to collect the events of one file operation, a save or a checkout, into one batch.
    internal static readonly TimeSpan BatchInterval = TimeSpan.FromMilliseconds(100);

    private sealed record ReachedKeys(ResourceKeySubscription Subscription, IReadOnlyList<ResourceKey> Resources);

    private readonly IResourceRootHandler _handler;
    private readonly ResourceSubscriptions _subscriptions;
    private readonly ILocalFileSystem _fileSystem;
    private readonly IDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly IAutomationService _automationService;
    private readonly ILogger _logger;

    // Guards every field below.
    private readonly object _lock = new();

    // The pending keys in the order they were first touched, each once.
    private List<ResourceKey> _pendingKeys = new();
    private HashSet<ResourceKey> _pendingKeySet = new();
    private long _firstPendingTimestamp;

    // True when the pending keys include a report, which goes out without waiting for the batch interval.
    private bool _isReportPending;

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
        IAutomationService automationService,
        ILogger logger)
    {
        _handler = handler;
        _subscriptions = subscriptions;
        _fileSystem = fileSystem;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
        _automationService = automationService;
        _logger = logger;
    }

    /// <summary>
    /// Records a touched path with the root's resolver when the touch changed a name, then adds the path's key to the
    /// pending batch. An empty key, for a path the batches leave out, is only recorded with the resolver.
    /// </summary>
    public void AddTouchedPath(string fullPath, ResourceKey resource, bool isNameChange)
    {
        // A delivery accepts any listing started after its batch was taken. That listing reflects every name change in
        // the batch only because each change reaches the resolver before its key joins a batch.
        if (isNameChange)
        {
            _handler.RecordNameChanged(fullPath);
        }

        if (!resource.IsEmpty)
        {
            AddTouchedKey(resource);
        }
    }

    // Adds a touched key to the pending batch, each key once. Drops the key when no subscription follows the root.
    private void AddTouchedKey(ResourceKey resource)
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

            AddPendingKey(resource);

            if (_timer is null
                && !_isDelivering)
            {
                ArmTimer(BatchInterval);
            }
        }
    }

    /// <summary>
    /// Adds the keys touched by one of Celbridge's own operations to the pending batch, and delivers the batch
    /// without waiting for the batch interval. The operation has already recorded its changes with the root's
    /// resolver. Drops the keys when no subscription follows the root.
    /// </summary>
    public void AddReportedKeys(IEnumerable<ResourceKey> resources)
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

            foreach (var resource in resources)
            {
                AddPendingKey(resource);
            }

            _isReportPending = true;
            DeliverAtOnce();
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
            DeliverAtOnce();
        }
    }

    // Adds the key to the pending batch, unless the batch contains the key already. Call it under the lock.
    private void AddPendingKey(ResourceKey resource)
    {
        if (!_pendingKeySet.Add(resource))
        {
            return;
        }

        if (_pendingKeys.Count == 0)
        {
            _firstPendingTimestamp = _timeProvider.GetTimestamp();
        }
        _pendingKeys.Add(resource);
    }

    // Starts the delivery of the pending work now. A delivery that is in progress starts the next one as it ends. Call
    // it under the lock.
    private void DeliverAtOnce()
    {
        if (_isDelivering)
        {
            return;
        }

        ArmTimer(TimeSpan.Zero);
    }

    // Arms the timer to deliver the pending work after the due time. The timer is assigned before it is armed, since a
    // timer may fire on this thread before Change returns. The delivery then releases the timer. Call it under the
    // lock.
    private void ArmTimer(TimeSpan dueTime)
    {
        _timer ??= _timeProvider.CreateTimer(
            callback: _ => OnTimerElapsed(),
            state: null,
            dueTime: Timeout.InfiniteTimeSpan,
            period: Timeout.InfiniteTimeSpan);

        _timer.Change(dueTime, Timeout.InfiniteTimeSpan);
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
            _isReportPending = false;
            _isDelivering = true;
        }

        try
        {
            var subscriptions = _subscriptions.GetSubscriptionsFor(_handler.RootName);

            // Created after the batch was taken, when every change in the batch has been recorded with the resolver.
            var itemChecker = new BatchItemChecker(_handler, _fileSystem);

            await DeliverChangesAsync(keys, subscriptions, itemChecker);

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
        if (_isResyncPending
            || _isReportPending)
        {
            ArmTimer(TimeSpan.Zero);
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

        ArmTimer(dueTime);
    }

    private async Task DeliverChangesAsync(
        List<ResourceKey> keys,
        RootSubscriptions subscriptions,
        BatchItemChecker itemChecker)
    {
        if (keys.Count == 0
            || subscriptions.IsEmpty)
        {
            return;
        }

        var startTimestamp = _timeProvider.GetTimestamp();

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

        var itemStates = new Dictionary<ResourceKey, ResourceItemState>();
        var keysToCheck = wantedKeys.Concat(reachedKeys.SelectMany(reached => reached.Resources));
        foreach (var key in keysToCheck)
        {
            if (!itemStates.ContainsKey(key))
            {
                itemStates[key] = await itemChecker.CheckAsync(key);
            }
        }

        foreach (var subscription in subscriptions.MatcherSubscriptions)
        {
            var changes = new List<ResourceChange>();
            foreach (var key in wantedKeys)
            {
                var state = itemStates[key];
                bool mayBeFolder = state != ResourceItemState.File;
                if (subscription.Matches(key.Path, mayBeFolder))
                {
                    changes.Add(new ResourceChange(key, state));
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
                .Select(key => new ResourceChange(key, itemStates[key]))
                .ToList();
            var batch = new ResourceChangeBatch(_handler.RootName, changes);
            EnqueueDelivery(() => reached.Subscription.DeliverChanges(batch));
        }

        var elapsed = (long)_timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;
        _automationService.LogEvent(new ResourceChangesDeliveredEvent(_handler.RootName, keys.Count, elapsed));
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
