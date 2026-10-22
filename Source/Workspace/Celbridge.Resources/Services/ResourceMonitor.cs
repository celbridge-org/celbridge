using System.Diagnostics;
using Celbridge.Logging;
using Celbridge.Workspace;
using Timer = System.Timers.Timer;

namespace Celbridge.Resources.Services;

/// <summary>
/// Watches the roots that opt in, and delivers the keys that each change touches to the subscriptions of the root.
/// Also translates the watcher's events into resource change messages, and schedules registry updates.
/// </summary>
public class ResourceMonitor : IResourceMonitor, IDisposable
{
    private const int UpdateDebounceMs = 250;

    private sealed class WatchedRoot
    {
        public IResourceRootHandler Handler { get; }
        public IFileSystemMonitor Monitor { get; }

        public WatchedRoot(IResourceRootHandler handler, IFileSystemMonitor monitor)
        {
            Handler = handler;
            Monitor = monitor;
        }
    }

    private readonly ILogger<ResourceMonitor> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IDispatcher _dispatcher;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IFileSystemMonitorFactory _fileSystemMonitorFactory;
    private readonly ILocalFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;

    private readonly object _updateLock = new();

    private readonly List<WatchedRoot> _watchedRoots = new();

    private readonly ResourceSubscriptions _subscriptions = new();

    // One batcher for each watched root, by root name. Guarded by the lock on the dictionary.
    private readonly Dictionary<string, ChangeBatcher> _batchers = new(StringComparer.Ordinal);

    private Timer? _updateDebounceTimer;
    private long _updateRequestedTimestamp;
    private long _updateStartedTimestamp;
    private bool _isDisposed;

    public ResourceMonitor(
        ILogger<ResourceMonitor> logger,
        IDispatcher dispatcher,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        IFileSystemMonitorFactory fileSystemMonitorFactory,
        ILocalFileSystem fileSystem)
        : this(
            logger,
            dispatcher,
            messengerService,
            workspaceWrapper,
            fileSystemMonitorFactory,
            fileSystem,
            TimeProvider.System)
    {
    }

    internal ResourceMonitor(
        ILogger<ResourceMonitor> logger,
        IDispatcher dispatcher,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        IFileSystemMonitorFactory fileSystemMonitorFactory,
        ILocalFileSystem fileSystem,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _dispatcher = dispatcher;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _fileSystemMonitorFactory = fileSystemMonitorFactory;
        _fileSystem = fileSystem;
        _timeProvider = timeProvider;
    }

