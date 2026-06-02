namespace Celbridge.FileSystem;

/// <summary>
/// Creates IFileSystemMonitor instances bound to a backing folder. The resource
/// layer asks for one monitor per watched root; remote-substrate backends supply
/// their own factory producing monitors over a push or polling channel.
/// </summary>
public interface IFileSystemMonitorFactory
{
    /// <summary>
    /// Creates a monitor that watches the subtree rooted at the given backing
    /// folder. The monitor is not started. The caller owns the returned monitor
    /// and must dispose it.
    /// </summary>
    IFileSystemMonitor Create(string backingFolderPath);
}
