namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// The project service provides functionality to support the project panel in the workspace UI.
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// Factory method to create the project panel for the workspace UI.
    /// </summary>
    object CreateProjectPanel();

    /// <summary>
    /// Returns the loaded Project Data associated with the open workspace.
    /// </summary>
    IProjectData LoadedProjectData { get; }
}
