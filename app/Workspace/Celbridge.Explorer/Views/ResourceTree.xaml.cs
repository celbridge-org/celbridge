using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Documents;
using Celbridge.Explorer.Menu;
using Celbridge.Explorer.Models;
using Celbridge.Explorer.ViewModels;
using Celbridge.UserInterface.ContextMenu;
using Celbridge.Workspace;
using Windows.Foundation;

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
        ViewModel.SelectionRequested += OnSelectionRequested;
    }

    private void ResourceTree_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectionRequested -= OnSelectionRequested;
        ViewModel.OnUnloaded();
    }

    private void OnSelectionRequested(List<ResourceKey> resourceKeys)
    {
        ResourceListView.SelectedItems.Clear();

        var items = ViewModel.FindItemsByResourceKeys(resourceKeys);
        foreach (var item in items)
        {
            ResourceListView.SelectedItems.Add(item);
        }
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

        // Ensure ListView keeps focus for keyboard navigation
        ResourceListView.Focus(FocusState.Programmatic);
    }

    //
    // Helper methods
    //

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
