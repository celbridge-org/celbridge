using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Projects;

namespace Celbridge.Explorer.Services;

/// <summary>
/// Monitors file system changes in the project folder and reports changes to resources.
/// </summary>
public class ResourceChangeMonitor : IDisposable
{
    private readonly ILogger<ResourceChangeMonitor> _logger;
    private readonly ICommandService _commandService;
    private readonly IProjectService _projectService;
    private readonly IMessengerService _messengerService;
    private readonly IDispatcher _dispatcher;
    private readonly string _projectFolderPath;

    private FileSystemWatcher? _fileSystemWatcher;
    private readonly System.Timers.Timer _debounceTimer;
    private readonly object _lock = new();
    
    // Track pending changes for debouncing
    private readonly HashSet<string> _pendingCreated = new();
    private readonly HashSet<string> _pendingChanged = new();
    private readonly HashSet<string> _pendingDeleted = new();
    private readonly Dictionary<string, string> _pendingRenamed = new();

    private const int DebounceDelayMs = 500; // Wait 500ms after last change before processing
    private bool _isDisposed;

    public ResourceChangeMonitor(
        ILogger<ResourceChangeMonitor> logger,
        ICommandService commandService,
        IProjectService projectService,
        IMessengerService messengerService,
        IDispatcher dispatcher)
    {
        _logger = logger;
        _commandService = commandService;
        _projectService = projectService;
        _messengerService = messengerService;
        _dispatcher = dispatcher;

        var project = _projectService.CurrentProject;
        Guard.IsNotNull(project);

        _projectFolderPath = project.ProjectFolderPath;

        _debounceTimer = new System.Timers.Timer(DebounceDelayMs);
        _debounceTimer.AutoReset = false;
        _debounceTimer.Elapsed += OnDebounceTimerElapsed;
    }

    public Result Initialize()
    {
        if (_isDisposed)
        {
            return Result.Fail("Cannot initialize a disposed ResourceChangeMonitor");
        }

        try
        {
            if (!Directory.Exists(_projectFolderPath))
            {
                return Result.Fail($"Project folder does not exist: {_projectFolderPath}");
            }

            /// Start monitoring the project folder for file system changes.
            _fileSystemWatcher = new FileSystemWatcher(_projectFolderPath)
            {
                NotifyFilter = NotifyFilters.FileName 
                             | NotifyFilters.DirectoryName 
                             | NotifyFilters.LastWrite 
                             | NotifyFilters.Size,
                IncludeSubdirectories = true,
                EnableRaisingEvents = false
            };

            _fileSystemWatcher.Created += OnFileSystemCreated;
            _fileSystemWatcher.Changed += OnFileSystemChanged;
            _fileSystemWatcher.Deleted += OnFileSystemDeleted;
            _fileSystemWatcher.Renamed += OnFileSystemRenamed;
            _fileSystemWatcher.Error += OnFileSystemError;

            // Start disabled, enable after setup
            _fileSystemWatcher.EnableRaisingEvents = true;

            _logger.LogDebug($"Resource change monitoring started for: {_projectFolderPath}");

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail("Failed to initialize resource change monitor")
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
            _debounceTimer.Stop();

            if (_fileSystemWatcher != null)
            {
                _fileSystemWatcher.EnableRaisingEvents = false;
                _fileSystemWatcher.Created -= OnFileSystemCreated;
                _fileSystemWatcher.Changed -= OnFileSystemChanged;
                _fileSystemWatcher.Deleted -= OnFileSystemDeleted;
                _fileSystemWatcher.Renamed -= OnFileSystemRenamed;
                _fileSystemWatcher.Error -= OnFileSystemError;
                _fileSystemWatcher.Dispose();
                _fileSystemWatcher = null;
            }

            lock (_lock)
            {
                _pendingCreated.Clear();
                _pendingChanged.Clear();
                _pendingDeleted.Clear();
                _pendingRenamed.Clear();
            }

            _logger.LogDebug($"Resource change monitoring stopped for: {_projectFolderPath}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error occurred while shutting down resource change monitor: {ex.Message}");
        }
    }

    #region FileSystemWatcher Event Handlers

    private void OnFileSystemCreated(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnorePath(e.FullPath))
        {
            return;
        }

        lock (_lock)
        {
            _pendingCreated.Add(e.FullPath);
            RestartDebounceTimer();
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnorePath(e.FullPath))
        {
            return;
        }

