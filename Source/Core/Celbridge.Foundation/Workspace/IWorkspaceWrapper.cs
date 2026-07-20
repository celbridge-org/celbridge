namespace Celbridge.Workspace;

/// <summary>
/// Wrapper for the workspace service.
/// The workspace service is only available when a project is loaded.
/// Use this wrapper to check if the workspace is loaded and to access it via dependency injection.
/// </summary>
public interface IWorkspaceWrapper
{
    /// <summary>
    /// Returns true if the workspace page is currently loaded.
    /// </summary>
    bool IsWorkspacePageLoaded { get; }

    /// <summary>
    /// Returns true while a workspace service is present, from its creation early in the load through
    /// unload. This spans a wider window than IsWorkspacePageLoaded, which becomes true only once the page
    /// finishes loading, so it is the correct signal for reaching the workspace during the load sequence.
    /// </summary>
    bool HasWorkspaceService { get; }

    /// <summary>
    /// Returns the workspace service for the current loaded project.
    /// This property is populated prior to the workspace page UI loading, so it can be accessed while the workspace 
    /// is in the process of loading. Attempting to access this property when no workspace service is present throws 
    /// an InvalidOperationException.
    /// </summary>
    IWorkspaceService WorkspaceService { get; }
}
