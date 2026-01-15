using Celbridge.Explorer.ViewModels;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.Explorer.Views;

public sealed partial class ResourceTreeView : UserControl, IResourceTreeView
{
    private readonly IStringLocalizer _stringLocalizer;
    private IResourceRegistry? _resourceRegistry;
    private bool _isPopulating;

    public ResourceTreeViewModel ViewModel { get; }
    private string RunString => _stringLocalizer.GetString("ResourceTree_Run");
    private string OpenString => _stringLocalizer.GetString("ResourceTree_Open");
    private string AddFileString => _stringLocalizer.GetString("ResourceTree_AddFile");
    private string AddFolderString => _stringLocalizer.GetString("ResourceTree_AddFolder");
    private string CutString => _stringLocalizer.GetString("ResourceTree_Cut");
    private string CopyString => _stringLocalizer.GetString("ResourceTree_Copy");
    private string PasteString => _stringLocalizer.GetString("ResourceTree_Paste");
    private string DeleteString => _stringLocalizer.GetString("ResourceTree_Delete");
    private string RenameString => _stringLocalizer.GetString("ResourceTree_Rename");
    private string OpenFileExplorerString => _stringLocalizer.GetString("ResourceTree_OpenFileExplorer");
    private string OpenApplicationString => _stringLocalizer.GetString("ResourceTree_OpenApplication");
    private string CopyResourceKeyString => _stringLocalizer.GetString("ResourceTree_CopyResourceKey");
    private string CopyFilePathString => _stringLocalizer.GetString("ResourceTree_CopyFilePath");

    public ResourceTreeView()
    {
        this.InitializeComponent();

        ViewModel = ServiceLocator.AcquireService<ResourceTreeViewModel>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        Loaded += ResourceTreeView_Loaded;
        Unloaded += ResourceTreeView_Unloaded;
    }

    private void ResourceTreeView_Loaded(object sender, RoutedEventArgs e)
    {
        ResourcesTreeView.Collapsed += ResourcesTreeView_Collapsed;
        ResourcesTreeView.Expanding += ResourcesTreeView_Expanding;

        ViewModel.OnLoaded(this);
    }