        lock (_lock)
        {
            _pendingChanged.Add(e.FullPath);
            RestartDebounceTimer();
        }
    }

    private void OnFileSystemDeleted(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnorePath(e.FullPath))
        {
            return;
        }

        lock (_lock)
        {
            _pendingDeleted.Add(e.FullPath);
            RestartDebounceTimer();
        }
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        if (ShouldIgnorePath(e.FullPath) || ShouldIgnorePath(e.OldFullPath))
        {
            return;
        }

        lock (_lock)
        {
            _pendingRenamed[e.OldFullPath] = e.FullPath;
            
            // Also track as a changed file since the content may have been updated
            // This handles applications that use rename operations as part of their save process
            _pendingChanged.Add(e.FullPath);
            
            RestartDebounceTimer();
        }
    }

    private void OnFileSystemError(object sender, ErrorEventArgs e)
    {
        var exception = e.GetException();
        _logger.LogError($"File system watcher error: {exception?.Message ?? "Unknown error"}");
    }

    #endregion

    #region Debouncing

    private void RestartDebounceTimer()
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnDebounceTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        ProcessPendingChanges();
    }

    private void ProcessPendingChanges()
    {
        HashSet<string> created;
        HashSet<string> changed;
        HashSet<string> deleted;
        Dictionary<string, string> renamed;

        lock (_lock)
        {
            // Copy pending changes and clear the collections
            created = new HashSet<string>(_pendingCreated);
            changed = new HashSet<string>(_pendingChanged);
            deleted = new HashSet<string>(_pendingDeleted);
            renamed = new Dictionary<string, string>(_pendingRenamed);

            _pendingCreated.Clear();
            _pendingChanged.Clear();
            _pendingDeleted.Clear();
            _pendingRenamed.Clear();
        }

        bool updateResourceRegistry = false;

        // Process all changes
        foreach (var path in created)
        {
            OnResourceCreated(path);
            updateResourceRegistry = true;
        }

        foreach (var path in changed)
        {
            OnResourceChanged(path);
        }

        foreach (var path in deleted)
        {
            OnResourceDeleted(path);
            updateResourceRegistry = true;
        }

        foreach (var kvp in renamed)
        {
            OnResourceRenamed(kvp.Key, kvp.Value);
            updateResourceRegistry = true;
        }

        if (updateResourceRegistry)
        {
            _commandService.Execute<IUpdateResourcesCommand>();
        }
    }

    #endregion

    #region Change Processing Methods

    private void OnResourceCreated(string fullPath)
    {
        var resourceKey = GetResourceKey(fullPath);
        if (resourceKey.IsEmpty)
        {
            // Path is not in project folder - this shouldn't happen due to ShouldIgnorePath checks
            return;
        }
        
        _logger.LogDebug($"Resource created: {resourceKey}");
        
        _dispatcher.TryEnqueue(() =>
        {
            var message = new MonitoredResourceCreatedMessage(resourceKey);
            _messengerService.Send(message);
        });
    }

    private void OnResourceChanged(string fullPath)
    {
        var resourceKey = GetResourceKey(fullPath);
        if (resourceKey.IsEmpty)
        {
            // Path is not in project folder - this shouldn't happen due to ShouldIgnorePath checks
            return;
        }
        
        _logger.LogDebug($"Resource changed: {resourceKey}");
        
        _dispatcher.TryEnqueue(() =>
        {
            var message = new MonitoredResourceChangedMessage(resourceKey);
            _messengerService.Send(message);
        });
    }

    private void OnResourceDeleted(string fullPath)
    {
        var resourceKey = GetResourceKey(fullPath);
        if (resourceKey.IsEmpty)
        {
            // Path is not in project folder - this shouldn't happen due to ShouldIgnorePath checks
            return;
        }
        
        _logger.LogDebug($"Resource deleted: {resourceKey}");
        
        _dispatcher.TryEnqueue(() =>
        {
            var message = new MonitoredResourceDeletedMessage(resourceKey);
            _messengerService.Send(message);
        });
    }

    private void OnResourceRenamed(string oldFullPath, string newFullPath)
    {
        var oldResourceKey = GetResourceKey(oldFullPath);
        var newResourceKey = GetResourceKey(newFullPath);
        
        if (oldResourceKey.IsEmpty || newResourceKey.IsEmpty)
        {
            // One or both paths are not in project folder - this shouldn't happen due to ShouldIgnorePath checks
            return;
        }
        
        _logger.LogDebug($"Resource renamed: {oldResourceKey} -> {newResourceKey}");
        
        _dispatcher.TryEnqueue(() =>
        {
            var message = new MonitoredResourceRenamedMessage(oldResourceKey, newResourceKey);
            _messengerService.Send(message);
        });
    }

    #endregion

    #region Helper Methods

    private bool ShouldIgnorePath(string fullPath)
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
                if (File.Exists(fullPath))
                {
                    var attributes = File.GetAttributes(fullPath);
                    if ((attributes & System.IO.FileAttributes.Hidden) != 0 ||
                        (attributes & System.IO.FileAttributes.System) != 0)
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // File may have been deleted between the existence check and attribute read
                // Treat as "ignore" to avoid race condition issues
                return true;
            }
        }
        
        // Check if path is directly under project root and matches ignored folders
        var projectFolder = _projectFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var relativePath = fullPath.StartsWith(projectFolder, StringComparison.OrdinalIgnoreCase)
            ? fullPath.Substring(projectFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : string.Empty;

        if (!string.IsNullOrEmpty(relativePath))
        {
            var firstSegment = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            
            // Only ignore "celbridge" folder if it's directly in the project root
            if (firstSegment.Equals("celbridge", StringComparison.OrdinalIgnoreCase))
            {
                return true;
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

    private ResourceKey GetResourceKey(string fullPath)
    {
        try
        {
            var projectFolder = _projectFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!fullPath.StartsWith(projectFolder, StringComparison.OrdinalIgnoreCase))
            {
                // Path is not in project folder - return empty ResourceKey
                _logger.LogWarning($"Path is not in project folder: {fullPath}");
                return ResourceKey.Empty;
            }

            var relativePath = fullPath.Substring(projectFolder.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            
            // Convert backslashes to forward slashes for ResourceKey
            var resourceKeyPath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
            if (Path.AltDirectorySeparatorChar != '/')
            {
                resourceKeyPath = resourceKeyPath.Replace(Path.AltDirectorySeparatorChar, '/');
            }
            
            return new ResourceKey(resourceKeyPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to convert path to ResourceKey: {fullPath}");
            return ResourceKey.Empty;
        }
    }

    #endregion

    #region IDisposable

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
                _debounceTimer?.Dispose();
            }

            _isDisposed = true;
        }
    }

    ~ResourceChangeMonitor()
    {
        Dispose(false);
    }

    #endregion
}
