namespace Celbridge.Resources;

/// <summary>
/// Watches the project folder and any other registered watched roots for file
/// system changes, debouncing and scheduling resource-registry updates.
/// </summary>
public interface IResourceMonitor
{
    /// <summary>
    /// Initializes the resource monitor and starts watching for file system changes
    /// across every registered root whose Capabilities.IsWatched is true.
    /// </summary>
    Result Initialize();

    /// <summary>
    /// Shuts down the resource monitor and stops watching for file system changes.
    /// </summary>
    void Shutdown();

    /// <summary>
    /// Schedules a resource update with debouncing to coalesce rapid calls.
    /// </summary>
    void ScheduleResourceUpdate();
}