    private void ResourceTreeView_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnUnloaded();
    }

    public async Task<Result> PopulateTreeView(IResourceRegistry resourceRegistry)
    {
        // Prevent concurrent population which causes duplicate resources in the tree view.
        // This can happen because OnLoaded calls PopulateTreeView, and shortly after,
        // WorkspaceLoader.LoadWorkspaceAsync also calls UpdateResourcesAsync which calls PopulateTreeView.
        // The Task.Delay(10) below creates a window where a second call can interleave.
        if (_isPopulating)
        {
            return Result.Ok();
        }
        _isPopulating = true;

        try
        {
            _resourceRegistry = resourceRegistry;

            // Note: This method is called while loading the workspace, so workspaceWrapper.IsWorkspacePageLoaded
            // may be false here. This is ok, because the ResourceRegistry has been initialized at this point.

            var rootFolder = _resourceRegistry.RootFolder;
            var rootNodes = ResourcesTreeView.RootNodes;

            // Make a note of the currently selected resource
            var selectedResourceKey = GetSelectedResource();

            try
            {
                // Clear existing nodes
                rootNodes.Clear();

                // I don't know why, but if this delay is not here, the TreeView can get into a corrupted state with
                // resources missing their icons. This happens in particular when undo/redoing resource operations quickly.
                // I tried many combinations of clearing the nodes, calling UpdateLayout() and using rootNodes.ReplaceWith().
                // This is the only solution that has worked consistently.
                // I tried reducing the delay down to 1ms, but that still caused the issue.
                // Unfortunately this causes a visible flicker when updating the TreeView, but it's more important that it works robustly.
                await Task.Delay(10);

                // Recursively populate the Tree View
                PopulateTreeViewNodes(rootNodes, rootFolder.Children);
            }
            catch (Exception ex)
            {
                return Result.Fail($"An exception occurred when populating the tree view.")
                    .WithException(ex); ;
            }

            // Attempt to re-select the previously selected resource
            if (_resourceRegistry.GetResource(selectedResourceKey) != null)
            {
                await SetSelectedResource(selectedResourceKey);
            }

            return Result.Ok();
        }
        finally
        {
            _isPopulating = false;
        }
    }

    public ResourceKey GetSelectedResource()
    {
        ResourceKey selectedResourceKey = new();
        var selectedItem = ResourcesTreeView.SelectedItem as TreeViewNode;
        if (selectedItem != null)
        {
            var selectedResource = selectedItem.Content as IResource;
            if (selectedResource != null)
            {
                Guard.IsNotNull(_resourceRegistry);
                selectedResourceKey = _resourceRegistry.GetResourceKey(selectedResource);
            }
        }

        return selectedResourceKey;
    }

    public async Task<Result> SetSelectedResource(ResourceKey resource)
    {
        // If the resource registry hasn't been set yet (tree not populated), return early
        if (_resourceRegistry is null)
        {
            return Result.Ok();
        }

        if (resource.IsEmpty)
        {
            // An empty resource key indicates that no resource should be selected
            ResourcesTreeView.SelectedItem = null;
            return Result.Ok();
        }

        // Check if the requested resource exists in the registry
        var getResult = _resourceRegistry.GetResource(resource);
        if (getResult.IsFailure)
        {
            return Result.Fail($"Failed to get resource from resource registry: {resource}")
                .WithErrors(getResult);
        }

        var segments = resource.ToString().Split('/');
        Guard.IsTrue(segments.Length >= 1);

        var node = FindNode(0, ResourcesTreeView.RootNodes);

        if (node is null)
        {
            return Result.Fail($"No matching tree node found for resource: '{resource}'");
        }

        ResourcesTreeView.SelectedItem = node;

        // Scroll the list view to bring the selected item into view
        var itemContainer = (TreeViewItem)ResourcesTreeView.ContainerFromNode(node);
        if (itemContainer != null)
        {
            itemContainer.StartBringIntoView();
        }

        await Task.CompletedTask;

        return Result.Ok();

        TreeViewNode? FindNode(int segmentIndex, IList<TreeViewNode> nodes)
        {
            string segment = segments[segmentIndex];
            foreach (var node in nodes)
            {
                var resource = node.Content as IResource;
                Guard.IsNotNull(resource);

                if (resource.Name == segment)
                {
                    if (segmentIndex == segments.Length - 1)
                    {
                        // Found the required node
                        return node;
                    }

                    if (resource is IFolderResource &&
                        node.HasChildren)
                    {
                        // Force the node to expand so it's children are realized
                        node.IsExpanded = true;

                        // We've matched this segment with a folder, now check the next segment
                        return FindNode(segmentIndex + 1, node.Children);
                    }
                }
            }

            return null;
        }
    }

    private void PopulateTreeViewNodes(IList<TreeViewNode> nodes, IList<IResource> resources)
    {
        Guard.IsNotNull(_resourceRegistry);

        foreach (var resource in resources)
        {
            if (resource is IFolderResource folderResource)
            {
                var resourceKey = _resourceRegistry.GetResourceKey(folderResource);
                var isExpanded = _resourceRegistry.IsFolderExpanded(resourceKey);

                var folderNode = new TreeViewNode
                {
                    Content = folderResource,
                    IsExpanded = isExpanded,
                };
                AutomationProperties.SetName(folderNode, folderResource.Name);

                if (folderResource.Children.Count > 0)
                {
                    if (folderResource.IsExpanded)
                    {
                        PopulateTreeViewNodes(folderNode.Children, folderResource.Children);
                    }
                    else
                    {
                        // The child nodes will only be populated if the user expands the folder
                        folderNode.HasUnrealizedChildren = true;
                    }
                }

                nodes.Add(folderNode);
            }
            else if (resource is IFileResource fileResource)
            {
                var fileNode = new TreeViewNode
                {
                    Content = fileResource
                };
                AutomationProperties.SetName(fileNode, fileResource.Name);

                nodes.Add(fileNode);
            }
        }
    }

    private void TreeViewItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var element = sender as FrameworkElement;
        Guard.IsNotNull(element);

        var treeViewNode = element.DataContext as TreeViewNode;
        Guard.IsNotNull(treeViewNode);

        var resource = treeViewNode.Content as IResource;
        Guard.IsNotNull(resource);

        OpenResource(resource, treeViewNode);
    }

    private void ResourceContextMenu_Run(object sender, RoutedEventArgs e)
    {
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);

        var treeViewNode = menuFlyoutItem.DataContext as TreeViewNode;
        Guard.IsNotNull(treeViewNode);

        var resource = treeViewNode.Content as IResource;
        Guard.IsNotNull(resource);

        RunScriptResource(resource, treeViewNode);
    }

    private void ResourceContextMenu_Open(object? sender, RoutedEventArgs e)
    {
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);

        var treeViewNode = menuFlyoutItem.DataContext as TreeViewNode;
        Guard.IsNotNull(treeViewNode);

        var resource = treeViewNode.Content as IResource;
        Guard.IsNotNull(resource);

        OpenResource(resource, treeViewNode);
    }

    private void RunScriptResource(IResource resource, TreeViewNode node)
    {
        if (resource is IFileResource scriptResource)
        {
            ViewModel.RunScript(scriptResource);
        }
    }

    private void OpenResource(IResource resource, TreeViewNode node)
    { 
        if (resource is IFolderResource)
        {
            // Opening a folder resource toggles the expanded state
            node.IsExpanded = !node.IsExpanded;
        }
        else if (resource is IFileResource fileResource)
        {
            ViewModel.OpenDocument(fileResource);
        }
    }

    private void ResourceContextMenu_AddFolder(object? sender, RoutedEventArgs e)
    {
        var resource = AcquireContextMenuResource(sender);

        if (resource is IFolderResource destFolder)
        {
            // Add a folder to the selected folder
            ViewModel.ShowAddResourceDialog(ResourceType.Folder, destFolder);
        }
        else if (resource is IFileResource destFile)
        {
            // Add a folder to the folder containing the selected file
            Guard.IsNotNull(destFile.ParentFolder);

            ViewModel.ShowAddResourceDialog(ResourceType.Folder, destFile.ParentFolder);
        }
        else
        {
            // Add a folder resource to the root folder
            ViewModel.ShowAddResourceDialog(ResourceType.Folder, null);
        }
    }

    private void ResourceContextMenu_AddFile(object? sender, RoutedEventArgs e)
    {
        var resource = AcquireContextMenuResource(sender);

        if (resource is IFolderResource destFolder)
        {
            // Add a file to the selected folder
            ViewModel.ShowAddResourceDialog(ResourceType.File, destFolder);
            return;
        }
        else if (resource is IFileResource destFile)
        {
            Guard.IsNotNull(destFile.ParentFolder);

            // Add a file to the folder containing the selected file
            ViewModel.ShowAddResourceDialog(ResourceType.File, destFile.ParentFolder);
        }
        else
        {
            // Add a file resource to the root folder
            ViewModel.ShowAddResourceDialog(ResourceType.File, null);
        }
    }

    private void ResourceContextMenu_Cut(object sender, RoutedEventArgs e)
    {
        var resource = AcquireContextMenuResource(sender);
        Guard.IsNotNull(resource);

        ViewModel.CutResourceToClipboard(resource);
    }

    private void ResourceContextMenu_Copy(object sender, RoutedEventArgs e)
    {
        var resource = AcquireContextMenuResource(sender);
        Guard.IsNotNull(resource);

        ViewModel.CopyResourceToClipboard(resource);
    }

    private void ResourceContextMenu_Paste(object sender, RoutedEventArgs e)
    {
        var destResource = AcquireContextMenuResource(sender);

        // Resource is permitted to be null here (indicates the root folder)
        ViewModel.PasteResourceFromClipboard(destResource);
    }

    private void ResourceContextMenu_Delete(object? sender, RoutedEventArgs e)
    {
        var resource = AcquireContextMenuResource(sender);
        Guard.IsNotNull(resource);

        ViewModel.ShowDeleteResourceDialog(resource);
    }

    private void ResourceContextMenu_Rename(object? sender, RoutedEventArgs e)
    {
        var resource = AcquireContextMenuResource(sender);
        Guard.IsNotNull(resource);

        ViewModel.ShowRenameResourceDialog(resource);
    }

    private void ResourceContextMenu_OpenFileExplorer(object sender, RoutedEventArgs e)
    {
        // A null resource here indicates the root folder
        var resource = AcquireContextMenuResource(sender);
        ViewModel.OpenResourceInExplorer(resource);
    }

    private void ResourceContextMenu_OpenApplication(object sender, RoutedEventArgs e)
    {
        // A null resource here indicates the root folder
        var resource = AcquireContextMenuResource(sender);

        ViewModel.OpenResourceInApplication(resource);
    }

    private void ResourceContextMenu_CopyResourceKey(object sender, RoutedEventArgs e)
    {
        var resource = AcquireContextMenuResource(sender);
        Guard.IsNotNull(resource);

        ViewModel.CopyResourceKeyToClipboard(resource);
    }

    private void ResourceContextMenu_CopyFilePath(object sender, RoutedEventArgs e)
    {
        var resource = AcquireContextMenuResource(sender);
        Guard.IsNotNull(resource);

        ViewModel.CopyFilePathToClipboard(resource);
    }

    private void ResourcesTreeView_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        // Only folder resources can be expanded
        if (args.Item is IFolderResource folderResource)
        {
            folderResource.IsExpanded = true;
            ViewModel.SetFolderIsExpanded(folderResource, true);

            if (args.Node is TreeViewNode folderNode &&
                folderNode.HasUnrealizedChildren)
            {
                // Lazy populate the child nodes
                folderNode.HasUnrealizedChildren = false;
                PopulateTreeViewNodes(folderNode.Children, folderResource.Children);
            }
        }
    }

    private void ResourcesTreeView_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        // Only folder resources can be collapsed
        if (args.Item is IFolderResource folderResource)
        {
            folderResource.IsExpanded = false;
            ViewModel.SetFolderIsExpanded(folderResource, false);

            // Deleting the child nodes here and setting the folder to HasUnrealizedChildren = true would
            // seem like a good idea, but in practice this causes the TreeView to go haywire and resource icons start
            // disappearing. It's not really necessary to delete the child nodes here anyway, because all collapsed folders
            // will be set to HasUnrealizedChildren = true with 0 children the next time the registry updates.
        }
    }

    private void TreeView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

        if (e.Key == VirtualKey.Delete)
        {
            if (ResourcesTreeView.SelectedItem is TreeViewNode treeViewNode &&
                treeViewNode.Content is IResource resource)
            {
                ViewModel.ShowDeleteResourceDialog(resource);
            }
        }
        else if (control)
        {
            var treeViewNode = ResourcesTreeView.SelectedItem as TreeViewNode;
            if (treeViewNode is not null)
            {
                var selectedResource = treeViewNode.Content as IResource;
                if (selectedResource is not null)
                {
                    if (e.Key == VirtualKey.C)
                    {
                        ViewModel.CopyResourceToClipboard(selectedResource);
                    }
                    else if (e.Key == VirtualKey.X)
                    {
                        ViewModel.CutResourceToClipboard(selectedResource);
                    }
                }

                // selectedResource is permitted to be null here (indicates the root folder)
                if (e.Key == VirtualKey.V)
                {
                    ViewModel.PasteResourceFromClipboard(selectedResource);
                }
            }
        }
    }

    private void ResourcesTreeView_DragItemsCompleted(TreeView sender, TreeViewDragItemsCompletedEventArgs args)
    {
        var draggedItems = args.Items.ToList();

        TreeViewNode? newParentNode = args.NewParentItem as TreeViewNode;

        // A null newParent indicates that the dragged items are being moved to the root folder
        IFolderResource? newParent = null;
        if (newParentNode is not null)
        {
            if (newParentNode.Content is IFileResource fileResource)
            {
                newParent = fileResource.ParentFolder;
            }
            else if (newParentNode.Content is IFolderResource folderResource)
            {
                newParent = folderResource;
            }
        }

        var resources = new List<IResource>();
        foreach (var item in draggedItems)
        {
            if (item is not TreeViewNode itemNode)
            {
                continue;
            }

            if (itemNode.Content is IResource resource)
            {
                resources.Add(resource);
            }
        }

        ViewModel.MoveResourcesToFolder(resources, newParent);
    }

    private void ResourceContextMenu_Opening(object sender, object e)
    {
        var resource = AcquireContextMenuResource(sender);
        ViewModel.OnContextMenuOpening(resource);
    }

    private IResource? AcquireContextMenuResource(object? obj)
    {
        IResource? resource = null;
        if (obj is null)
        {
            return null;
        }
        else if (obj is MenuFlyoutItem menuFlyoutItem)
        {
            var treeViewNode = menuFlyoutItem.DataContext as TreeViewNode;
            if (treeViewNode == null)
            {
                // Resource is permitted to be null here (indicates the root folder)
                return null;
            }

            resource = treeViewNode.Content as IResource;
        }
        else if (obj is MenuFlyout menuFlyout)
        {
            var target = menuFlyout.Target;
            Guard.IsNotNull(target);

            var treeViewNode = target.DataContext as TreeViewNode;

            // Resource is permitted to be null here (indicates the root folder)
            if (treeViewNode != null)
            {
                resource = treeViewNode.Content as IResource;
            }
        }
        return resource;
    }

    private void ResourcesTreeView_DragOver(object sender, DragEventArgs e)
    {
        // Accept the drag and drop content
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.Handled = true;
    }

    private void ResourcesTreeView_Drop(object sender, DragEventArgs e)
    {
        Guard.IsNotNull(_resourceRegistry);

        e.Handled = true;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        // Find the destination resource that the item was dropped over
        IResource? destResource = null;
        var position = e.GetPosition(ResourcesTreeView);
        TreeViewNode? dropTargetNode = FindNodeAtPosition(ResourcesTreeView, position);
        if (dropTargetNode is null)
        {
            destResource = _resourceRegistry.RootFolder;
        }
        else
        {
            destResource = dropTargetNode.Content as IResource;
        }

        _ = ProcessDroppedItems();

        async Task ProcessDroppedItems()
        {
            List<string> sourcePaths = new();
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.Count == 0)
            {
                return;
            }

            foreach (var item in items)
            {
                if (item is StorageFile storageFile)
                {
                    var filePath = Path.GetFullPath(storageFile.Path);
                    sourcePaths.Add(filePath);
                }
                else if (item is StorageFolder storageFolder)
                {
                    var folderPath = Path.GetFullPath(storageFolder.Path);
                    sourcePaths.Add(folderPath);
                }
            }

            _ = ViewModel.ImportResources(sourcePaths, destResource);
        }
    }

    private TreeViewNode? FindNodeAtPosition(TreeView treeView, Point position)
    {
        TreeViewNode? CheckNode(TreeViewNode checkNode)
        {
            var container = treeView.ContainerFromNode(checkNode) as TreeViewItem;
            if (container != null)
            {
                var bounds = container.TransformToVisual(treeView).TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
                if (bounds.Contains(position))
                {
                    return checkNode;
                }

                if (checkNode.HasChildren && checkNode.IsExpanded)
                {
                    foreach (var childNode in checkNode.Children)
                    {
                        var foundNode = CheckNode(childNode);
                        if (foundNode is not null)
                        {
                            return foundNode;
                        }
                    }
                }
            }

            return null;
        }

        foreach (var node in treeView.RootNodes)
        {
            var foundNode = CheckNode(node);
            if (foundNode is not null)
            {
                return foundNode;
            }
        }

        return null;
    }

    private void ResourcesTreeView_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        Guard.IsNotNull(_resourceRegistry);

        var node = args.AddedItems.FirstOrDefault() as TreeViewNode;
        if (node is null ||
            node.Content is null)
        {
            ViewModel.OnSelectedResourceChanged(ResourceKey.Empty);
            return;
        }

        var resource = node.Content as IResource;
        Guard.IsNotNull(resource);

        var selectedResource = _resourceRegistry.GetResourceKey(resource);
        ViewModel.OnSelectedResourceChanged(selectedResource);
    }

    //
    // Public methods for toolbar actions (called from ExplorerPanel)
    //

    /// <summary>
    /// Adds a file to the currently selected folder (or root if nothing selected).
    /// </summary>
    public void AddFileToSelectedFolder()
    {
        var destFolder = GetSelectedResourceFolder();
        ViewModel.ShowAddResourceDialog(ResourceType.File, destFolder);
    }

    /// <summary>
    /// Adds a folder to the currently selected folder (or root if nothing selected).
    /// </summary>
    public void AddFolderToSelectedFolder()
    {
        var destFolder = GetSelectedResourceFolder();
        ViewModel.ShowAddResourceDialog(ResourceType.Folder, destFolder);
    }

    /// <summary>
    /// Returns the folder resource for the currently selected resource.
    /// If a file is selected, returns its parent folder.
    /// If a folder is selected, returns that folder.
    /// If nothing is selected, returns null (indicating root folder).
    /// </summary>
    private IFolderResource? GetSelectedResourceFolder()
    {
        var selectedItem = ResourcesTreeView.SelectedItem as TreeViewNode;
        if (selectedItem?.Content is IFolderResource folderResource)
        {
            return folderResource;
        }
        else if (selectedItem?.Content is IFileResource fileResource)
        {
            return fileResource.ParentFolder;
        }
        return null;
    }

    /// <summary>
    /// Collapses all folders in the tree view, including nested children.
    /// </summary>
    public void CollapseAllFolders()
    {
        CollapseNodesRecursively(ResourcesTreeView.RootNodes);
    }

    private void CollapseNodesRecursively(IList<TreeViewNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Content is IFolderResource folderResource)
            {
                if (node.IsExpanded)
                {
                    // Collapse children first
                    CollapseNodesRecursively(node.Children);

                    // Then collapse this node
                    node.IsExpanded = false;

                    // Update the folder state in the registry
                    folderResource.IsExpanded = false;
                    ViewModel.SetFolderIsExpanded(folderResource, false);
                }
            }
        }
    }
}
