using System.Collections.Concurrent;
using Celbridge.Logging;
using Celbridge.Workspace;
using ThreadingTimer = System.Threading.Timer;
using Timer = System.Timers.Timer;

namespace Celbridge.Resources.Services;

/// <summary>
/// Watches each registered root that advertises IsWatched for file system
/// changes and schedules debounced resource updates.
/// </summary>
/// <remarks>
/// Uses FileSystemWatcher directly because the gateway does not surface watcher
/// events, hence the AllowDirectFileSystemAccess exemption.
/// </remarks>
[AllowDirectFileSystemAccess]
public class ResourceMonitor : IResourceMonitor, IDisposable
{
    private const int UpdateDebounceMs = 250;

    // A single File.WriteAllBytes on Windows generates multiple FileSystemWatcher
    // Changed events (truncate, write, close). This per-path trailing-edge
    // debounce coalesces that burst into one settled-state notification. The
    // window also gives the writer time to release the handle before consumers
    // probe size or read content.
    private const int ChangedDebounceMs = 75;

    private sealed class WatchedRoot
    {
        public IResourceRootHandler Handler { get; }
        public FileSystemWatcher Watcher { get; }

        public WatchedRoot(IResourceRootHandler handler, FileSystemWatcher watcher)
        {
            Handler = handler;
            Watcher = watcher;
        }
    }

    // Per-path debounce slot for Changed events. The handler is kept latest-wins
    // because a path is always served by a single root in practice (the watchers
    // are per-root); this just guards against pathological cross-root races.
    private sealed class ChangedDebounceEntry
    {
        public IResourceRootHandler Handler;
        public ThreadingTimer? Timer;

        public ChangedDebounceEntry(IResourceRootHandler handler)
        {
            Handler = handler;
        }
    }

    private readonly ILogger<ResourceMonitor> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IDispatcher _dispatcher;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private readonly object _updateLock = new();

    private readonly List<WatchedRoot> _watchedRoots = new();
    private Timer? _updateDebounceTimer;
    private bool _isDisposed;

    // Case-insensitive: Windows file paths are case-insensitive and watcher
    // events can surface different casings for the same on-disk file.
    private readonly ConcurrentDictionary<string, ChangedDebounceEntry> _changedDebounceEntries =
        new(StringComparer.OrdinalIgnoreCase);

    public ResourceMonitor(
        ILogger<ResourceMonitor> logger,
        IDispatcher dispatcher,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _dispatcher = dispatcher;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
    }

