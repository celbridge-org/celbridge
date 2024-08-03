using Celbridge.Resources;

namespace Celbridge.Projects;

/// <summary>
/// The resource tree view provides functionality to populate the resource tree view in the project panel.
/// </summary>
public interface IResourceTreeView
{
    /// <summary>
    /// Populate the resource tree view with the contents of the resource registry.
    /// </summary>
    Task<Result> PopulateTreeView(IResourceRegistry resourceRegistry);
}
