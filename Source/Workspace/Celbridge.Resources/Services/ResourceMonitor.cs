using Celbridge.Logging;
using Celbridge.Workspace;
using Timer = System.Timers.Timer;

namespace Celbridge.Resources.Services;

/// <summary>
/// Watches each registered root that advertises IsWatched for file system
/// changes and schedules debounced resource updates.
/// </summary>
/// <remarks>
/// Wraps one IFileSystemMonitor per watched root for the raw substrate events
/// and keeps the domain half: path-to-ResourceKey mapping, policy filtering,
/// messenger dispatch, and the project-tree registry debounce.
/// </remarks>
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

    private readonly object _updateLock = new();

    private readonly List<WatchedRoot> _watchedRoots = new();
    private Timer? _updateDebounceTimer;
    private bool _isDisposed;

    public ResourceMonitor(
        ILogger<ResourceMonitor> logger,
        IDispatcher dispatcher,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        IFileSystemMonitorFactory fileSystemMonitorFactory)
    {
        _logger = logger;
        _dispatcher = dispatcher;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _fileSystemMonitorFactory = fileSystemMonitorFactory;
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
            var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlerRegistry;
            foreach (var handler in rootHandlerRegistry.RootHandlers.Values)
            {
                if (!handler.Capabilities.IsWatched)
                {
                    continue;
                }

                var monitor = _fileSystemMonitorFactory.Create(handler.BackingLocation);
                monitor.FileSystemChanged += (sender, monitorEvent) => OnFileSystemChanged(handler, monitorEvent);

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

    public void Shutdown()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
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

            _logger.LogDebug("Resource monitoring stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while shutting down resource monitor");
        }
    }

    private void OnFileSystemChanged(IResourceRootHandler handler, FileSystemMonitorEvent monitorEvent)
    {
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

        ScheduleResourceUpdateIfProjectRoot(handler);
    }

    private void HandleChanged(IResourceRootHandler handler, string fullPath)
    {
        if (ShouldIgnorePath(handler, fullPath))
        {
            return;
        }

        OnResourceChanged(handler, fullPath);

        ScheduleResourceUpdateIfProjectRoot(handler);
    }

    private void HandleDeleted(IResourceRootHandler handler, string fullPath)
    {
        if (ShouldIgnorePath(handler, fullPath))
        {
            return;
        }

        OnResourceDeleted(handler, fullPath);

        ScheduleResourceUpdateIfProjectRoot(handler);
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

        ScheduleResourceUpdateIfProjectRoot(handler);
    }

    private void ScheduleResourceUpdateIfProjectRoot(IResourceRootHandler handler)
    {
        // The project tree sync (Registry.UpdateResourceRegistry) is project-scoped.
        // Events from non-project roots (temp:, logs:) don't touch the project tree,
        // so they skip the debounce.
        if (handler.RootName == ResourceKey.DefaultRoot)
        {
            ScheduleResourceUpdate();
        }
    }

    public void ScheduleResourceUpdate()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return;
        }

        lock (_updateLock)
        {
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
        lock (_updateLock)
        {
            _updateDebounceTimer?.Dispose();
            _updateDebounceTimer = null;
        }

        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return;
        }

        // Marshal to UI thread since UpdateResources may trigger UI updates via message
        _dispatcher.TryEnqueue(async () =>
        {
            var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;

            var result = await resourceService.UpdateResourcesAsync();
            if (result.IsFailure)
            {
                _logger.LogWarning(result, "Failed to refresh resources");
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

    // Drops a watcher event when its path cannot be keyed, or when a project-root
    // key is denied List by the policy engine. Events from non-project roots are
    // already confined to their backing folder and need no policy check.
    private bool ShouldIgnorePath(IResourceRootHandler handler, string fullPath)
    {
        var keyResult = handler.GetResourceKey(fullPath);
        if (keyResult.IsFailure)
        {
            return true;
        }

        if (handler.RootName == ResourceKey.DefaultRoot)
        {
            var policy = _workspaceWrapper.WorkspaceService.ResourceService.Policy;
            var policyResult = policy.Evaluate(keyResult.Value, ResourceAction.List, isFolder: false);
            if (policyResult.IsFailure)
            {
                return true;
            }
        }

        return false;
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
