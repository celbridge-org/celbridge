namespace Celbridge.Explorer;

/// <summary>
/// Provides functionality to populate the resource tree in the Explorer panel.
/// </summary>
public interface IResourceTree
{
    /// <summary>
    /// Populate the resource tree with the contents of the resource registry.
    /// </summary>
    Task<Result> PopulateResourceTree(IResourceRegistry resourceRegistry);
}
