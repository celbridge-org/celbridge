namespace Celbridge.Explorer.Helpers;

/// <summary>
/// Decides which resources the Explorer tree draws. Sidecar files are never drawn, and the project's
/// hide patterns filter the rest unless the tree is showing hidden files.
/// </summary>
public static class ResourceTreeFilter
{
    /// <summary>
    /// Whether the resource is a kind the tree draws at all. Sidecars and their broken forms are project
    /// metadata rather than content.
    /// </summary>
    public static bool IsDrawableKind(IResource resource)
    {
        if (resource is IFileResource fileResource)
        {
            return fileResource.FileKind is not (FileKind.Sidecar or FileKind.Orphan or FileKind.InvalidSidecar);
        }

        return resource is IFolderResource;
    }

    /// <summary>
    /// Whether the resource is drawn on its own row, which a hidden resource is only while the tree is
    /// showing hidden files.
    /// </summary>
    public static bool IsDrawn(
        IResource resource,
        IResourceRegistry registry,
        IResourcePolicy policy,
        bool showHiddenFiles)
    {
        if (!IsDrawableKind(resource))
        {
            return false;
        }

        if (showHiddenFiles)
        {
            return true;
        }

        var resourceKey = registry.GetResourceKey(resource);

        return !policy.IsHidden(resourceKey, resource is IFolderResource);
    }

    /// <summary>
    /// Whether the folder holds a child the tree would draw, so a folder whose children are all hidden or
    /// all sidecars does not offer an expander that opens onto nothing.
    /// </summary>
    public static bool HasDrawableChildren(
        IFolderResource folder,
        IResourceRegistry registry,
        IResourcePolicy policy,
        bool showHiddenFiles)
    {
        foreach (var child in folder.Children)
        {
            if (IsDrawn(child, registry, policy, showHiddenFiles))
            {
                return true;
            }
        }

        return false;
    }
}
