using System.Collections.ObjectModel;
using Celbridge.Commands;
using Celbridge.Console;
using Celbridge.DataTransfer;
using Celbridge.Documents;
using Celbridge.Explorer.Models;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Python;
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
    private readonly IResourceTransferService _resourceTransferService;
    private readonly IExplorerService _explorerService;
    private readonly IFolderStateService _folderStateService;
    private readonly IDocumentsService _documentsService;
    private readonly IDataTransferService _dataTransferService;
    private readonly IPythonService _pythonService;

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
    private List<ResourceViewItem> _selectedItems = [];
    public List<ResourceViewItem> SelectedItems
    {
        get => _selectedItems;
        private set
        {
            if (SetProperty(ref _selectedItems, value))
            {
                OnPropertyChanged(nameof(IsResourceSelected));
                OnPropertyChanged(nameof(IsSingleItemSelected));
                OnPropertyChanged(nameof(HasMultipleSelection));
            }
        }
    }

    /// <summary>
    /// True when at least one resource is selected.
    /// </summary>
    public bool IsResourceSelected => SelectedItems.Count > 0;

    /// <summary>
    /// True when exactly one item is selected.
    /// </summary>
    public bool IsSingleItemSelected => SelectedItems.Count == 1;

    /// <summary>
    /// True when multiple items are selected.
    /// </summary>
    public bool HasMultipleSelection => SelectedItems.Count > 1;

    /// <summary>
    /// Set to true if the selected context menu item is a file resource that can be opened as a document.
    /// </summary>
    [ObservableProperty]
    private bool _isDocumentResourceSelected;

    /// <summary>
    /// Set to true if the selected context menu item is an executable script that can be run.
    /// </summary>
    [ObservableProperty]
    private bool _isExecutableResourceSelected;

    /// <summary>
    /// Set to true if the clipboard content contains a resource.
    /// </summary>
    [ObservableProperty]
    private bool _isResourceOnClipboard;

    /// <summary>
    /// Set to true if the selected context menu item is a file resource (not a folder).
    /// </summary>
    [ObservableProperty]
    private bool _isFileResourceSelected;

    public ResourceTreeViewModel(
        ILogger<ResourceTreeViewModel> logger,
        IMessengerService messengerService,
        ICommandService commandService,
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _commandService = commandService;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        _resourceTransferService = workspaceWrapper.WorkspaceService.ResourceService.TransferService;
        _explorerService = workspaceWrapper.WorkspaceService.ExplorerService;
        _folderStateService = _explorerService.FolderStateService;
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;
        _dataTransferService = workspaceWrapper.WorkspaceService.DataTransferService;
        _pythonService = workspaceWrapper.WorkspaceService.PythonService;
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
        var items = BuildFlatList(rootFolder, _folderStateService, _resourceRegistry);

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
    /// Builds a flat list of ResourceViewItems from the resource registry's folder hierarchy.
    /// Only includes children of expanded folders.
    /// </summary>
    private static List<ResourceViewItem> BuildFlatList(
        IFolderResource rootFolder,
        IFolderStateService folderStateService,
        IResourceRegistry resourceRegistry)
    {
        var items = new List<ResourceViewItem>();
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
                    BuildFlatListRecursive(folderResource.Children, items, indentLevel + 1, folderStateService, resourceRegistry);
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
    /// Gets the resource key for a resource.
    /// </summary>
    public ResourceKey GetResourceKey(IResource resource)
    {
        return _resourceRegistry.GetResourceKey(resource);
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
        return SelectedItems
            .Select(item => _resourceRegistry.GetResourceKey(item.Resource))
            .ToList();
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
    /// Toggles the expansion state of a folder item.
    /// </summary>
    public void ToggleExpand(ResourceViewItem item)
    {
        if (!item.IsFolder || !item.HasChildren)
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
        if (!item.IsFolder || !item.IsExpanded)
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
    /// Collapses all folders in the tree.
    /// </summary>
    public void CollapseAllFolders()
    {
        foreach (var item in TreeItems.ToList())
        {
            if (item.IsFolder && item.IsExpanded)
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
        SelectedItems = selectedItems;

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

    /// <summary>
    /// Resolves the destination folder for a drop operation.
    /// If the target is a file, returns its parent folder.
    /// If the target is null, returns the root folder.
    /// </summary>
    public IFolderResource ResolveDropTargetFolder(IResource? dropTarget)
    {
        if (dropTarget is IFileResource fileResource)
        {
            return fileResource.ParentFolder ?? _resourceRegistry.RootFolder;
        }
        else if (dropTarget is IFolderResource folderResource)
        {
            return folderResource;
        }
        return _resourceRegistry.RootFolder;
    }

    //
    // Context menu
    //

    public void OnContextMenuOpening(IResource? resource)
    {
        _ = UpdateContextMenuOptions(resource);
    }

    private async Task UpdateContextMenuOptions(IResource? resource)
    {
        // Single-item specific properties (only valid when exactly one item selected)
        IsDocumentResourceSelected = IsSingleItemSelected && IsSupportedDocumentFormat(resource);
        IsExecutableResourceSelected = IsSingleItemSelected && IsResourceExecutable(resource);
        IsFileResourceSelected = IsSingleItemSelected && resource is IFileResource;

        bool isResourceOnClipboard = false;
        var contentDescription = _dataTransferService.GetClipboardContentDescription();
        if (contentDescription.ContentType == ClipboardContentType.Resource)
        {
            var destFolderResource = _resourceRegistry.GetContextMenuItemFolder(resource);
            var getResult = await _dataTransferService.GetClipboardResourceTransfer(destFolderResource);
            if (getResult.IsSuccess)
            {
                var content = getResult.Value;
                isResourceOnClipboard = content.TransferItems.Count > 0;
            }
        }
        IsResourceOnClipboard = isResourceOnClipboard;
    }

    private bool IsSupportedDocumentFormat(IResource? resource)
    {
        if (resource is not null && resource is IFileResource fileResource)
        {
            var resourceKey = _resourceRegistry.GetResourceKey(fileResource);
            var documentType = _documentsService.GetDocumentViewType(resourceKey);
            return documentType != DocumentViewType.UnsupportedFormat;
        }
        return false;
    }

    private bool IsResourceExecutable(IResource? resource)
    {
        if (resource is not null && resource is IFileResource fileResource)
        {
            var resourceKey = _resourceRegistry.GetResourceKey(fileResource);
            var extension = Path.GetExtension(resourceKey);

            if (extension == ExplorerConstants.PythonExtension ||
                extension == ExplorerConstants.IPythonExtension)
            {
                return _pythonService.IsPythonHostAvailable;
            }
        }
        return false;
    }

    //
    // Resource operations
    //

    public void RunScript(IFileResource scriptResource)
    {
        var resource = _resourceRegistry.GetResourceKey(scriptResource);
        var extension = Path.GetExtension(resource);

        if (extension != ExplorerConstants.PythonExtension &&
            extension != ExplorerConstants.IPythonExtension)
        {
            return;
        }

        _commandService.Execute<IRunCommand>(command =>
        {
            command.ScriptResource = resource;
        });
    }

    public void OpenDocument(IFileResource fileResource)
    {
        if (!IsSupportedDocumentFormat(fileResource))
        {
            return;
        }

        var resource = _resourceRegistry.GetResourceKey(fileResource);

        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = resource;
        });
    }

    public void ShowAddResourceDialog(ResourceType resourceType, IFolderResource? destFolder)
    {
        if (destFolder is null)
        {
            destFolder = _resourceRegistry.RootFolder;
        }

        var destFolderResource = _resourceRegistry.GetResourceKey(destFolder);

        _commandService.Execute<IAddResourceDialogCommand>(command =>
        {
            command.ResourceType = resourceType;
            command.DestFolderResource = destFolderResource;
        });
    }

    public void ShowDeleteResourceDialog(IResource resource)
    {
        var resourceKey = _resourceRegistry.GetResourceKey(resource);

        _commandService.Execute<IDeleteResourceDialogCommand>(command =>
        {
            command.Resources = new List<ResourceKey> { resourceKey };
        });
    }

    /// <summary>
    /// Shows the delete dialog for multiple resources.
    /// </summary>
    public void ShowDeleteResourcesDialog(List<IResource> resources)
    {
        if (resources.Count == 0)
        {
            return;
        }

        var resourceKeys = resources
            .Select(r => _resourceRegistry.GetResourceKey(r))
            .ToList();

        _commandService.Execute<IDeleteResourceDialogCommand>(command =>
        {
            command.Resources = resourceKeys;
        });
    }

    public void ShowRenameResourceDialog(IResource resource)
    {
        var resourceKey = _resourceRegistry.GetResourceKey(resource);

        _commandService.Execute<IRenameResourceDialogCommand>(command =>
        {
            command.Resource = resourceKey;
        });
    }

    public void OpenResourceInExplorer(IResource? resource)
    {
        var resourceKey = resource is null ? ResourceKey.Empty : _resourceRegistry.GetResourceKey(resource);

        _commandService.Execute<IOpenFileManagerCommand>(command =>
        {
            command.Resource = resourceKey;
        });
    }

    public void OpenResourceInApplication(IResource? resource)
    {
        var resourceKey = resource is null ? ResourceKey.Empty : _resourceRegistry.GetResourceKey(resource);

        _commandService.Execute<IOpenApplicationCommand>(command =>
        {
            command.Resource = resourceKey;
        });
    }

    public void MoveResourcesToFolder(List<IResource> resources, IFolderResource? destFolder)
    {
        if (destFolder is null)
        {
            destFolder = _resourceRegistry.RootFolder;
        }

        var destResource = _resourceRegistry.GetResourceKey(destFolder);
        var sourceResources = new List<ResourceKey>();

        foreach (var resource in resources)
        {
            var sourceResource = _resourceRegistry.GetResourceKey(resource);
            var resolvedDestResource = _resourceRegistry.ResolveDestinationResource(sourceResource, destResource);

            if (sourceResource == resolvedDestResource)
            {
                continue; // Skip - already at destination
            }

            sourceResources.Add(sourceResource);
        }

        if (sourceResources.Count > 0)
        {
            _commandService.Execute<ICopyResourceCommand>(command =>
            {
                command.SourceResources = sourceResources;
                command.DestResource = destResource;
                command.TransferMode = DataTransferMode.Move;
            });
        }
    }

    //
    // Clipboard support
    //

    public void CutResourceToClipboard(IResource sourceResource)
    {
        var sourceResourceKey = _resourceRegistry.GetResourceKey(sourceResource);

        _commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResources = new List<ResourceKey> { sourceResourceKey };
            command.TransferMode = DataTransferMode.Move;
        });
    }

    /// <summary>
    /// Cuts multiple resources to the clipboard.
    /// </summary>
    public void CutResourcesToClipboard(List<IResource> resources)
    {
        var resourceKeys = resources
            .Select(r => _resourceRegistry.GetResourceKey(r))
            .ToList();

        _commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResources = resourceKeys;
            command.TransferMode = DataTransferMode.Move;
        });
    }

    public void CopyResourceToClipboard(IResource sourceResource)
    {
        var resourceKey = _resourceRegistry.GetResourceKey(sourceResource);

        _commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResources = new List<ResourceKey> { resourceKey };
            command.TransferMode = DataTransferMode.Copy;
        });
    }

    /// <summary>
    /// Copies multiple resources to the clipboard.
    /// </summary>
    public void CopyResourcesToClipboard(List<IResource> resources)
    {
        var resourceKeys = resources
            .Select(r => _resourceRegistry.GetResourceKey(r))
            .ToList();

        _commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResources = resourceKeys;
            command.TransferMode = DataTransferMode.Copy;
        });
    }

    public void PasteResourceFromClipboard(IResource? destResource)
    {
        var destFolderResource = _resourceRegistry.GetContextMenuItemFolder(destResource);

        _commandService.Execute<IPasteResourceFromClipboardCommand>(command =>
        {
            command.DestFolderResource = destFolderResource;
        });
    }

    public void CopyResourceKeyToClipboard(IResource resource)
    {
        var resourceKey = _resourceRegistry.GetResourceKey(resource);

        _commandService.Execute<ICopyTextToClipboardCommand>(command =>
        {
            command.Text = resourceKey;
            command.TransferMode = DataTransferMode.Copy;
        });
    }

    public void CopyFilePathToClipboard(IResource resource)
    {
        var filePath = _resourceRegistry.GetResourcePath(resource);

        _commandService.Execute<ICopyTextToClipboardCommand>(command =>
        {
            command.Text = filePath;
            command.TransferMode = DataTransferMode.Copy;
        });
    }

    public Result ImportResources(List<string> sourcePaths, IResource? destResource)
    {
        if (destResource is null)
        {
            return Result.Fail("Destination resource is null");
        }

        var destFolderResource = _resourceRegistry.GetContextMenuItemFolder(destResource);
        var createResult = _resourceTransferService.CreateResourceTransfer(sourcePaths, destFolderResource, DataTransferMode.Copy);
        if (createResult.IsFailure)
        {
            return Result.Fail($"Failed to create resource transfer. {createResult.Error}");
        }
        var resourceTransfer = createResult.Value;

        var transferResult = _resourceTransferService.TransferResources(destFolderResource, resourceTransfer);
        if (transferResult.IsFailure)
        {
            return Result.Fail($"Failed to transfer resources. {transferResult.Error}");
        }

        return Result.Ok();
    }

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
}
