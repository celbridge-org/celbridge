namespace Celbridge.FileSystem.Services;

/// <summary>
/// Creates FileSystemMonitor instances over local backing folders. Registered
/// as a singleton; each created monitor is owned and disposed by its caller.
/// </summary>
public sealed class FileSystemMonitorFactory : IFileSystemMonitorFactory
{
    private readonly ILogger<FileSystemMonitor> _logger;

    public FileSystemMonitorFactory(ILogger<FileSystemMonitor> logger)
    {
        _logger = logger;
    }

    public IFileSystemMonitor Create(string backingFolderPath)
    {
        return new FileSystemMonitor(_logger, backingFolderPath);
    }
}
