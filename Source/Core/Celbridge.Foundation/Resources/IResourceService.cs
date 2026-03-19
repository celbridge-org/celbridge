namespace Celbridge.Resources;

/// <summary>
/// Service for managing project resources including the resource registry, 
/// resource monitoring, and resource transfer operations.
/// </summary>
public interface IResourceService
{
    /// <summary>
    /// Returns the Resource Registry associated with the current project.
    /// </summary>
    IResourceRegistry Registry { get; }

    /// <summary>
    /// Returns the Resource Monitor associated with the current project.
    /// </summary>
    IResourceMonitor Monitor { get; }

    /// <summary>
    /// Returns the Resource Transfer Service associated with the workspace.
    /// </summary>
    IResourceTransferService TransferService { get; }

    /// <summary>
    /// Returns the Resource Operation Service associated with the workspace.
    /// </summary>
    IResourceOperationService OperationService { get; }

    /// <summary>
    /// Schedules a resource update. 
    /// The update occurs after a short quiet period to coalesce rapid calls from multiple sources.
    /// </summary>
    void ScheduleResourceUpdate();

    /// <summary>
    /// Refreshes the resource registry immediately.
    /// </summary>
    Result UpdateResources();
}
