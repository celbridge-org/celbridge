namespace Celbridge.Workspace;

/// <summary>
/// The view that contains the workspace surfaces. The application shell creates one when a project loads
/// and tears it down when the project unloads, so a view is only ever used for a single project.
/// </summary>
public interface IWorkspaceView
{
    /// <summary>
    /// Cancelled by the workspace if its load fails, so a caller waiting on that load stops waiting.
    /// Assigned before the view is shown.
    /// </summary>
    CancellationTokenSource? LoadCancellation { get; set; }

    /// <summary>
    /// Tears the workspace down. Completes once the workspace has finished unloading.
    /// </summary>
    Task TeardownAsync();
}