    public Result Initialize()
    {
        if (_isDisposed)
        {
            return Result.Fail("Cannot initialize a disposed ResourceMonitor");
        }

        try
        {
            // Spin up one FileSystemWatcher per registered root that opted in via Capabilities.IsWatched.
            // WorkspaceLoader calls Initialize after the workspace finishes constructing, so the wrapper
            // returns the configured registry instance here.
            var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlerRegistry;
            foreach (var handler in rootHandlerRegistry.RootHandlers.Values)
            {
                if (!handler.Capabilities.IsWatched)
                {
                    continue;
                }

                if (!Directory.Exists(handler.BackingLocation))
                {
                    _logger.LogWarning(
                        $"Backing folder for root '{handler.RootName}' does not exist: {handler.BackingLocation}");
                    continue;
                }

                var watcher = CreateWatcher(handler);
                _watchedRoots.Add(new WatchedRoot(handler, watcher));

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

    private FileSystemWatcher CreateWatcher(IResourceRootHandler handler)
    {
        var watcher = new FileSystemWatcher(handler.BackingLocation)
        {
            NotifyFilter = NotifyFilters.FileName
                         | NotifyFilters.DirectoryName
                         | NotifyFilters.LastWrite
                         | NotifyFilters.Size,
            IncludeSubdirectories = true,
            EnableRaisingEvents = false
        };

        // Closures capture the handler so each event knows which root it belongs to.
        watcher.Created += (sender, e) => OnFileSystemCreated(handler, e);
        watcher.Changed += (sender, e) => OnFileSystemChanged(handler, e);
        watcher.Deleted += (sender, e) => OnFileSystemDeleted(handler, e);
        watcher.Renamed += (sender, e) => OnFileSystemRenamed(handler, e);
        watcher.Error += OnFileSystemError;

        watcher.EnableRaisingEvents = true;
        return watcher;
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

            // Drain any in-flight per-path Changed debounce timers. Dispose
            // before clearing so a timer that fires mid-shutdown finds an
            // empty dict and exits via the TryRemove guard.
            foreach (var pair in _changedDebounceEntries)
            {
                pair.Value.Timer?.Dispose();
            }
            _changedDebounceEntries.Clear();

            foreach (var watchedRoot in _watchedRoots)
            {
                var watcher = watchedRoot.Watcher;
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            _watchedRoots.Clear();

            _logger.LogDebug("Resource monitoring stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while shutting down resource monitor");
        }
    }

    private void OnFileSystemCreated(IResourceRootHandler handler, FileSystemEventArgs e)
    {
        if (ShouldIgnorePath(handler, e.FullPath))
        {
            return;
        }

        // Send granular notification for listeners (e.g., document editors)
        OnResourceCreated(handler, e.FullPath);

        // Also notify as changed because some save patterns (atomic temp-write
        // followed by replace of an existing destination, used by the in-app
        // file writer and many external editors) surface as a Created event on
        // the destination rather than a Changed or Renamed event. Listeners
        // that watch for content changes need to react in that case too.
        OnResourceChanged(handler, e.FullPath);

        ScheduleResourceUpdateIfProjectRoot(handler);
    }

    private void OnFileSystemChanged(IResourceRootHandler handler, FileSystemEventArgs e)
    {
        if (ShouldIgnorePath(handler, e.FullPath))
        {
            return;
        }

        EnqueueChangedEvent(handler, e.FullPath);
    }

    // Per-path trailing-edge debounce: each Changed event resets the timer for
    // its path. When the timer expires we emit a single ResourceChangedMessage
    // for the settled state and schedule the project-tree refresh. Created /
    // Deleted / Renamed are not debounced because they carry distinct semantics
    // and listeners need each one.
    private void EnqueueChangedEvent(IResourceRootHandler handler, string fullPath)
    {
        if (_isDisposed)
        {
            return;
        }

        _changedDebounceEntries.AddOrUpdate(
            fullPath,
            addValueFactory: _ =>
            {
                var entry = new ChangedDebounceEntry(handler);
                entry.Timer = new ThreadingTimer(
                    callback: state => OnChangedDebounceElapsed(fullPath),
                    state: null,
                    dueTime: ChangedDebounceMs,
                    period: Timeout.Infinite);
                return entry;
            },
            updateValueFactory: (_, existing) =>
            {
                existing.Handler = handler;
                existing.Timer?.Change(ChangedDebounceMs, Timeout.Infinite);
                return existing;
            });
    }

    private void OnChangedDebounceElapsed(string fullPath)
    {
        if (!_changedDebounceEntries.TryRemove(fullPath, out var entry))
        {
            return;
        }

        entry.Timer?.Dispose();

        if (_isDisposed)
        {
            return;
        }

        OnResourceChanged(entry.Handler, fullPath);
        ScheduleResourceUpdateIfProjectRoot(entry.Handler);
    }

    private void OnFileSystemDeleted(IResourceRootHandler handler, FileSystemEventArgs e)
    {
        if (ShouldIgnorePath(handler, e.FullPath))
        {
            return;
        }

        OnResourceDeleted(handler, e.FullPath);

        ScheduleResourceUpdateIfProjectRoot(handler);
    }

    private void OnFileSystemRenamed(IResourceRootHandler handler, RenamedEventArgs e)
    {
        // Only check the new path for ignore rules. The old path may no longer exist on disk
        // (the rename has already completed), so File.GetAttributes would throw and cause the
        // event to be incorrectly ignored. This is critical for editors and coding agents that
        // use a "write temp, delete original, rename temp" save pattern.
        if (ShouldIgnorePath(handler, e.FullPath))
        {
            return;
        }

        OnResourceRenamed(handler, e.OldFullPath, e.FullPath);

        // Also notify as changed since content may have been updated
        // (handles applications that use rename as part of save, e.g., Excel)
        OnResourceChanged(handler, e.FullPath);

        ScheduleResourceUpdateIfProjectRoot(handler);
    }

    private void OnFileSystemError(object sender, ErrorEventArgs e)
    {
        var exception = e.GetException();
        if (exception is not null)
        {
            _logger.LogError(exception, "File system watcher error");
        }
        else
        {
            _logger.LogError("File system watcher error (no exception attached)");
        }
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
            var policy = _workspaceWrapper.WorkspaceService.ResourcePolicy;
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
