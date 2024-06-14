namespace Celbridge.BaseLibrary.Workspace;

/// <summary>
/// Wrapper for the workspace service.
/// The workspace service is only available when a project workspace is loaded.
/// Use this wrapper to check if the workspace is loaded and to access it via dependency injection.
/// </summary>
public interface IWorkspaceWrapper
{
    /// <summary>
    /// Returns trues if a project workspace is currently loaded.
    /// </summary>
    bool IsWorkspaceLoaded { get; }

    /// <summary>
    /// The workspace that is currently loaded.
    /// Attempting to access this property when no workspace is loaded will throw an InvalidOperationException.
    /// </summary>
    IWorkspaceService WorkspaceService { get; }
}
