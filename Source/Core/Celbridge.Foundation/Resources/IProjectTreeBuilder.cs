namespace Celbridge.Resources;

/// <summary>
/// Walks the project folder through the resource file-system gateway and
/// produces a fresh resource tree. Visibility filtering is the gateway's
/// responsibility, so entries hidden by policy never reach the tree.
/// </summary>
public interface IProjectTreeBuilder
{
    /// <summary>
    /// Builds a fresh tree for the project root. The returned root has
    /// parent = null and child resources sorted folders-first then alphabetical.
    /// Fresh instances are returned on every call. Fails if the project folder
    /// cannot be enumerated.
    /// </summary>
    Task<Result<IFolderResource>> BuildTreeAsync();
}
