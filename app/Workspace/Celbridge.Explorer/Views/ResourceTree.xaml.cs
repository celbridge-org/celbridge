using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Documents;
using Celbridge.Explorer.Menu;
using Celbridge.Explorer.Models;
using Celbridge.Explorer.ViewModels;
using Celbridge.UserInterface.ContextMenu;
using Celbridge.Workspace;
using Microsoft.UI.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.Explorer.Views;

/// <summary>
/// A custom tree control built on ListView, because TreeView is not flexible enough.
/// </summary>
public sealed partial class ResourceTree : UserControl, IResourceTree
{
    private readonly ICommandService _commandService;
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IResourceTransferService _resourceTransferService;
    private readonly IDocumentsService _documentsService;
    private readonly IMenuBuilder<ExplorerMenuContext> _menuBuilder;
    private readonly IDataTransferService _dataTransferService;
    private bool _isPopulating;

    public ResourceTreeViewModel ViewModel { get; }

    public ResourceTree()
    {
        this.InitializeComponent();

        ViewModel = ServiceLocator.AcquireService<ResourceTreeViewModel>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();
        _menuBuilder = ServiceLocator.AcquireService<IMenuBuilder<ExplorerMenuContext>>();

        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        _resourceTransferService = workspaceWrapper.WorkspaceService.ResourceService.TransferService;
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;
        _dataTransferService = workspaceWrapper.WorkspaceService.DataTransferService;

        Loaded += ResourceTree_Loaded;
        Unloaded += ResourceTree_Unloaded;
    }

