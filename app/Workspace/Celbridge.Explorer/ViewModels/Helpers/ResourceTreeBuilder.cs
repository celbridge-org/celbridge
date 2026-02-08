using Celbridge.Explorer.Models;

namespace Celbridge.Explorer.ViewModels.Helpers;

/// <summary>
/// Builds a flat list of ResourceViewItems from a hierarchical folder structure.
/// Used to populate the ListView-based resource tree.
/// </summary>
public static class ResourceTreeBuilder
{
    /// <summary>
    /// Builds a flat list of ResourceViewItems from the resource registry's folder hierarchy.
    /// Includes the root folder as the first item, followed by its children.
    /// Only includes children of expanded folders.
    /// </summary>
    public static List<ResourceViewItem> BuildFlatList(
        IFolderResource rootFolder,
        IFolderStateService folderStateService,
        IResourceRegistry resourceRegistry)
    {
        var items = new List<ResourceViewItem>();

        // Add the root folder as the first item (always expanded, never collapsible)
        var hasChildren = rootFolder.Children.Count > 0;
        var projectName = Path.GetFileName(resourceRegistry.ProjectFolderPath);
        var rootItem = new ResourceViewItem(
            rootFolder,
            indentLevel: 0,
            isExpanded: true,
            hasChildren,
            isRootFolder: true,
            displayName: projectName);
        items.Add(rootItem);

        // Add children at indent level 0 (root uses negative margin, so children at 0 align correctly)
        BuildFlatListRecursive(rootFolder.Children, items, 0, folderStateService, resourceRegistry);

        return items;
    }

    /// <summary>
    /// Recursively builds the flat list by traversing the tree structure.
    /// </summary>
    private static void BuildFlatListRecursive(
        IList<IResource> resources,
        List<ResourceViewItem> items,
        int indentLevel,
        IFolderStateService folderStateService,
        IResourceRegistry resourceRegistry)
    {
        foreach (var resource in resources)
        {
            if (resource is IFolderResource folderResource)
            {
                var hasChildren = folderResource.Children.Count > 0;
                var resourceKey = resourceRegistry.GetResourceKey(folderResource);
                var isExpanded = folderStateService.IsExpanded(resourceKey);

                var item = new ResourceViewItem(resource, indentLevel, isExpanded, hasChildren);
                items.Add(item);

                // Only add children if the folder is expanded
                if (isExpanded && hasChildren)
                {
                    BuildFlatListRecursive(
                        folderResource.Children,
                        items,
                        indentLevel + 1,
                        folderStateService,
                        resourceRegistry);
                }
            }
            else if (resource is IFileResource)
            {
                var item = new ResourceViewItem(resource, indentLevel, false, false);
                items.Add(item);
            }
        }
    }
}
