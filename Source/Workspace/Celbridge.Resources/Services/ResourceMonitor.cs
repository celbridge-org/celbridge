using Celbridge.Automation;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

/// <summary>
/// Watches the roots that opt in, and delivers the keys that each change touches to the subscriptions of the root. The
/// keys reported by Celbridge's own operations are delivered at once.
/// </summary>
public class ResourceMonitor : IResourceMonitor, IDisposable
{
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
    private readonly IDispatcher _dispatcher;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IFileSystemMonitorFactory _fileSystemMonitorFactory;
    private readonly ILocalFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;
    private readonly IAutomationService _automationService;

    private readonly List<WatchedRoot> _watchedRoots = new();

    private readonly ResourceSubscriptions _subscriptions = new();

    // One batcher for each watched root, by root name. Guarded by the lock on the dictionary.
    private readonly Dictionary<string, ChangeBatcher> _batchers = new(StringComparer.Ordinal);

    private bool _isDisposed;

    public ResourceMonitor(
        ILogger<ResourceMonitor> logger,
        IDispatcher dispatcher,
        IWorkspaceWrapper workspaceWrapper,
        IFileSystemMonitorFactory fileSystemMonitorFactory,
        ILocalFileSystem fileSystem,
        IAutomationService automationService)
        : this(
            logger,
            dispatcher,
            workspaceWrapper,
            fileSystemMonitorFactory,
            fileSystem,
            automationService,
            TimeProvider.System)
    {
    }

    internal ResourceMonitor(
        ILogger<ResourceMonitor> logger,
        IDispatcher dispatcher,
        IWorkspaceWrapper workspaceWrapper,
        IFileSystemMonitorFactory fileSystemMonitorFactory,
        ILocalFileSystem fileSystem,
        IAutomationService automationService,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _dispatcher = dispatcher;
        _workspaceWrapper = workspaceWrapper;
        _fileSystemMonitorFactory = fileSystemMonitorFactory;
        _fileSystem = fileSystem;
        _timeProvider = timeProvider;
        _automationService = automationService;
    }

    public Result Initialize()
    {
        if (_isDisposed)
        {
            return Result.Fail("Cannot initialize a disposed ResourceMonitor");
        }

        try
        {
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

    public void ReportOperationChanges(IReadOnlyList<ResourceKey> resources)
    {
        if (_isDisposed)
        {
            return;
        }

        foreach (var rootResources in resources.GroupBy(resource => resource.Root))
        {
            ChangeBatcher? batcher;
            lock (_batchers)
            {
                _batchers.TryGetValue(rootResources.Key, out batcher);
            }

            if (batcher is null)
            {
                continue;
            }

            var batchedResources = rootResources
                .Where(IsBatched)
                .ToList();
            if (batchedResources.Count > 0)
            {
                batcher.AddReportedKeys(batchedResources);
            }
        }
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
                batcher = new ChangeBatcher(
                    handler,
                    _subscriptions,
                    _fileSystem,
                    _dispatcher,
                    _timeProvider,
                    _automationService,
                    _logger);
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
        // Each change to a name reaches the resolver, ignored paths included. A changed event leaves the listings alone,
        // since only a change to a name alters a listing.
        bool isNameChange = monitorEvent.Kind != FileSystemMonitorEventKind.Changed;

        batcher.AddTouchedPath(monitorEvent.Path, GetBatchedKey(handler, monitorEvent.Path), isNameChange);
        if (monitorEvent.OldPath is not null)
        {
            batcher.AddTouchedPath(monitorEvent.OldPath, GetBatchedKey(handler, monitorEvent.OldPath), isNameChange);
        }
    }

    // Returns the key that a touched path adds to the root's batch. The empty key is returned for the root folder
    // itself, for a path in the reserved .git folder, and for a path outside every valid key.
    private ResourceKey GetBatchedKey(IResourceRootHandler handler, string fullPath)
    {
        var keyResult = handler.GetResourceKey(fullPath);
        if (keyResult.IsFailure)
        {
            return ResourceKey.Empty;
        }
        var resource = keyResult.Value;

        return IsBatched(resource)
            ? resource
            : ResourceKey.Empty;
    }

    // Returns false for the root key itself, and for a key in the reserved .git folder.
    private bool IsBatched(ResourceKey resource)
    {
        if (resource.IsEmpty)
        {
            return false;
        }

        var policy = _workspaceWrapper.WorkspaceService.ResourceService.Policy;
        var policyResult = policy.Evaluate(resource, ResourceAction.Read);

        return policyResult.IsSuccess;
    }

    // The monitor lost events, so which resources were missed is unknown. The path cache is dropped, since one of the
    // missing events may have replaced a folder with a reparse point. Another may have added a name the listings lack.
    // Each subscriber of the root receives a resync in place of the lost events.
    private void OnMonitoringDesynchronized(IResourceRootHandler handler, ChangeBatcher batcher)
    {
        _logger.LogWarning(
            "Resource monitoring for root '{RootName}' lost events; resyncing its subscribers", handler.RootName);
        _automationService.LogEvent(new ResourceEventsLostEvent(handler.RootName));

        handler.InvalidatePathCache();

        batcher.RequestResync();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Shutdown();
        _isDisposed = true;
    }
}
