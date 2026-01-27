namespace Celbridge.Workspace;

/// <summary>
/// Interface for monitoring file system changes in the project folder and scheduling resource updates.
/// </summary>
public interface IResourceMonitor
{
    /// <summary>
    /// Initializes the resource monitor and starts watching for file system changes.
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
