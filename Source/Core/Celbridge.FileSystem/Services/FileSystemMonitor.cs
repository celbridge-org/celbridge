using System.Collections.Concurrent;
using ThreadingTimer = System.Threading.Timer;

namespace Celbridge.FileSystem.Services;

/// <summary>
/// FileSystemWatcher-backed implementation of ILocalFileSystem's watch
/// counterpart. Owns one watcher over the backing folder subtree and coalesces
/// the burst of Changed events a single write emits into one settled
/// notification. The only watcher call site in product code.
/// </summary>
public sealed class FileSystemMonitor : IFileSystemMonitor
{
    // A single File.WriteAllBytes on Windows generates multiple FileSystemWatcher
    // Changed events (truncate, write, close). This per-path trailing-edge
    // debounce coalesces that burst into one settled-state notification. The
    // window also gives the writer time to release the handle before consumers
    // probe size or read content.
    private const int ChangedDebounceMs = 75;

    private sealed class ChangedDebounceEntry
    {
        public ThreadingTimer? Timer;
    }

    private readonly ILogger<FileSystemMonitor> _logger;
    private readonly string _backingFolderPath;

    // Case-insensitive keying: a watcher event's leaf name echoes the casing the
    // mutating call used, not the canonical on-disk name, so two writers touching
    // one file with different casing would otherwise get separate debounce slots.
    private readonly ConcurrentDictionary<string, ChangedDebounceEntry> _changedDebounceEntries =
        new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? _watcher;
    private bool _isDisposed;

    public event EventHandler<FileSystemMonitorEvent>? FileSystemChanged;

    public FileSystemMonitor(ILogger<FileSystemMonitor> logger, string backingFolderPath)
    {
        _logger = logger;
        _backingFolderPath = backingFolderPath;
    }

    public Result Start()
    {
        if (_isDisposed)
        {
            return Result.Fail("Cannot start a disposed FileSystemMonitor");
        }

        // The existence guard lives here rather than in the caller so the caller
        // never touches the System.IO facades.
        if (!Directory.Exists(_backingFolderPath))
        {
            return Result.Fail($"Backing folder does not exist: {_backingFolderPath}");
        }

        try
        {
            var watcher = new FileSystemWatcher(_backingFolderPath)
            {
                NotifyFilter = NotifyFilters.FileName
                             | NotifyFilters.DirectoryName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Size,
                IncludeSubdirectories = true,
                EnableRaisingEvents = false
            };

            watcher.Created += OnWatcherCreated;
            watcher.Changed += OnWatcherChanged;
            watcher.Deleted += OnWatcherDeleted;
            watcher.Renamed += OnWatcherRenamed;
            watcher.Error += OnWatcherError;

            watcher.EnableRaisingEvents = true;
            _watcher = watcher;

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to start file system monitor for: {_backingFolderPath}")
                .WithException(ex);
        }
    }

    private void OnWatcherCreated(object sender, FileSystemEventArgs e)
    {
        Raise(new FileSystemMonitorEvent(FileSystemMonitorEventKind.Created, e.FullPath, OldPath: null));
    }

    private void OnWatcherChanged(object sender, FileSystemEventArgs e)
    {
        EnqueueChangedEvent(e.FullPath);
    }

    private void OnWatcherDeleted(object sender, FileSystemEventArgs e)
    {
        Raise(new FileSystemMonitorEvent(FileSystemMonitorEventKind.Deleted, e.FullPath, OldPath: null));
    }

    private void OnWatcherRenamed(object sender, RenamedEventArgs e)
    {
        Raise(new FileSystemMonitorEvent(FileSystemMonitorEventKind.Renamed, e.FullPath, e.OldFullPath));
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
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

    // Per-path trailing-edge debounce: each Changed event resets the timer for
    // its path. When the timer expires we raise a single Changed event for the
    // settled state. Created / Deleted / Renamed are not debounced because they
    // carry distinct semantics and listeners need each one.
    private void EnqueueChangedEvent(string fullPath)
    {
        if (_isDisposed)
        {
            return;
        }

        _changedDebounceEntries.AddOrUpdate(
            fullPath,
            addValueFactory: _ =>
            {
                var entry = new ChangedDebounceEntry();
                entry.Timer = new ThreadingTimer(
                    callback: state => OnChangedDebounceElapsed(fullPath),
                    state: null,
                    dueTime: ChangedDebounceMs,
                    period: Timeout.Infinite);
                return entry;
            },
            updateValueFactory: (_, existing) =>
            {
                // A concurrent OnChangedDebounceElapsed may dispose this timer
                // between the lookup and here. Treat the race as the burst already
                // settling rather than letting the watcher callback throw.
                try
                {
                    existing.Timer?.Change(ChangedDebounceMs, Timeout.Infinite);
                }
                catch (ObjectDisposedException)
                {
                }
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

        Raise(new FileSystemMonitorEvent(FileSystemMonitorEventKind.Changed, fullPath, OldPath: null));
    }

    private void Raise(FileSystemMonitorEvent monitorEvent)
    {
        // A watcher callback queued before Dispose may still fire after it. Drop
        // it so listeners never see an event from a monitor that has shut down.
        if (_isDisposed)
        {
            return;
        }

        FileSystemChanged?.Invoke(this, monitorEvent);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnWatcherCreated;
            _watcher.Changed -= OnWatcherChanged;
            _watcher.Deleted -= OnWatcherDeleted;
            _watcher.Renamed -= OnWatcherRenamed;
            _watcher.Error -= OnWatcherError;
            _watcher.Dispose();
            _watcher = null;
        }

        // Drain any in-flight per-path Changed debounce timers. Dispose before
        // clearing so a timer that fires mid-shutdown finds an empty dict and
        // exits via the TryRemove guard.
        foreach (var pair in _changedDebounceEntries)
        {
            pair.Value.Timer?.Dispose();
        }
        _changedDebounceEntries.Clear();
    }
}
