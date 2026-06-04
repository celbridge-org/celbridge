namespace Celbridge.Tests.FileSystem;

/// <summary>
/// In-memory <see cref="IFileSystemMonitor"/> for tests. Lets a test raise
/// synthetic path events synchronously so consumers can be exercised without a
/// real FileSystemWatcher, disk, or debounce sleeps.
/// </summary>
public sealed class FakeFileSystemMonitor : IFileSystemMonitor
{
    public string BackingFolderPath { get; }
    public bool Started { get; private set; }
    public bool Disposed { get; private set; }

    public event EventHandler<FileSystemMonitorEvent>? FileSystemChanged;

    public FakeFileSystemMonitor(string backingFolderPath)
    {
        BackingFolderPath = backingFolderPath;
    }

    public Result Start()
    {
        Started = true;
        return Result.Ok();
    }

    public void RaiseCreated(string path)
    {
        Raise(new FileSystemMonitorEvent(FileSystemMonitorEventKind.Created, path, OldPath: null));
    }

    public void RaiseChanged(string path)
    {
        Raise(new FileSystemMonitorEvent(FileSystemMonitorEventKind.Changed, path, OldPath: null));
    }

    public void RaiseDeleted(string path)
    {
        Raise(new FileSystemMonitorEvent(FileSystemMonitorEventKind.Deleted, path, OldPath: null));
    }

    public void RaiseRenamed(string oldPath, string newPath)
    {
        Raise(new FileSystemMonitorEvent(FileSystemMonitorEventKind.Renamed, newPath, oldPath));
    }

    private void Raise(FileSystemMonitorEvent monitorEvent)
    {
        FileSystemChanged?.Invoke(this, monitorEvent);
    }

    public void Dispose()
    {
        Disposed = true;
    }
}

/// <summary>
/// Factory that hands out FakeFileSystemMonitor instances and records each one
/// so a test can raise events on the monitor created for a given root.
/// </summary>
public sealed class FakeFileSystemMonitorFactory : IFileSystemMonitorFactory
{
    private readonly List<FakeFileSystemMonitor> _created = new();

    public IReadOnlyList<FakeFileSystemMonitor> Created => _created;

    public IFileSystemMonitor Create(string backingFolderPath)
    {
        var monitor = new FakeFileSystemMonitor(backingFolderPath);
        _created.Add(monitor);
        return monitor;
    }
}
