using Celbridge.Projects.ViewModels;
using Celbridge.Resources;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.Projects.Views;

public sealed partial class ResourceTreeView : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IMessengerService _messengerService;

    public ResourceTreeViewModel ViewModel { get; }
    private LocalizedString AddString => _stringLocalizer.GetString("ResourceTree_Add");
    private LocalizedString FolderString => _stringLocalizer.GetString("ResourceTree_Folder");
    private LocalizedString FileString => _stringLocalizer.GetString("ResourceTree_File");
    private LocalizedString EditString => _stringLocalizer.GetString("ResourceTree_Edit");
    private LocalizedString CutString => _stringLocalizer.GetString("ResourceTree_Cut");
    private LocalizedString CopyString => _stringLocalizer.GetString("ResourceTree_Copy");
    private LocalizedString PasteString => _stringLocalizer.GetString("ResourceTree_Paste");
    private LocalizedString DeleteString => _stringLocalizer.GetString("ResourceTree_Delete");
    private LocalizedString RenameString => _stringLocalizer.GetString("ResourceTree_Rename");

    public ResourceTreeView()
    {
        this.InitializeComponent();

        var serviceProvider = ServiceLocator.ServiceProvider;
        ViewModel = serviceProvider.GetRequiredService<ResourceTreeViewModel>();
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();
        _messengerService = serviceProvider.GetRequiredService<IMessengerService>();

        Loaded += ResourceTreeView_Loaded;
        Unloaded += ResourceTreeView_Unloaded;
    }

    private void ResourceTreeView_Loaded(object sender, RoutedEventArgs e)
    {
        _messengerService.Register<ResourceRegistryUpdatedMessage>(this, OnResourceRegistryUpdatedMessage);

        ResourcesTreeView.Collapsed += ResourcesTreeView_Collapsed;
        ResourcesTreeView.Expanding += ResourcesTreeView_Expanding;
        ViewModel.OnLoaded();
    }

    private void ResourceTreeView_Unloaded(object sender, RoutedEventArgs e)
    {
        _messengerService.Unregister<ResourceRegistryUpdatedMessage>(this);

        ViewModel.OnUnloaded();
    }

    private void OnResourceRegistryUpdatedMessage(object recipient, ResourceRegistryUpdatedMessage message)
    {
        UpdateTreeViewNodes();
    }

    private void UpdateTreeViewNodes()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        var workspaceWrapper = serviceProvider.GetRequiredService<IWorkspaceWrapper>();
        // workspaceWrapper.IsWorkspacePageLoaded may still be null here when called during project load

        var resourceRegistry = workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry;
        var rootFolder = resourceRegistry.RootFolder;
        var rootNodes = ResourcesTreeView.RootNodes;

        // Clear existing nodes
        rootNodes.Clear();

        // Generate the root nodes for the root resources
        foreach (var resource in rootFolder.Children)
        {
            if (resource is IFileResource fileResource)
            {
                var fileNode = new TreeViewNode
                {
                    Content = fileResource
                };
                rootNodes.Add(fileNode);
            }
            else if (resource is IFolderResource folderResource)
            {
                AddNodes(rootNodes, folderResource);
            }
        }

        void AddNodes(IList<TreeViewNode> parentNodes, IFolderResource folder)
        {
            var resourceKey = resourceRegistry.GetResourceKey(folder);
            var isExpanded = folder.IsExpanded;

            var folderNode = new TreeViewNode
            {
                Content = folder,
                IsExpanded = isExpanded
            };

            // Add the folder node
            parentNodes.Add(folderNode);

            // Recursively add children
            var children = folder.Children.OrderBy(c => c is IFolderResource ? 0 : 1).ThenBy(c => c.Name);
            foreach (var child in children)
            {
                if (child is IFolderResource childFolder)
                {
                    AddNodes(folderNode.Children, childFolder);
                }
                else if (child is IFileResource childFile)
                {
                    var fileNode = new TreeViewNode
                    {
                        Content = childFile
                    };
                    folderNode.Children.Add(fileNode);
                }
            }
        }
    }

    private void AddFolder(object? sender, RoutedEventArgs e)
    {
        var resource = AcquireResource(sender);

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

    private void AddFile(object? sender, RoutedEventArgs e)
    {
        var resource = AcquireResource(sender);
        Guard.IsNotNull(resource);

        if (resource is IFolderResource destFolder)
        {
            // Add a file to the selected folder
            ViewModel.ShowAddResourceDialog(ResourceType.File, destFolder);
        }
        else if (resource is IFileResource destFile)
        {
            // Add a file to the folder containing the selected file
            Guard.IsNotNull(destFile.ParentFolder);

            ViewModel.ShowAddResourceDialog(ResourceType.File, destFile.ParentFolder);
        }
        else
        {
            // Add a file resource to the root folder
            ViewModel.ShowAddResourceDialog(ResourceType.File, null);
        }
    }

    private void CutResource(object sender, RoutedEventArgs e)
    {
        var resource = AcquireResource(sender);
        Guard.IsNotNull(resource);

        ViewModel.CutResourceToClipboard(resource);
    }

    private void CopyResource(object sender, RoutedEventArgs e)
    {
        var resource = AcquireResource(sender);
        Guard.IsNotNull(resource);

        ViewModel.CopyResourceToClipboard(resource);
    }

    private void PasteResource(object sender, RoutedEventArgs e)
    {
        var destResource = AcquireResource(sender);

        // Resource is permitted to be null here (indicates the root folder)
        ViewModel.PasteResourceFromClipboard(destResource);
    }

    private void DeleteResource(object? sender, RoutedEventArgs e)
    {
        var resource = AcquireResource(sender);
        Guard.IsNotNull(resource);

        ViewModel.ShowDeleteResourceDialog(resource);
    }

    private void RenameResource(object? sender, RoutedEventArgs e)
    {
        var resource = AcquireResource(sender);
        Guard.IsNotNull(resource);

        ViewModel.ShowRenameResourceDialog(resource);
    }

    private void OpenResource(object? sender, RoutedEventArgs e)
    {
        var element = sender as FrameworkElement;
        Guard.IsNotNull(element);
    }

    private void ResourcesTreeView_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        // Only folder resources can be expanded
        if (args.Item is IFolderResource folderResource)
        {
            ViewModel.SetFolderIsExpanded(folderResource, true);
        }
    }

    private void ResourcesTreeView_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        // Only folder resources can be expanded
        if (args.Item is IFolderResource folderResource)
        {
            ViewModel.SetFolderIsExpanded(folderResource, false);
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

        if (args.NewParentItem is not TreeViewNode newParentNode)
        {
            return;
        }

        // A null newParent indicates that the dragged items are being moved to the root folder
        IFolderResource? newParent = null;
        if (newParentNode.Content is IFileResource fileResource)
        {
            newParent = fileResource.ParentFolder;
        }
        else if (newParentNode.Content is IFolderResource folderResource)
        {
            newParent = folderResource;
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
        var resource = AcquireResource(sender);
        ViewModel.OnContextMenuOpening(resource);
    }

    private IResource? AcquireResource(object? obj)
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
}
