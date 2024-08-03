using Celbridge.Resources;

namespace Celbridge.Projects;

/// <summary>
/// The project service provides functionality to support the project panel in the workspace UI.
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// Returns the Resource Registry associated with the loaded project.
    /// </summary>
    IResourceRegistry ResourceRegistry { get; }

    /// <summary>
    /// Returns the Resource Tree View associated with the loaded project.
    /// </summary>
    IResourceTreeView ResourceTreeView { get; }

    /// <summary>
    /// Factory method to create the project panel for the workspace UI.
    /// </summary>
    object CreateProjectPanel();

    /// <summary>
    /// Update the resource registry and populate the resource tree view.
    /// </summary>
    Task<Result> UpdateResourcesAsync();
}
