namespace Celbridge.Explorer.Helpers;

/// <summary>
/// Decides which resources the Explorer tree hides. The tree hides .cel files and what the project's
/// Explorer list excludes, unless the tree is showing hidden files.
/// </summary>
public static class ResourceTreeFilter
{
    /// <summary>
    /// Returns true if the tree hides the resource while the tree is not showing hidden files.
    /// </summary>
    public static bool IsHidden(ResourceKey resource, bool isFolder, IResourcePolicy policy)
    {
        if (!isFolder
            && resource.ResourceName.EndsWith(SidecarFile.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return policy.IsExplorerExcluded(resource, isFolder);
    }
}