    private void ResourceTree_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnLoaded();
    }

    private void ResourceTree_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnUnloaded();
    }

    //
    // IResourceTree implementation
    //

    public async Task<Result> PopulateResourceTree(IResourceRegistry resourceRegistry)
    {
        // Prevent concurrent population which causes duplicate resources.
        if (_isPopulating)
        {
            return Result.Ok();
        }
        _isPopulating = true;

        try
        {
            // Save state before rebuilding
            var savedScrollOffset = GetScrollOffset();
            var selectedResourceKey = GetSelectedResource();

            try
            {
                // Rebuild the resource tree from the resource registry
                ViewModel.RebuildResourceTree();
            }
            catch (Exception ex)
            {
                return Result.Fail($"An exception occurred when populating the tree view.")
                    .WithException(ex);
            }

            // Restore selection if the resource still exists
            if (ViewModel.ResourceExists(selectedResourceKey))
            {
                await SelectResource(selectedResourceKey, scrollIntoView: false);
            }

            // Restore scroll position
            ResourceListView.UpdateLayout();
            SetScrollOffset(savedScrollOffset);

            return Result.Ok();
        }
        finally
        {
            _isPopulating = false;
        }
    }

    public ResourceKey GetSelectedResource()
    {
        return ViewModel.GetSelectedResourceKey();
    }

    public List<ResourceKey> GetSelectedResources()
    {
        return ViewModel.GetSelectedResourceKeys();
    }

    public async Task<Result> SelectResource(ResourceKey resource, bool scrollIntoView = true)
    {
        if (resource.IsEmpty)
        {
            ViewModel.SelectedItem = null;
            return Result.Ok();
        }

        // Check if the requested resource exists
        if (!ViewModel.ResourceExists(resource))
        {
            return Result.Fail($"Resource does not exist in registry: {resource}");
        }

        // Expand parent folders to make the resource visible
        ViewModel.ExpandPathToResource(resource);

        // Find and select the item
        if (!ViewModel.SetSelectedResource(resource))
        {
            return Result.Fail($"No matching tree item found for resource: '{resource}'");
        }

        if (scrollIntoView && ViewModel.SelectedItem != null)
        {
            ResourceListView.ScrollIntoView(ViewModel.SelectedItem);
        }

        return await Task.FromResult(Result.Ok());
    }

    public async Task<Result> SelectResources(List<ResourceKey> resources)
    {
        if (resources.Count == 0)
        {
            return Result.Ok();
        }

        // First, expand all parent folders to make all resources visible.
        // Each call to ExpandPathToResource rebuilds TreeItems with new instances,
        // so we must do all expansions before selecting items.
        foreach (var resource in resources)
        {
            ViewModel.ExpandPathToResource(resource);
        }

        // Clear current selection and add matching items
        ResourceListView.SelectedItems.Clear();

        var items = ViewModel.FindItemsByResourceKeys(resources);
        foreach (var item in items)
        {
            ResourceListView.SelectedItems.Add(item);
        }

        // Scroll the first item into view
        if (items.Count > 0)
        {
            ResourceListView.ScrollIntoView(items[0]);
        }

        return await Task.FromResult(Result.Ok());
    }

    //
    // Scroll position helpers
    //

    private double GetScrollOffset()
    {
        try
        {
            var scrollViewer = FindScrollViewer(ResourceListView);
            return scrollViewer?.VerticalOffset ?? 0;
        }
        catch (Exception)
        {
            // Ignore exceptions during shutdown when visual tree is being torn down
            return 0;
        }
    }

    private void SetScrollOffset(double offset)
    {
        try
        {
            var scrollViewer = FindScrollViewer(ResourceListView);
            scrollViewer?.ChangeView(null, offset, null, disableAnimation: true);
        }
        catch (Exception)
        {
            // Ignore exceptions during shutdown when visual tree is being torn down
        }
    }

    private ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        try
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer scrollViewer)
                {
                    return scrollViewer;
                }
                var result = FindScrollViewer(child);
                if (result != null)
                {
                    return result;
                }
            }
        }
        catch (Exception)
        {
            // Ignore exceptions during shutdown when visual tree is being torn down
        }
        return null;
    }

    //
    // Event handlers
    //

    private void ListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // Get the item at the tap position (works for both selectable and non-selectable items like root)
        var position = e.GetPosition(ResourceListView);
        var tappedItem = FindItemAtPosition(position);
        
        if (tappedItem != null)
        {
            OpenResource(tappedItem);
        }
    }

    private void ExpanderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        if (button.DataContext is not ResourceViewItem item)
        {
            return;
        }

        ViewModel.ToggleExpand(item);
    }

    private void OpenResource(ResourceViewItem item)
    {
        if (item.Resource is IFolderResource)
        {
            // Double-clicking root folder opens it in File Explorer
            if (item.IsRootFolder)
            {
                var resourceKey = _resourceRegistry.GetResourceKey(item.Resource);
                _commandService.Execute<IOpenFileManagerCommand>(command =>
                {
                    command.Resource = resourceKey;
                });
            }
            else
            {
                ViewModel.ToggleExpand(item);
            }
        }
        else if (item.Resource is IFileResource fileResource)
        {
            var resourceKey = _resourceRegistry.GetResourceKey(fileResource);
            if (!_documentsService.IsDocumentSupported(resourceKey))
            {
                return;
            }

            _commandService.Execute<IOpenDocumentCommand>(command =>
            {
                command.FileResource = resourceKey;
            });
        }
    }

    private void ListView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

        var selectedItem = ViewModel.SelectedItem;
        var selectedResources = ViewModel.GetSelectedResources();

        e.Handled = (ctrl, e.Key) switch
        {
            (false, VirtualKey.Delete) => HandleDelete(selectedResources),
            (false, VirtualKey.F2) => HandleRename(selectedItem),
            (false, VirtualKey.Right) => HandleExpand(selectedItem),
            (false, VirtualKey.Left) => HandleCollapse(selectedItem),
            (false, VirtualKey.Enter) => HandleOpen(selectedItem),
            (false, VirtualKey.Escape) => HandleClearSelection(),
            (true, VirtualKey.A) => HandleSelectAllSiblings(),
            (true, VirtualKey.D) => HandleDuplicate(selectedItem),
            (true, VirtualKey.C) => HandleCopy(selectedResources),
            (true, VirtualKey.X) => HandleCut(selectedResources),
            (true, VirtualKey.V) => HandlePaste(selectedItem),
            _ => false
        };
    }

    private bool HandleDelete(List<IResource> selectedResources)
    {
        if (selectedResources.Count == 0)
        {
            return false;
        }

        var resourceKeys = selectedResources
            .Select(r => _resourceRegistry.GetResourceKey(r))
            .ToList();

        _commandService.Execute<IDeleteResourceDialogCommand>(command =>
        {
            command.Resources = resourceKeys;
        });
        return true;
    }

    private bool HandleRename(ResourceViewItem? selectedItem)
    {
        if (selectedItem == null || selectedItem.IsRootFolder)
        {
            return false;
        }

        var resourceKey = _resourceRegistry.GetResourceKey(selectedItem.Resource);
        _commandService.Execute<IRenameResourceDialogCommand>(command =>
        {
            command.Resource = resourceKey;
        });
        return true;
    }

    private bool HandleExpand(ResourceViewItem? selectedItem)
    {
        if (selectedItem == null || !selectedItem.IsFolder || selectedItem.IsExpanded)
        {
            return false;
        }

        ViewModel.ExpandItem(selectedItem);
        return true;
    }

    private bool HandleCollapse(ResourceViewItem? selectedItem)
    {
        if (selectedItem == null)
        {
            return false;
        }

        if (selectedItem.IsFolder && selectedItem.IsExpanded)
        {
            ViewModel.CollapseItem(selectedItem);
            return true;
        }

        return ViewModel.SelectParentFolder();
    }

    private bool HandleOpen(ResourceViewItem? selectedItem)
    {
        if (selectedItem == null)
        {
            return false;
        }

        OpenResource(selectedItem);
        return true;
    }

    private bool HandleClearSelection()
    {
        ResourceListView.SelectedItems.Clear();
        return true;
    }

    private bool HandleSelectAllSiblings()
    {
        ResourceListView.SelectedItems.Clear();

        var siblings = ViewModel.GetSiblingItems();
        foreach (var item in siblings)
        {
            ResourceListView.SelectedItems.Add(item);
        }

        return true;
    }

    private bool HandleDuplicate(ResourceViewItem? selectedItem)
    {
        if (selectedItem == null || selectedItem.IsRootFolder)
        {
            return false;
        }

        var resourceKey = _resourceRegistry.GetResourceKey(selectedItem.Resource);
        _commandService.Execute<IDuplicateResourceDialogCommand>(command =>
        {
            command.Resource = resourceKey;
        });
        return true;
    }

    private bool HandleCopy(List<IResource> selectedResources)
    {
        if (selectedResources.Count == 0)
        {
            return false;
        }

        var resourceKeys = selectedResources
            .Select(r => _resourceRegistry.GetResourceKey(r))
            .ToList();

        _commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResources = resourceKeys;
            command.TransferMode = DataTransferMode.Copy;
        });
        return true;
    }

    private bool HandleCut(List<IResource> selectedResources)
    {
        if (selectedResources.Count == 0)
        {
            return false;
        }

        var resourceKeys = selectedResources
            .Select(r => _resourceRegistry.GetResourceKey(r))
            .ToList();

        _commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResources = resourceKeys;
            command.TransferMode = DataTransferMode.Move;
        });
        return true;
    }

    private bool HandlePaste(ResourceViewItem? selectedItem)
    {
        var destFolderResource = _resourceRegistry.GetContextMenuItemFolder(selectedItem?.Resource);
        _commandService.Execute<IPasteResourceFromClipboardCommand>(command =>
        {
            command.DestFolderResource = destFolderResource;
        });
        return true;
    }

    private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Filter out root folder from selection - it should not be selectable
        var rootItem = ResourceListView.SelectedItems
            .OfType<ResourceViewItem>()
            .FirstOrDefault(item => item.IsRootFolder);

        if (rootItem != null)
        {
            ResourceListView.SelectedItems.Remove(rootItem);
            return; // Selection changed event will fire again after removal
        }

        // Update the ViewModel with the current selection (also sends notification)
        var selectedItems = ResourceListView.SelectedItems.OfType<ResourceViewItem>().ToList();
        ViewModel.UpdateSelectedItems(selectedItems);
    }

    private void ListView_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Clear selection when tapping in empty area (not on an item)
        var position = e.GetPosition(ResourceListView);
        var clickedItem = FindItemAtPosition(position);

        if (clickedItem == null)
        {
            ResourceListView.SelectedItems.Clear();
        }
    }

    //
    // Context menu handlers
    //

    private async void ListView_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        // If right-clicking on an already-selected item, preserve the multi-selection
        // If right-clicking on an unselected item, select only that item
        // If right-clicking on root folder or empty space, track root as the clicked resource
        var position = e.GetPosition(ResourceListView);
        var clickedItem = FindItemAtPosition(position);

        IResource? clickedResource;
        if (clickedItem != null)
        {
            clickedResource = clickedItem.Resource;

            if (clickedItem.IsRootFolder)
            {
                // Root folder is not selectable, clear selection
                ResourceListView.SelectedItems.Clear();
            }
            else
            {
                bool isAlreadySelected = ResourceListView.SelectedItems.Contains(clickedItem);
                if (!isAlreadySelected)
                {
                    ViewModel.SelectedItem = clickedItem;
                }
            }
        }
        else
        {
            // Right-clicking empty space - target root folder
            clickedResource = ViewModel.RootFolder;
            ResourceListView.SelectedItems.Clear();
        }

        // Build and show the context menu
        await ShowContextMenuAsync(position, clickedResource);
    }

    private async Task ShowContextMenuAsync(Point position, IResource? clickedResource)
    {
        // Build menu context
        var context = await BuildMenuContext(clickedResource);

        // Clear existing items
        ResourceContextMenu.Items.Clear();

        // Build menu items dynamically
        var items = _menuBuilder.BuildMenuItems(context);

        // Add items to the flyout
        foreach (var item in items)
        {
            ResourceContextMenu.Items.Add(item);
        }

        // Show the menu at the pointer position
        ResourceContextMenu.ShowAt(ResourceListView, position);
    }

    private async Task<ExplorerMenuContext> BuildMenuContext(IResource? clickedResource)
    {
        var selectedResources = ViewModel.GetSelectedResources();
        var rootFolder = ViewModel.RootFolder;
        var isRootFolderTargeted = clickedResource == rootFolder;

        // Check clipboard state
        var contentDescription = _dataTransferService.GetClipboardContentDescription();
        var hasClipboardData = false;

        if (contentDescription.ContentType == ClipboardContentType.Resource)
        {
            var destFolder = ResolveDropTargetFolder(clickedResource);
            var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
            var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
            var destFolderKey = resourceRegistry.GetResourceKey(destFolder);
            
            var getResult = await _dataTransferService.GetClipboardResourceTransfer(destFolderKey);
            if (getResult.IsSuccess)
            {
                var content = getResult.Value;
                hasClipboardData = content.TransferItems.Count > 0;
            }
        }

        var context = new ExplorerMenuContext(
            ClickedResource: clickedResource,
            SelectedResources: selectedResources,
            RootFolder: rootFolder,
            IsRootFolderTargeted: isRootFolderTargeted,
            HasClipboardData: hasClipboardData,
            ClipboardContentType: contentDescription.ContentType,
            ClipboardOperation: contentDescription.ContentOperation
        );

        return context;
    }

    private IFolderResource ResolveDropTargetFolder(IResource? resource)
    {
        if (resource is IFileResource fileResource && fileResource.ParentFolder != null)
        {
            return fileResource.ParentFolder;
        }
        else if (resource is IFolderResource folderResource)
        {
            return folderResource;
        }
        return ViewModel.RootFolder;
    }

    //
    // Drag and Drop
    //

    private ResourceViewItem? _dragOverItem;

    private void ListView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        // Store the dragged items for later use, excluding root folder
        var draggedResources = new List<IResource>();
        foreach (var item in e.Items)
        {
            if (item is ResourceViewItem treeItem && !treeItem.IsRootFolder)
            {
                draggedResources.Add(treeItem.Resource);
            }
        }

        // Cancel drag if no valid items (e.g., only root folder was selected)
        if (draggedResources.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        e.Data.Properties["DraggedResources"] = draggedResources;
        e.Data.RequestedOperation = DataPackageOperation.Move;

        // Set text for drag visual - show count of items being dragged
        var count = draggedResources.Count;
        var text = count == 1
            ? draggedResources[0].Name
            : $"{count} items";

        e.Data.Properties.Add(StandardDataFormats.Text, text);
    }

    private void ListView_DragOver(object sender, DragEventArgs e)
    {
        // Clear previous highlight
        if (_dragOverItem != null)
        {
            _dragOverItem = null;
        }

        // Find the item under the cursor
        var position = e.GetPosition(ResourceListView);
        var targetItem = FindItemAtPosition(position);

        // Determine if drop is allowed
        bool canDrop = false;

        // Check for internal drag (from our ListView)
        if (e.Data?.Properties?.ContainsKey("DraggedResources") == true)
        {
            // Internal drag - check if target is valid (folder or file)
            if (targetItem?.Resource is IFolderResource or IFileResource)
            {
                canDrop = true;
                _dragOverItem = targetItem;
            }
        }
        // Check for external drag (from File Explorer, etc.)
        else if (e.DataView?.Contains(StandardDataFormats.StorageItems) == true)
        {
            // External drag - allow drop on folder, file (uses parent), or empty space (root folder)
            canDrop = true;
            _dragOverItem = targetItem;
        }

        // Update cursor and accepted operation
        if (canDrop)
        {
            // For internal drags, check if Ctrl is pressed for copy operation
            if (e.Data?.Properties?.ContainsKey("DraggedResources") == true)
            {
                var isControlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                    .HasFlag(CoreVirtualKeyStates.Down);

                e.AcceptedOperation = isControlPressed
                    ? DataPackageOperation.Copy
                    : DataPackageOperation.Move;
                e.DragUIOverride.Caption = e.AcceptedOperation == DataPackageOperation.Copy ? "Copy" : "Move";
            }
            else
            {
                // External drag - always copy
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "Copy";
            }

            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsContentVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.DragUIOverride.Caption = "Cannot drop here";
            e.DragUIOverride.IsCaptionVisible = true;
        }

        e.Handled = true;
    }

    private void ListView_Drop(object sender, DragEventArgs e)
    {
        // Clear drag-over highlight
        _dragOverItem = null;

        e.Handled = true;

        // Find the drop target
        var position = e.GetPosition(ResourceListView);
        var dropTargetItem = FindItemAtPosition(position);
        var destFolder = ResolveDropTargetFolder(dropTargetItem?.Resource);

        // Check if this is an internal drag (from our ListView)
        if (e.Data?.Properties?.TryGetValue("DraggedResources", out var draggedObj) == true &&
            draggedObj is List<IResource> draggedResources)
        {
            MoveResourcesToFolder(draggedResources, destFolder);
            return;
        }

        // Handle external drop (from file explorer)
        if (e.DataView?.Contains(StandardDataFormats.StorageItems) == true)
        {
            _ = ProcessExternalDrop(e.DataView, destFolder);
        }
    }

    private void MoveResourcesToFolder(List<IResource> resources, IFolderResource destFolder)
    {
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

    private async Task ProcessExternalDrop(DataPackageView dataView, IFolderResource destFolder)
    {
        var sourcePaths = new List<string>();
        var items = await dataView.GetStorageItemsAsync();
        if (items.Count == 0)
        {
            return;
        }

        foreach (var item in items)
        {
            if (item is StorageFile storageFile)
            {
                sourcePaths.Add(Path.GetFullPath(storageFile.Path));
            }
            else if (item is StorageFolder storageFolder)
            {
                sourcePaths.Add(Path.GetFullPath(storageFolder.Path));
            }
        }

        var destFolderResource = _resourceRegistry.GetResourceKey(destFolder);
        var createResult = _resourceTransferService.CreateResourceTransfer(
            sourcePaths,
            destFolderResource,
            DataTransferMode.Copy);

        if (createResult.IsFailure)
        {
            return;
        }

        var resourceTransfer = createResult.Value;
        _resourceTransferService.TransferResources(destFolderResource, resourceTransfer);
    }

    private ResourceViewItem? FindItemAtPosition(Point position)
    {
        foreach (var item in ViewModel.TreeItems)
        {
            var container = ResourceListView.ContainerFromItem(item) as ListViewItem;
            if (container != null)
            {
                var bounds = container.TransformToVisual(ResourceListView)
                    .TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
                if (bounds.Contains(position))
                {
                    return item;
                }
            }
        }
        return null;
    }

    //
    // Public methods for toolbar actions (called from ExplorerPanel)
    //

    /// <summary>
    /// Adds a file to the currently selected folder (or root if nothing selected).
    /// </summary>
    public void AddFileToSelectedFolder()
    {
        var destFolder = ViewModel.GetSelectedResourceFolder() ?? ViewModel.RootFolder;
        var destFolderResource = _resourceRegistry.GetResourceKey(destFolder);

        _commandService.Execute<IAddResourceDialogCommand>(command =>
        {
            command.ResourceType = ResourceType.File;
            command.DestFolderResource = destFolderResource;
        });
    }

    /// <summary>
    /// Adds a folder to the currently selected folder (or root if nothing selected).
    /// </summary>
    public void AddFolderToSelectedFolder()
    {
        var destFolder = ViewModel.GetSelectedResourceFolder() ?? ViewModel.RootFolder;
        var destFolderResource = _resourceRegistry.GetResourceKey(destFolder);

        _commandService.Execute<IAddResourceDialogCommand>(command =>
        {
            command.ResourceType = ResourceType.Folder;
            command.DestFolderResource = destFolderResource;
        });
    }

    /// <summary>
    /// Collapses all folders in the tree view.
    /// </summary>
    public void CollapseAllFolders()
    {
        ViewModel.CollapseAllFolders();
    }
}
