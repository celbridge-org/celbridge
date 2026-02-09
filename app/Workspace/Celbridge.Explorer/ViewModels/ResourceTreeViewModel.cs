using System.Collections.ObjectModel;
using Celbridge.DataTransfer;
using Celbridge.Explorer.Models;
using Celbridge.Logging;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Explorer.ViewModels;

/// <summary>
/// View model for the control that displays the resource tree in the Explorer panel.
/// </summary>
public partial class ResourceTreeViewModel : ObservableObject
{
    private readonly ILogger<ResourceTreeViewModel> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IFolderStateService _folderStateService;
    private readonly IDataTransferService _dataTransferService;

    /// <summary>
    /// The flat list of items to display in the ListView.
    /// </summary>
    public ObservableCollection<ResourceViewItem> TreeItems { get; } = new();

    /// <summary>
    /// The selected item in the tree (the anchor). Required for ListView two-way binding
    /// and serves as the keyboard focus/anchor item in multi-select scenarios.
    /// </summary>
    [ObservableProperty]
    private ResourceViewItem? _selectedItem;

    /// <summary>
    /// All selected items for multi-select operations. Updated from ListView.SelectedItems
    /// which is read-only and cannot be bound directly.
    /// </summary>
    public List<ResourceViewItem> SelectedItems { get; private set; } = [];

    /// <summary>
    /// The root folder resource.
    /// </summary>
    public IFolderResource RootFolder => _resourceRegistry.RootFolder;

    /// <summary>
    /// Raised when the view should update the selected resources.
    /// </summary>
    public event Action<List<ResourceKey>>? SelectionRequested;

    public ResourceTreeViewModel(
        ILogger<ResourceTreeViewModel> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        _folderStateService = workspaceWrapper.WorkspaceService.ExplorerService.FolderStateService;
        _dataTransferService = workspaceWrapper.WorkspaceService.DataTransferService;
    }

    //
    // Lifecycle
    //

    public void OnLoaded()
    {
        _messengerService.Register<ClipboardContentChangedMessage>(this, OnClipboardContentChangedMessage);
        _messengerService.Register<ResourceRegistryUpdatedMessage>(this, OnResourceRegistryUpdatedMessage);
    }

    public void OnUnloaded()
    {
        _messengerService.UnregisterAll(this);
    }

    private void OnClipboardContentChangedMessage(object recipient, ClipboardContentChangedMessage message)
    {
        var contentDescription = _dataTransferService.GetClipboardContentDescription();

        if (contentDescription.ContentType == ClipboardContentType.Resource)
        {
            // Todo: Clear previously faded resources
        }

        if (contentDescription.ContentOperation == ClipboardContentOperation.Move)
        {
            // Todo: Fade cut resources in the tree view
        }
    }

    private void OnResourceRegistryUpdatedMessage(object recipient, ResourceRegistryUpdatedMessage message)
    {
        // Clean up expanded folders that no longer exist in the registry
        // This must happen before rebuilding the tree to avoid stale folder states
        _folderStateService.Cleanup();

        // Rebuild the tree with the updated registry
        RebuildResourceTree();
    }

    //
    // Tree population
    //

    /// <summary>
    /// Rebuilds the flat list from the current resource registry state.
    /// If selectedResources is provided, those resources will be selected after rebuild.
    /// Otherwise, the current selection is preserved.
    /// </summary>
    public void RebuildResourceTree(List<ResourceKey>? selectedResources = null)
    {
        // Use provided selection, or preserve current selection
        var resourcesToSelect = selectedResources ?? GetSelectedResourceKeys();

        var items = BuildFlatItemList();

        TreeItems.Clear();
        foreach (var item in items)
        {
            TreeItems.Add(item);
        }

        if (resourcesToSelect.Count > 0)
        {
            SelectionRequested?.Invoke(resourcesToSelect);
        }
    }

