namespace Celbridge.Resources;

/// <summary>
/// Walks the project folder on disk and produces a fresh resource tree,
/// skipping hidden and tool-internal entries that should not surface in the
/// user-visible registry.
/// </summary>
public interface IProjectTreeBuilder
{
    /// <summary>
    /// Builds a fresh tree rooted at the supplied project folder. The returned
    /// root has parent = null and child resources sorted folders-first then
    /// alphabetical. Fresh instances are returned on every call.
    /// </summary>
    IFolderResource BuildTree(string projectFolderPath);
}
