using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Workspace;
using Timer = System.Timers.Timer;

namespace Celbridge.Resources.Services;

/// <summary>
/// Monitors file system changes across every registered root with the IsWatched
/// capability and schedules resource updates. Each watched root gets its own
/// FileSystemWatcher; all watchers feed into a single shared debounce timer so
/// rapid bursts of events coalesce.
/// </summary>
public class ResourceMonitor : IResourceMonitor, IDisposable
{
    private const int UpdateDebounceMs = 250;

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

    private readonly ILogger<ResourceMonitor> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IDispatcher _dispatcher;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private readonly object _updateLock = new();

    private readonly List<WatchedRoot> _watchedRoots = new();
    private Timer? _updateDebounceTimer;
    private bool _isDisposed;

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
            var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
            foreach (var handler in registry.RootHandlers.Values)
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
            _logger.LogError($"Error occurred while shutting down resource monitor: {ex.Message}");
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

        OnResourceChanged(handler, e.FullPath);

        ScheduleResourceUpdateIfProjectRoot(handler);
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
        _logger.LogError($"File system watcher error: {exception?.Message ?? "Unknown error"}");
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
        _dispatcher.TryEnqueue(() =>
        {
            var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;

            var result = resourceService.UpdateResources();
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
            var message = new MonitoredResourceCreatedMessage(resourceKey);
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
            var message = new MonitoredResourceChangedMessage(resourceKey);
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
            var message = new MonitoredResourceDeletedMessage(resourceKey);
            _messengerService.Send(message);
        });
    }

    private void OnResourceRenamed(IResourceRootHandler handler, string oldFullPath, string newFullPath)
    {
        var oldResourceKey = BuildResourceKey(handler, oldFullPath);
        var newResourceKey = BuildResourceKey(handler, newFullPath);

        if ((oldResourceKey.IsEmpty || newResourceKey.IsEmpty) &&
            handler.RootName == ResourceKey.DefaultRoot)
        {
            return;
        }

        _logger.LogDebug($"Resource renamed: {oldResourceKey} -> {newResourceKey}");

        _dispatcher.TryEnqueue(() =>
        {
            var message = new MonitoredResourceRenamedMessage(oldResourceKey, newResourceKey);
            _messengerService.Send(message);
        });
    }

    private bool ShouldIgnorePath(IResourceRootHandler handler, string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);

        // Cross-platform: Unix hidden files start with '.'
        if (fileName.StartsWith(".") ||
            fileName.StartsWith("~") ||
            fileName.EndsWith(".tmp"))
        {
            return true;
        }

        // Ignore Office temporary files
        // Excel creates temp files with random hex names (e.g., "FED4B600") during save operations
        // Excel also creates lock files like "~$filename.xlsx"
        if (fileName.StartsWith("~$") ||  // Excel lock files
            fileName.StartsWith("~WRL"))   // Word temporary files
        {
            return true;
        }

        // Ignore Python temporary and cache files
        if (fileName.EndsWith(".pyc") ||          // Compiled Python files
            fileName.EndsWith(".pyo") ||          // Optimized Python files
            fileName.EndsWith(".pyd") ||          // Python dynamic modules
            fileName.StartsWith("__pycache__"))   // Python cache directory
        {
            return true;
        }

        // Windows-specific checks
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var attributes = File.GetAttributes(fullPath);
                if ((attributes & System.IO.FileAttributes.Hidden) != 0 ||
                    (attributes & System.IO.FileAttributes.System) != 0)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // File may have been deleted between the event firing and reading attributes.
                // Treat as "ignore" to avoid race condition issues.
                return true;
            }
        }

        // Only the project watcher needs to suppress the visible legacy "celbridge" folder
        // and the new hidden ".celbridge" folder at the project root. Non-project watchers
        // are already scoped inside their own backing location.
        if (handler.RootName == ResourceKey.DefaultRoot)
        {
            var projectFolder = handler.BackingLocation.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var relativePath = fullPath.StartsWith(projectFolder, StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(projectFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : string.Empty;

            if (!string.IsNullOrEmpty(relativePath))
            {
                var firstSegment = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];

                if (firstSegment.Equals(ProjectConstants.MetaDataFolder, StringComparison.OrdinalIgnoreCase) ||
                    firstSegment.Equals(ProjectConstants.CelbridgeFolder, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        // Ignore specific files and folders at any level
        var pathParts = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (pathParts.Any(part => part.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
                                 part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                                 part.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                                 part.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                                 part.Equals("__pycache__", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
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