    /// <summary>
    /// Builds a flat list of ResourceViewItems from the resource registry's folder hierarchy.
    /// </summary>
    private List<ResourceViewItem> BuildFlatItemList()
    {
        var items = new List<ResourceViewItem>();
        var rootFolder = _resourceRegistry.RootFolder;

        // Add the root folder as the first item (always expanded, never collapsible)
        var hasChildren = rootFolder.Children.Count > 0;
        var projectName = Path.GetFileName(_resourceRegistry.ProjectFolderPath);
        var rootItem = new ResourceViewItem(
            rootFolder,
            indentLevel: 0,
            isExpanded: true,
            hasChildren,
            isRootFolder: true,
            displayName: projectName);
        items.Add(rootItem);

        // Add children at indent level 0 (root uses negative margin, so children at 0 align correctly)
        BuildFlatItemListRecursive(rootFolder.Children, items, indentLevel: 0);

        return items;
    }

    /// <summary>
    /// Recursively builds the flat list by traversing the tree structure.
    /// </summary>
    private void BuildFlatItemListRecursive(
        IList<IResource> resources,
        List<ResourceViewItem> items,
        int indentLevel)
    {
        foreach (var resource in resources)
        {
            if (resource is IFolderResource folderResource)
            {
                var hasChildren = folderResource.Children.Count > 0;
                var resourceKey = _resourceRegistry.GetResourceKey(folderResource);
                var isExpanded = _folderStateService.IsExpanded(resourceKey);

                var item = new ResourceViewItem(resource, indentLevel, isExpanded, hasChildren);
                items.Add(item);

                // Only add children if the folder is expanded
                if (isExpanded && hasChildren)
                {
                    BuildFlatItemListRecursive(
                        folderResource.Children,
                        items,
                        indentLevel + 1);
                }
            }
            else if (resource is IFileResource)
            {
                var item = new ResourceViewItem(resource, indentLevel, false, false);
                items.Add(item);
            }
        }
    }

    /// <summary>
    /// Checks if a resource exists in the registry.
    /// </summary>
    public bool ResourceExists(ResourceKey resourceKey)
    {
        return _resourceRegistry.GetResource(resourceKey).IsSuccess;
    }

    /// <summary>
    /// Gets the resource key for the currently selected item (anchor).
    /// </summary>
    public ResourceKey GetSelectedResourceKey()
    {
        if (SelectedItem?.Resource != null)
        {
            return _resourceRegistry.GetResourceKey(SelectedItem.Resource);
        }
        return ResourceKey.Empty;
    }

    /// <summary>
    /// Gets the list of selected resource keys.
    /// </summary>
    public List<ResourceKey> GetSelectedResourceKeys()
    {
        var selectedResources = SelectedItems
            .Select(item => _resourceRegistry.GetResourceKey(item.Resource))
            .ToList();

        return selectedResources;
    }

    /// <summary>
    /// Sets the selected item by resource key.
    /// </summary>
    public bool SetSelectedResource(ResourceKey resourceKey)
    {
        if (resourceKey.IsEmpty)
        {
            SelectedItem = null;
            return true;
        }

        // Find the item in the tree
        foreach (var item in TreeItems)
        {
            var itemKey = _resourceRegistry.GetResourceKey(item.Resource);
            if (itemKey == resourceKey)
            {
                SelectedItem = item;
                return true;
            }
        }

        _logger.LogDebug($"SetSelectedResource: Item '{resourceKey}' not found in tree (count = {TreeItems.Count})");
        return false;
    }

    /// <summary>
    /// Selects the parent folder of the currently selected item.
    /// Returns true if a parent was selected, false otherwise.
    /// </summary>
    public bool SelectParentFolder()
    {
        if (SelectedItem?.Resource.ParentFolder == null)
        {
            return false;
        }

        var parentKey = _resourceRegistry.GetResourceKey(SelectedItem.Resource.ParentFolder);
        if (parentKey.IsEmpty)
        {
            return false;
        }

        return SetSelectedResource(parentKey);
    }

    //
    // Expand/Collapse
    //

