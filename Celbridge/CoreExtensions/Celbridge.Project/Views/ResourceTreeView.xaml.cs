using Celbridge.BaseLibrary.Resources;
using Celbridge.Project.Models;
using Celbridge.Project.ViewModels;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;
using Windows.System;

namespace Celbridge.Project.Views;

public sealed partial class ResourceTreeView : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

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

        ResourcesTreeView.Collapsed += ResourcesTreeView_Collapsed;
        ResourcesTreeView.Expanding += ResourcesTreeView_Expanding;
    }

    private void AddFolder(object? sender, RoutedEventArgs e)
    {
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);

        if (menuFlyoutItem.DataContext is IFolderResource folderResource)
        {
            // Add a folder to the selected folder
            ViewModel.AddFolder(folderResource);
        }
        else if (menuFlyoutItem.DataContext is IFileResource fileResource)
        {
            // Add a folder to the folder containing the selected file
            var parentFolder = fileResource.ParentFolder;
            Guard.IsNotNull(parentFolder);

            ViewModel.AddFolder(parentFolder);
        }
        else
        {
            // Add a folder resource to the root folder
            ViewModel.AddFolder(null);
        }
    }

    private void AddFile(object? sender, RoutedEventArgs e)
    {
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);

        if (menuFlyoutItem.DataContext is IFolderResource folderResource)
        {
            // Add a file to the selected folder
            ViewModel.AddFile(folderResource);
        }
        else if (menuFlyoutItem.DataContext is IFileResource fileResource)
        {
            // Add a file to the folder containing the selected file
            var parentFolder = fileResource.ParentFolder;
            Guard.IsNotNull(parentFolder);

            ViewModel.AddFile(parentFolder);
        }
        else
        {
            // Add a file resource to the root folder
            ViewModel.AddFile(null);
        }
    }

    private void CutResource(object sender, RoutedEventArgs e)
    {
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);

        var resource = menuFlyoutItem.DataContext as IResource;
        Guard.IsNotNull(resource);

        ViewModel.CutResource(resource);
    }

    private void CopyResource(object sender, RoutedEventArgs e)
    {
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);

        var resource = menuFlyoutItem.DataContext as IResource;
        Guard.IsNotNull(resource);

        ViewModel.CopyResource(resource);
    }

    private void PasteResource(object sender, RoutedEventArgs e)
    {
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);

        var resource = menuFlyoutItem.DataContext as IResource;
        Guard.IsNotNull(resource);

        ViewModel.PasteResource(resource);
    }

    private void DeleteResource(object? sender, RoutedEventArgs e)
    {
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);

        if (menuFlyoutItem.DataContext is FolderResource folderResource)
        {
            // Delete the selected folder
            ViewModel.DeleteFolder(folderResource);
        }
        else if (menuFlyoutItem.DataContext is FileResource fileResource)
        {
            // Delete the selected file
            ViewModel.DeleteFile(fileResource);
        }
    }

    private void RenameResource(object? sender, RoutedEventArgs e)
    {
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);

        if (menuFlyoutItem.DataContext is FolderResource folderResource)
        {
            ViewModel.RenameFolder(folderResource);
        }
        else if (menuFlyoutItem.DataContext is FileResource fileResource)
        {
            ViewModel.RenameFile(fileResource);
        }
    }

    private void OpenResource(object? sender, RoutedEventArgs e)
    {
        var element = sender as FrameworkElement;
        Guard.IsNotNull(element);
    }

    private void DoubleTappedResource(object? sender, DoubleTappedRoutedEventArgs e)
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
        if (e.Key == VirtualKey.Delete)
        {
            if (ResourcesTreeView.SelectedItem is FileResource fileResource)
            {
                ViewModel.DeleteFile(fileResource);
            }
            else if (ResourcesTreeView.SelectedItem is FolderResource folderResource)
            {
                ViewModel.DeleteFolder(folderResource);
            }
        }
    }

    private void ResourcesTreeView_DragItemsCompleted(TreeView sender, TreeViewDragItemsCompletedEventArgs args)
    {
        var draggedItems = args.Items.ToList();

        // A null newParent indicates that the dragged items are being moved to the root folder
        IFolderResource? newParent = null;
        if (args.NewParentItem is IFileResource fileResource)
        {
            newParent = fileResource.ParentFolder;
        }
        else if (args.NewParentItem is IFolderResource folderResource)
        {
            newParent = folderResource;
        }

        var resources = new List<IResource>();
        foreach (var item in draggedItems)
        {
            if (item is IResource resource)
            {
                resources.Add(resource);
            }
        }

        ViewModel.MoveResources(resources, newParent);
    }
}
