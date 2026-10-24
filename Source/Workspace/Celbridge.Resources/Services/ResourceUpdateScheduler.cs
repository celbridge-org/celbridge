using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

/// <summary>
/// Schedules an update of the resource registry after the project changes, once the changes have been quiet for a
/// short interval. Follows the changes through a subscription to the paths in the search scope, since the registry's
/// tree leaves the excluded paths out.
/// </summary>
public sealed class ResourceUpdateScheduler : IDisposable
{
    private static readonly TimeSpan UpdateDebounceInterval = TimeSpan.FromMilliseconds(250);

    private readonly ILogger<ResourceUpdateScheduler> _logger;
    private readonly IDispatcher _dispatcher;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly TimeProvider _timeProvider;

    private readonly Lock _updateLock = new();

    private IMatcherSubscription? _subscription;
    private ITimer? _updateDebounceTimer;

    // Each update request and each start of a registry update takes the next number, so comparing the two numbers
    // tells which came last.
    private long _updateSequence;
    private long _updateRequestedSequence;
    private long _updateStartedSequence;
    private bool _isDisposed;

    public ResourceUpdateScheduler(
        ILogger<ResourceUpdateScheduler> logger,
        IDispatcher dispatcher,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
        : this(logger, dispatcher, messengerService, workspaceWrapper, TimeProvider.System)
    {
    }

    internal ResourceUpdateScheduler(
        ILogger<ResourceUpdateScheduler> logger,
        IDispatcher dispatcher,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _dispatcher = dispatcher;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Subscribes to the changes in the project that the search scope matches. A change or a resync schedules an
    /// update.
    /// </summary>
    public Result Start(IResourceMonitor monitor, IPathMatcher searchScope)
    {
        if (_isDisposed)
        {
            return Result.Fail("Cannot start a disposed ResourceUpdateScheduler");
        }

        if (_subscription is not null)
        {
            return Result.Fail("The ResourceUpdateScheduler has already started");
        }

        _messengerService.Register<ResourceRegistryUpdateStartingMessage>(this, OnResourceRegistryUpdateStarting);

        var handlers = new ResourceChangeHandlers(
            batch => ScheduleUpdate(),
            resync => ScheduleUpdate());

        var subscribeResult = monitor.SubscribeToMatcher(ResourceKey.DefaultRoot, searchScope, handlers);
        if (subscribeResult.IsFailure)
        {
            return Result.Fail("Failed to subscribe to the changes in the project")
                .WithErrors(subscribeResult);
        }
        _subscription = subscribeResult.Value;

        return Result.Ok();
    }

    /// <summary>
    /// Schedules an update of the registry. Calls that arrive within the quiet interval share one update.
    /// </summary>
    public void ScheduleUpdate()
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return;
        }

        lock (_updateLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _updateRequestedSequence = ++_updateSequence;

            if (_updateDebounceTimer != null)
            {
                _updateDebounceTimer.Change(UpdateDebounceInterval, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _updateDebounceTimer = _timeProvider.CreateTimer(
                    callback: _ => OnDebounceTimerElapsed(),
                    state: null,
                    dueTime: UpdateDebounceInterval,
                    period: Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void OnResourceRegistryUpdateStarting(object recipient, ResourceRegistryUpdateStartingMessage message)
    {
        lock (_updateLock)
        {
            _updateStartedSequence = ++_updateSequence;
        }
    }

    private void OnDebounceTimerElapsed()
    {
        bool alreadyCovered;
        lock (_updateLock)
        {
            _updateDebounceTimer?.Dispose();
            _updateDebounceTimer = null;

            // A read that began after the last request enumerated a file system that already contained
            // whatever those requests described. Mutating commands rebuild the registry themselves, so
            // without this every command is trailed by a redundant watcher-driven rescan.
            alreadyCovered = _updateStartedSequence > _updateRequestedSequence;
        }

        if (alreadyCovered)
        {
            return;
        }

        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return;
        }

        // Marshal to UI thread since UpdateResources may trigger UI updates via message. The workspace
        // can unload before this runs, hence the guard and the catch: an exception escaping an async
        // dispatcher continuation terminates the process.
        _dispatcher.TryEnqueue(async () =>
        {
            try
            {
                if (!_workspaceWrapper.IsWorkspaceLoaded)
                {
                    return;
                }

                var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;

                var result = await resourceService.UpdateResourcesAsync();
                if (result.IsFailure)
                {
                    _logger.LogWarning(result, "Failed to refresh resources");
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "An exception occurred while refreshing resources");
            }
        });
    }

    public void Dispose()
    {
        lock (_updateLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _updateDebounceTimer?.Dispose();
            _updateDebounceTimer = null;
        }

        _messengerService.UnregisterAll(this);
        _subscription?.Dispose();
        _subscription = null;
    }
}