    /// <summary>
    /// Toggles the expansion state of a folder item (except root folder).
    /// </summary>
    public void ToggleExpand(ResourceViewItem item)
    {
        // Don't allow toggling root folder expansion
        if (!item.IsFolder || !item.HasChildren || item.IsRootFolder)
        {
            return;
        }

        if (item.IsExpanded)
        {
            CollapseItem(item);
        }
        else
        {
            ExpandItem(item);
        }
    }

    /// <summary>
    /// Expands a folder item if it's collapsed.
    /// Preserves all existing selections.
    /// </summary>
    public void ExpandItem(ResourceViewItem item)
    {
        if (!item.IsFolder || !item.HasChildren || item.IsExpanded)
        {
            return;
        }

        // Preserve current selection
        var selectedKeys = GetSelectedResourceKeys();

        item.IsExpanded = true;

        if (item.Resource is IFolderResource folderResource)
        {
            folderResource.IsExpanded = true;

            var folderResourceKey = _resourceRegistry.GetResourceKey(folderResource);
            _folderStateService.SetExpanded(folderResourceKey, true);
        }

        RebuildResourceTree(selectedKeys);
        RequestWorkspaceSave();
    }

    /// <summary>
    /// Collapses a folder item if it's expanded.
    /// When collapsing, any selected items inside the folder cause the folder to become selected.
    /// Items selected outside the folder remain selected.
    /// </summary>
    public void CollapseItem(ResourceViewItem item)
    {
        // Don't allow collapsing the root folder
        if (!item.IsFolder || !item.IsExpanded || item.IsRootFolder)
        {
            return;
        }

        var folderKey = _resourceRegistry.GetResourceKey(item.Resource);

        // Compute new selection: items inside folder transfer to the folder, others remain
        var selectedKeys = GetSelectedResourceKeys();
        var keysInsideFolder = selectedKeys.Where(key => key.IsDescendantOf(folderKey)).ToList();
        var keysOutsideFolder = selectedKeys.Where(key => !key.IsDescendantOf(folderKey)).ToList();

        var newSelectedKeys = new List<ResourceKey>(keysOutsideFolder);
        if (keysInsideFolder.Count > 0)
        {
            newSelectedKeys.Add(folderKey);
        }

        item.IsExpanded = false;

        if (item.Resource is IFolderResource folderResource)
        {
            folderResource.IsExpanded = false;
            _folderStateService.SetExpanded(folderKey, false);
        }

        RebuildResourceTree(newSelectedKeys);
        RequestWorkspaceSave();
    }

    /// <summary>
    /// Collapses all folders in the tree.
    /// </summary>
    public void CollapseAllFolders()
    {
        // Map selected items to their top-level parent folders so we can
        // preserve the selection (to an extent) after collapsing everything.
        var selectedKeys = GetSelectedResourceKeys();
        var topLevelFolderKeys = new HashSet<ResourceKey>();
        foreach (var key in selectedKeys)
        {
            var path = key.ToString();
            if (!string.IsNullOrEmpty(path))
            {
                var firstSlash = path.IndexOf('/');
                var topLevelPath = firstSlash > 0 ? path.Substring(0, firstSlash) : path;
                topLevelFolderKeys.Add(new ResourceKey(topLevelPath));
            }
        }

        foreach (var item in TreeItems.ToList())
        {
            // Skip root folder - it should never be collapsed
            if (item.IsFolder &&
                item.IsExpanded &&
                !item.IsRootFolder)
            {
                item.IsExpanded = false;
                if (item.Resource is IFolderResource folderResource)
                {
                    folderResource.IsExpanded = false;

                    var folderResourceKey = _resourceRegistry.GetResourceKey(folderResource);
                    _folderStateService.SetExpanded(folderResourceKey, false);
                }
            }
        }

        RebuildResourceTree(topLevelFolderKeys.ToList());
        RequestWorkspaceSave();
    }