    public Result Initialize()
    {
        if (_isDisposed)
        {
            return Result.Fail("Cannot initialize a disposed ResourceMonitor");
        }

        try
        {
            _messengerService.Register<ResourceRegistryUpdateStartingMessage>(this, OnResourceRegistryUpdateStarting);

            // Spin up one file system monitor per registered root that opted in via Capabilities.IsWatched.
            // WorkspaceLoader calls Initialize after the workspace finishes constructing, so the wrapper
            // returns the configured registry instance here.
            var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;
            foreach (var handler in rootHandlerRegistry.RootHandlers.Values)
            {
                if (!handler.Capabilities.IsWatched)
                {
                    continue;
                }

                var batcher = GetOrCreateBatcher(handler);
                var monitor = _fileSystemMonitorFactory.Create(handler.BackingLocation);
                monitor.FileSystemChanged +=
                    (sender, monitorEvent) => OnFileSystemChanged(handler, batcher, monitorEvent);
                monitor.MonitoringDesynchronized += (sender, args) => OnMonitoringDesynchronized(handler, batcher);

                var startResult = monitor.Start();
                if (startResult.IsFailure)
                {
                    _logger.LogWarning(startResult,
                        $"Resource monitoring not started for root '{handler.RootName}'");
                    monitor.Dispose();
                    continue;
                }

                _watchedRoots.Add(new WatchedRoot(handler, monitor));

                _logger.LogDebug(
                    $"Resource monitoring started for root '{handler.RootName}' at: {handler.BackingLocation}");
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail("Failed to initialize resource monitor")
                .WithException(ex);
        }
    }

    public Result<IMatcherSubscription> SubscribeToMatcher(
        string rootName,
        IPathMatcher matcher,
        ResourceChangeHandlers handlers)
    {
        if (_isDisposed)
        {
            return Result.Fail("Cannot subscribe to a disposed ResourceMonitor");
        }

        var rootHandlers = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers.RootHandlers;
        if (!rootHandlers.ContainsKey(rootName))
        {
            return Result.Fail($"No resource root is registered with the name '{rootName}'");
        }

        var subscription = new MatcherSubscription(rootName, matcher, handlers, _subscriptions.Remove);
        _subscriptions.Add(subscription);

        return subscription.OkResult<IMatcherSubscription>();
    }

    public Result<IResourceKeySubscription> SubscribeToResources(
        IReadOnlyList<ResourceKey> resources,
        ResourceChangeHandlers handlers)
    {
        if (_isDisposed)
        {
            return Result.Fail("Cannot subscribe to a disposed ResourceMonitor");
        }

        var validateResult = ValidateSubscribedResources(resources);
        if (validateResult.IsFailure)
        {
            return Result.Fail(validateResult);
        }

        var subscription = new ResourceKeySubscription(resources, handlers, _subscriptions.Remove);
        _subscriptions.Add(subscription);

        return subscription.OkResult<IResourceKeySubscription>();
    }

    // Accepts keys that each name an item below a registered root.
    private Result ValidateSubscribedResources(IReadOnlyList<ResourceKey> resources)
    {
        var rootHandlers = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers.RootHandlers;
        foreach (var resource in resources)
        {
            if (resource.IsEmpty)
            {
                return Result.Fail($"A subscribed key must name an item below its root: '{resource}'");
            }

            if (!rootHandlers.ContainsKey(resource.Root))
            {
                return Result.Fail($"No resource root is registered with the name '{resource.Root}'");
            }
        }

        return Result.Ok();
    }

    public void Shutdown()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            _messengerService.UnregisterAll(this);

            lock (_updateLock)
            {
                _updateDebounceTimer?.Dispose();
                _updateDebounceTimer = null;
            }

            foreach (var watchedRoot in _watchedRoots)
            {
                watchedRoot.Monitor.Dispose();
            }
            _watchedRoots.Clear();

            lock (_batchers)
            {
                foreach (var batcher in _batchers.Values)
                {
                    batcher.Dispose();
                }
                _batchers.Clear();
            }

            _subscriptions.Clear();

            _logger.LogDebug("Resource monitoring stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while shutting down resource monitor");
        }
    }

    private ChangeBatcher GetOrCreateBatcher(IResourceRootHandler handler)
    {
        ChangeBatcher? batcher;
        lock (_batchers)
        {
            if (!_batchers.TryGetValue(handler.RootName, out batcher))
            {
                batcher = new ChangeBatcher(handler, _subscriptions, _fileSystem, _dispatcher, _timeProvider, _logger);
                _batchers[handler.RootName] = batcher;
            }
        }

        return batcher;
    }

    private void OnFileSystemChanged(
        IResourceRootHandler handler,
        ChangeBatcher batcher,
        FileSystemMonitorEvent monitorEvent)
    {
        // The resolver learns of each change to a name before anything is batched, ignored paths included. A changed
        // event leaves the listings alone, since only a change to a name alters a listing.
        if (monitorEvent.Kind != FileSystemMonitorEventKind.Changed)
        {
            handler.RecordNameChanged(monitorEvent.Path);
            if (monitorEvent.OldPath is not null)
            {
                handler.RecordNameChanged(monitorEvent.OldPath);
            }
        }

        AddTouchedPath(handler, batcher, monitorEvent.Path);
        if (monitorEvent.OldPath is not null)
        {
            AddTouchedPath(handler, batcher, monitorEvent.OldPath);
        }

        switch (monitorEvent.Kind)
        {
            case FileSystemMonitorEventKind.Created:
                HandleCreated(handler, monitorEvent.Path);
                break;

            case FileSystemMonitorEventKind.Changed:
                HandleChanged(handler, monitorEvent.Path);
                break;

            case FileSystemMonitorEventKind.Deleted:
                HandleDeleted(handler, monitorEvent.Path);
                break;

            case FileSystemMonitorEventKind.Renamed:
                HandleRenamed(handler, monitorEvent.OldPath!, monitorEvent.Path);
                break;
        }
    }

    // Adds the key of a touched path to the root's batch. A path that cannot be keyed, the root folder itself and a
    // path in the reserved .git folder are dropped.
    private void AddTouchedPath(IResourceRootHandler handler, ChangeBatcher batcher, string fullPath)
    {
        var keyResult = handler.GetResourceKey(fullPath);
        if (keyResult.IsFailure)
        {
            return;
        }
        var resource = keyResult.Value;

        if (resource.IsEmpty)
        {
            return;
        }

        var policy = _workspaceWrapper.WorkspaceService.ResourceService.Policy;
        var policyResult = policy.Evaluate(resource, ResourceAction.Read);
        if (policyResult.IsFailure)
        {
            return;
        }

        batcher.AddTouchedKey(resource);
    }

    private void HandleCreated(IResourceRootHandler handler, string fullPath)
    {
        if (ShouldIgnorePath(handler, fullPath))
        {
            return;
        }

        // Send granular notification for listeners (e.g., document editors)
        OnResourceCreated(handler, fullPath);

        // Also notify as changed because some save patterns (atomic temp-write
        // followed by replace of an existing destination, used by the in-app
        // file writer and many external editors) surface as a Created event on
        // the destination rather than a Changed or Renamed event. Listeners
        // that watch for content changes need to react in that case too.
        OnResourceChanged(handler, fullPath);

        ScheduleResourceUpdate(handler);
    }

    private void HandleChanged(IResourceRootHandler handler, string fullPath)
    {
        if (ShouldIgnorePath(handler, fullPath))
        {
            return;
        }

        OnResourceChanged(handler, fullPath);

        ScheduleResourceUpdate(handler);
    }

    private void HandleDeleted(IResourceRootHandler handler, string fullPath)
    {
        if (ShouldIgnorePath(handler, fullPath))
        {
            return;
        }

        OnResourceDeleted(handler, fullPath);

        ScheduleResourceUpdate(handler);
    }

    private void HandleRenamed(IResourceRootHandler handler, string oldFullPath, string newFullPath)
    {
        // Only check the new path for ignore rules. The old path may no longer exist on disk
        // (the rename has already completed), so File.GetAttributes would throw and cause the
        // event to be incorrectly ignored. This is critical for editors and coding agents that
        // use a "write temp, delete original, rename temp" save pattern.
        if (ShouldIgnorePath(handler, newFullPath))
        {
            return;
        }

        OnResourceRenamed(handler, oldFullPath, newFullPath);

        // Also notify as changed since content may have been updated
        // (handles applications that use rename as part of save, e.g., Excel)
        OnResourceChanged(handler, newFullPath);

        ScheduleResourceUpdate(handler);
    }

    private void OnResourceRegistryUpdateStarting(object recipient, ResourceRegistryUpdateStartingMessage message)
    {
        lock (_updateLock)
        {
            _updateStartedTimestamp = Stopwatch.GetTimestamp();
        }
    }

    // The monitor lost events, so which resources were missed is exactly what is unknown: the tree is
    // rescanned unconditionally, and the path cache is dropped because one of the missing events may
    // have been a folder being replaced by a reparse point, or a name missing from the listings. Each
    // subscriber of the root receives a resync in place of the lost events.
    private void OnMonitoringDesynchronized(IResourceRootHandler handler, ChangeBatcher batcher)
    {
        _logger.LogWarning(
            "Resource monitoring for root '{RootName}' lost events; rescanning", handler.RootName);

        handler.InvalidatePathCache();

        batcher.RequestResync();

        ScheduleResourceUpdate(handler);
    }

    // Schedules an update prompted by an event on a root. The project tree sync
    // (Registry.UpdateResourceRegistry) is project-scoped, so an event on any other root (temp:, utils:)
    // does not touch the tree and schedules nothing.
    private void ScheduleResourceUpdate(IResourceRootHandler handler)
    {
        if (handler.RootName == ResourceKey.DefaultRoot)
        {
            ScheduleResourceUpdate();
        }
    }

    public void ScheduleResourceUpdate()
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return;
        }

        lock (_updateLock)
        {
            _updateRequestedTimestamp = Stopwatch.GetTimestamp();

            if (_updateDebounceTimer != null)
            {
                // Timer already running - reset it
                _updateDebounceTimer.Stop();
                _updateDebounceTimer.Start();
            }
            else
            {
                // Start new debounce timer
                _updateDebounceTimer = new Timer(UpdateDebounceMs);
                _updateDebounceTimer.AutoReset = false;
                _updateDebounceTimer.Elapsed += OnDebounceTimerElapsed;
                _updateDebounceTimer.Start();
            }
        }
    }

    private void OnDebounceTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        bool alreadyCovered;
        lock (_updateLock)
        {
            _updateDebounceTimer?.Dispose();
            _updateDebounceTimer = null;

            // A read that began after the last request enumerated a file system that already contained
            // whatever those requests described. Mutating commands rebuild the registry themselves, so
            // without this every command is trailed by a redundant watcher-driven rescan.
            alreadyCovered = _updateStartedTimestamp > _updateRequestedTimestamp;
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

    private void OnResourceCreated(IResourceRootHandler handler, string fullPath)
    {
        var resourceKey = BuildResourceKey(handler, fullPath);
        if (resourceKey.IsEmpty &&
            handler.RootName == ResourceKey.DefaultRoot)
        {
            // Project root with no path means the event was outside the backing folder.
            // For non-project roots, a root-only key is unusual but legal; only flag the project case.
            return;
        }

        _logger.LogDebug($"Resource created: {resourceKey}");

        _dispatcher.TryEnqueue(() =>
        {
            var message = new ResourceCreatedMessage(resourceKey);
            _messengerService.Send(message);
        });
    }

    private void OnResourceChanged(IResourceRootHandler handler, string fullPath)
    {
        var resourceKey = BuildResourceKey(handler, fullPath);
        if (resourceKey.IsEmpty &&
            handler.RootName == ResourceKey.DefaultRoot)
        {
            return;
        }

        _logger.LogDebug($"Resource changed: {resourceKey}");

        _dispatcher.TryEnqueue(() =>
        {
            var message = new ResourceChangedMessage(resourceKey);
            _messengerService.Send(message);
        });
    }

    private void OnResourceDeleted(IResourceRootHandler handler, string fullPath)
    {
        var resourceKey = BuildResourceKey(handler, fullPath);
        if (resourceKey.IsEmpty &&
            handler.RootName == ResourceKey.DefaultRoot)
        {
            return;
        }

        _logger.LogDebug($"Resource deleted: {resourceKey}");

        _dispatcher.TryEnqueue(() =>
        {
            var message = new ResourceDeletedMessage(resourceKey);
            _messengerService.Send(message);
        });
    }

    private void OnResourceRenamed(IResourceRootHandler handler, string oldFullPath, string newFullPath)
    {
        var oldResourceKey = BuildResourceKey(handler, oldFullPath);
        var newResourceKey = BuildResourceKey(handler, newFullPath);

        if ((oldResourceKey.IsEmpty || newResourceKey.IsEmpty)
            && handler.RootName == ResourceKey.DefaultRoot)
        {
            return;
        }

        _logger.LogDebug($"Resource renamed: {oldResourceKey} -> {newResourceKey}");

        _dispatcher.TryEnqueue(() =>
        {
            var message = new ResourceRenamedMessage(oldResourceKey, newResourceKey);
            _messengerService.Send(message);
        });
    }

    // Drops a watcher event when its path cannot be keyed, when a project-root key
    // names a reserved path, or when it names a path the tree walk skips. Events from
    // non-project roots are already confined to their backing folder and need no check.
    private bool ShouldIgnorePath(IResourceRootHandler handler, string fullPath)
    {
        var keyResult = handler.GetResourceKey(fullPath);
        if (keyResult.IsFailure)
        {
            return true;
        }

        if (handler.RootName != ResourceKey.DefaultRoot)
        {
            return false;
        }

        var resource = keyResult.Value;
        var policy = _workspaceWrapper.WorkspaceService.ResourceService.Policy;

        var policyResult = policy.Evaluate(resource, ResourceAction.Read, isFolder: false);
        if (policyResult.IsFailure)
        {
            return true;
        }

        // Search scope bounds the tree walk until the resource index lands, so a write inside
        // an excluded folder can only schedule a rebuild that produces the same tree. This
        // check retires with that bound.
        return policy.IsSearchExcluded(resource, isFolder: false);
    }

    private ResourceKey BuildResourceKey(IResourceRootHandler handler, string fullPath)
    {
        var result = handler.GetResourceKey(fullPath);
        if (result.IsFailure)
        {
            _logger.LogWarning($"Could not build resource key for path: {fullPath} ({result.FirstErrorMessage})");
            return ResourceKey.Empty;
        }
        return result.Value;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                Shutdown();
            }

            _isDisposed = true;
        }
    }

    ~ResourceMonitor()
    {
        Dispose(false);
    }
}
