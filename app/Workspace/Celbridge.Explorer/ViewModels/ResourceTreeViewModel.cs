using System.Collections.ObjectModel;
using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Explorer.Models;
using Celbridge.Explorer.ViewModels.Helpers;
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
    private readonly ICommandService _commandService;
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

    public ResourceTreeViewModel(
        ILogger<ResourceTreeViewModel> logger,
        IMessengerService messengerService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _commandService = commandService;
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
    /// Preserves the selected item if possible.
    /// </summary>
    public void RebuildResourceTree()
    {
        // Save the currently selected resource key before rebuilding
        var selectedResourceKey = GetSelectedResourceKey();

        var rootFolder = _resourceRegistry.RootFolder;
        var items = ResourceTreeBuilder.BuildFlatList(rootFolder, _folderStateService, _resourceRegistry);

        TreeItems.Clear();
        foreach (var item in items)
        {
            TreeItems.Add(item);
        }

        // Restore selection if the resource still exists
        if (!selectedResourceKey.IsEmpty)
        {
            SetSelectedResource(selectedResourceKey);
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

        var newExpandedState = !item.IsExpanded;
        item.IsExpanded = newExpandedState;

        // Update the folder state service and persist
        if (item.Resource is IFolderResource folderResource)
        {
            folderResource.IsExpanded = newExpandedState;

            // Update FolderStateService synchronously before rebuilding
            // so the new state is immediately available when BuildFlatList reads it
            var folderResourceKey = _resourceRegistry.GetResourceKey(folderResource);
            _folderStateService.SetExpanded(folderResourceKey, newExpandedState);

            // Also execute the command for undo/redo support (this happens asynchronously)
            SetFolderIsExpanded(folderResource, newExpandedState);
        }

        // Rebuild the list to reflect the new state
        RebuildResourceTree();
    }

    /// <summary>
    /// Expands a folder item if it's collapsed.
    /// </summary>
    public void ExpandItem(ResourceViewItem item)
    {
        if (!item.IsFolder || !item.HasChildren || item.IsExpanded)
        {
            return;
        }

        item.IsExpanded = true;

        if (item.Resource is IFolderResource folderResource)
        {
            folderResource.IsExpanded = true;

            // Update FolderStateService synchronously before rebuilding
            var folderResourceKey = _resourceRegistry.GetResourceKey(folderResource);
            _folderStateService.SetExpanded(folderResourceKey, true);

            // Also execute command for undo/redo
            SetFolderIsExpanded(folderResource, true);
        }

        RebuildResourceTree();
    }

    /// <summary>
    /// Collapses a folder item if it's expanded.
    /// </summary>
    public void CollapseItem(ResourceViewItem item)
    {
        // Don't allow collapsing the root folder
        if (!item.IsFolder || !item.IsExpanded || item.IsRootFolder)
        {
            return;
        }

        item.IsExpanded = false;

        if (item.Resource is IFolderResource folderResource)
        {
            folderResource.IsExpanded = false;

            // Update FolderStateService synchronously before rebuilding
            var folderResourceKey = _resourceRegistry.GetResourceKey(folderResource);
            _folderStateService.SetExpanded(folderResourceKey, false);

            // Also execute command for undo/redo
            SetFolderIsExpanded(folderResource, false);
        }

        RebuildResourceTree();
    }

    /// <summary>
    /// Collapses all folders in the tree (except root folder).
    /// </summary>
    public void CollapseAllFolders()
    {
        foreach (var item in TreeItems.ToList())
        {
            // Skip root folder - it should never be collapsed
            if (item.IsFolder && item.IsExpanded && !item.IsRootFolder)
            {
                item.IsExpanded = false;
                if (item.Resource is IFolderResource folderResource)
                {
                    folderResource.IsExpanded = false;

                    // Update FolderStateService synchronously
                    var folderResourceKey = _resourceRegistry.GetResourceKey(folderResource);
                    _folderStateService.SetExpanded(folderResourceKey, false);

                    // Also execute command for undo/redo
                    SetFolderIsExpanded(folderResource, false);
                }
            }
        }

        RebuildResourceTree();
    }

    /// <summary>
    /// Expands all parent folders of a resource to make it visible.
    /// </summary>
    public void ExpandPathToResource(ResourceKey resource)
    {
        var segments = resource.ToString().Split('/');
        var currentPath = string.Empty;

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
                    SetFolderIsExpanded(folder, true);
                }
            }
        }

        // Rebuild the list to include newly expanded items
        RebuildResourceTree();
    }

    //
    // Tree View state
    //

    private void SetFolderIsExpanded(IFolderResource folder, bool isExpanded)
    {
        var folderResource = _resourceRegistry.GetResourceKey(folder);

        bool currentStateServiceState = _folderStateService.IsExpanded(folderResource);
        bool currentFolderState = folder.IsExpanded;

        if (currentStateServiceState == isExpanded &&
            currentFolderState == isExpanded)
        {
            return;
        }

        _commandService.Execute<IExpandFolderCommand>(command =>
        {
            command.FolderResource = folderResource;
            command.Expanded = isExpanded;
        });
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
    /// Gets all sibling items of the selected item (or root-level items if nothing selected).
    /// </summary>
    public List<ResourceViewItem> GetSiblingItems()
    {
        var siblings = new List<ResourceViewItem>();

        // Determine the parent folder key to match against
        ResourceKey targetParentKey;

        if (SelectedItem != null)
        {
            var parentFolder = SelectedItem.Resource.ParentFolder;
            targetParentKey = parentFolder != null
                ? _resourceRegistry.GetResourceKey(parentFolder)
                : ResourceKey.Empty;
        }
        else
        {
            // Nothing selected - select root-level items (parent key is empty)
            targetParentKey = ResourceKey.Empty;
        }

        // Find all visible items that have the same parent folder
        foreach (var item in TreeItems)
        {
            if (item.IsRootFolder)
            {
                continue; // Never include root folder
            }

            var itemParentFolder = item.Resource.ParentFolder;
            var itemParentKey = itemParentFolder != null
                ? _resourceRegistry.GetResourceKey(itemParentFolder)
                : ResourceKey.Empty;

            if (itemParentKey == targetParentKey)
            {
                siblings.Add(item);
            }
        }

        return siblings;
    }
}