    /// <summary>
    /// Expands all parent folders of a resource to make it visible.
    /// </summary>
    public void ExpandPathToResource(ResourceKey resource)
    {
        var segments = resource.ToString().Split('/');
        var currentPath = string.Empty;
        var anyExpanded = false;

        // Expand each folder in the path (except the last segment which is the target)
        for (int i = 0; i < segments.Length - 1; i++)
        {
            currentPath = i == 0 ? segments[i] : $"{currentPath}/{segments[i]}";
            var folderKey = new ResourceKey(currentPath);

            var folderResult = _resourceRegistry.GetResource(folderKey);
            if (folderResult.IsSuccess && folderResult.Value is IFolderResource folder)
            {
                if (!folder.IsExpanded)
                {
                    folder.IsExpanded = true;
                    _folderStateService.SetExpanded(folderKey, true);
                    anyExpanded = true;
                }
            }
        }

        // Rebuild the list to include newly expanded items
        RebuildResourceTree();

        if (anyExpanded)
        {
            RequestWorkspaceSave();
        }
    }

    private void RequestWorkspaceSave()
    {
        var message = new WorkspaceStateDirtyMessage();
        _messengerService.Send(message);
    }

    //
    // Multi-selection support
    //

    /// <summary>
    /// Updates the SelectedItems collection from the view and sends the selection changed notification.
    /// Called when selection changes in the ListView.
    /// </summary>
    public void UpdateSelectedItems(List<ResourceViewItem> selectedItems)
    {
        SelectedItems.Clear();
        SelectedItems.AddRange(selectedItems);

        // Send the selection changed notification
        var selectedResourceKey = GetSelectedResourceKey();
        OnSelectedResourceChanged(selectedResourceKey);
    }

    /// <summary>
    /// Gets the list of selected resources.
    /// </summary>
    public List<IResource> GetSelectedResources()
    {
        return SelectedItems.Select(item => item.Resource).ToList();
    }

    /// <summary>
    /// Finds tree items matching the given resource keys.
    /// </summary>
    public List<ResourceViewItem> FindItemsByResourceKeys(List<ResourceKey> resourceKeys)
    {
        var items = new List<ResourceViewItem>();
        var keySet = new HashSet<ResourceKey>(resourceKeys);

        foreach (var item in TreeItems)
        {
            var itemKey = _resourceRegistry.GetResourceKey(item.Resource);
            if (keySet.Contains(itemKey))
            {
                items.Add(item);
            }
        }

        return items;
    }

    //
    // Selection notification
    //

    public void OnSelectedResourceChanged(ResourceKey resource)
    {
        var message = new SelectedResourceChangedMessage(resource);
        _messengerService.Send(message);
    }

    /// <summary>
    /// Gets the folder resource for the currently selected item.
    /// </summary>
    public IFolderResource? GetSelectedResourceFolder()
    {
        if (SelectedItem?.Resource is IFolderResource folderResource)
        {
            return folderResource;
        }
        else if (SelectedItem?.Resource is IFileResource fileResource)
        {
            return fileResource.ParentFolder;
        }
        return null;
    }

    /// <summary>
    /// Gets all sibling items of the specified item (or root-level items if nothing specified).
    /// </summary>
    public List<ResourceViewItem> GetSiblingItems(ResourceViewItem? selectedItem = null)
    {
        // Use the provided item, or fall back to the current selection
        var item = selectedItem ?? SelectedItem;

        // Determine the parent folder key:
        // - If an item is provided/selected, use its parent folder's key
        // - If nothing is selected, use the root folder's key (for root-level items)
        var targetParentKey = item != null
            ? GetParentKey(item.Resource.ParentFolder)
            : _resourceRegistry.GetResourceKey(RootFolder);

        return TreeItems
            .Where(i => !i.IsRootFolder && GetParentKey(i.Resource.ParentFolder) == targetParentKey)
            .ToList();
    }

    private ResourceKey GetParentKey(IFolderResource? parentFolder)
    {
        return parentFolder != null
            ? _resourceRegistry.GetResourceKey(parentFolder)
            : _resourceRegistry.GetResourceKey(RootFolder);
    }
}
