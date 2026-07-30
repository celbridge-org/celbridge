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

    // The substrate watcher buffers events in non-paged pool until they are read. A tool that writes
    // thousands of files into the watched subtree in one burst (uv populating its per-project caches under
    // the hidden project folder) overflows the 8K default, at which point the OS drops events. 64K is the
    // largest size worth requesting; a burst can still outrun it, which is what MonitoringDesynchronized
    // reports.
    private const int WatcherBufferSize = 64 * 1024;

    private sealed class ChangedDebounceEntry
    {
        // Guards the timer and the two fields below against a Changed event arriving while the debounce
        // callback for the same path is running.
        public readonly object Lock = new();

        public ThreadingTimer? Timer;

        // Environment.TickCount64 reading at which the burst is considered settled. Pushed out by each
        // Changed event so a callback that fires against a stale deadline can wait out the remainder.
        public long DeadlineMilliseconds;

        // Set once the debounce has fired and the entry has left the dictionary, so an event that picked
        // this entry up beforehand starts a new burst instead of arming a timer nothing owns.
        public bool IsElapsed;
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

    public event EventHandler? MonitoringDesynchronized;

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
                // Attributes is included so an external attrib +r / -r surfaces
                // as a Changed event. Consumers that cache WritableState use it
                // to refresh the ReadOnlyAttribute source.
                NotifyFilter = NotifyFilters.FileName
                             | NotifyFilters.DirectoryName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Size
                             | NotifyFilters.Attributes,
                IncludeSubdirectories = true,
                InternalBufferSize = WatcherBufferSize,
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

        // An overflow means the OS discarded events before we saw them, so nothing downstream can be
        // brought up to date incrementally. The watcher itself keeps running.
        if (exception is InternalBufferOverflowException)
        {
            _logger.LogWarning(
                "File system watcher buffer overflowed for '{BackingFolder}'; some change events were lost",
                _backingFolderPath);

            if (!_isDisposed)
            {
                MonitoringDesynchronized?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

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

        while (true)
        {
            // The factory only allocates. ConcurrentDictionary is free to run it more than once when
            // threads race on one path and keep only one result, so starting the timer inside it would
            // leave the losing invocations holding undisposed timers that still fire, evicting the entry
            // the winner is debouncing against.
            var entry = _changedDebounceEntries.GetOrAdd(fullPath, _ => new ChangedDebounceEntry());

            lock (entry.Lock)
            {
                if (entry.IsElapsed)
                {
                    // This entry's debounce has already fired and left the dictionary, so the event
                    // belongs to the next burst for the path. Retry to pick up a fresh entry.
                    continue;
                }

                entry.DeadlineMilliseconds = Environment.TickCount64 + ChangedDebounceMs;

                if (entry.Timer is null)
                {
                    entry.Timer = new ThreadingTimer(
                        callback: _ => OnChangedDebounceElapsed(fullPath, entry),
                        state: null,
                        dueTime: ChangedDebounceMs,
                        period: Timeout.Infinite);
                }
                else
                {
                    entry.Timer.Change(ChangedDebounceMs, Timeout.Infinite);
                }

                return;
            }
        }
    }

    private void OnChangedDebounceElapsed(string fullPath, ChangedDebounceEntry entry)
    {
        lock (entry.Lock)
        {
            // A Changed event can push the deadline out after the timer fires but before this callback
            // takes the lock. Serve out the remainder rather than raising mid-burst.
            var remainingMilliseconds = entry.DeadlineMilliseconds - Environment.TickCount64;
            if (remainingMilliseconds > 0)
            {
                entry.Timer?.Change((int)remainingMilliseconds, Timeout.Infinite);
                return;
            }

            entry.IsElapsed = true;
            entry.Timer?.Dispose();
            entry.Timer = null;

            _changedDebounceEntries.TryRemove(fullPath, out _);
        }

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

        // Drain any in-flight per-path Changed debounce timers. Marking each entry elapsed stops a
        // watcher callback that is already holding one from arming a replacement timer.
        foreach (var pair in _changedDebounceEntries)
        {
            var entry = pair.Value;
            lock (entry.Lock)
            {
                entry.IsElapsed = true;
                entry.Timer?.Dispose();
                entry.Timer = null;
            }
        }
        _changedDebounceEntries.Clear();
    }
}
