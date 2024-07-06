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
    private LocalizedString AddFolderText => _stringLocalizer.GetString("ResourceTree_AddFolder");
    private LocalizedString AddFileText => _stringLocalizer.GetString("ResourceTree_AddFile");
    private LocalizedString DeleteText => _stringLocalizer.GetString("ResourceTree_Delete");

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

        if (menuFlyoutItem.DataContext is FolderResource folderResource)
        {
            // Add a folder to the selected folder
            ViewModel.AddFolder(folderResource);
        }
        else if (menuFlyoutItem.DataContext is FileResource fileResource)
        {
            // Add a folder to the folder containing the selected file
            var parentFolder = fileResource.ParentFolder;
            Guard.IsNotNull(parentFolder);

            ViewModel.AddFolder(parentFolder);
        }
        else
        {
            // Add a folder at the root of the resource tree
            ViewModel.AddFolder(null);
        }
    }

    private void AddFile(object? sender, RoutedEventArgs e)
    {
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);

        if (menuFlyoutItem.DataContext is FolderResource folderResource)
        {
            // Add a file to the selected folder
            ViewModel.AddFile(folderResource);
        }
        else if (menuFlyoutItem.DataContext is FileResource fileResource)
        {
            // Add a file to the folder containing the selected file
            var parentFolder = fileResource.ParentFolder;
            Guard.IsNotNull(parentFolder);

            ViewModel.AddFile(parentFolder);
        }
        else
        {
            // Add a folder at the root of the resource tree
            ViewModel.AddFile(null);
        }
    }

    private void DeleteFolder(object? sender, RoutedEventArgs e)
    {
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);

        if (menuFlyoutItem.DataContext is FolderResource folderResource)
        {
            // Delete the selected folder
            ViewModel.DeleteFolder(folderResource);
        }
    }

    private void DeleteFile(object? sender, RoutedEventArgs e)
    {
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);

        if (menuFlyoutItem.DataContext is FileResource fileResource)
        {
            // Delete the selected file
            ViewModel.DeleteFile(fileResource);
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
        ViewModel.OnExpandedFoldersChanged();
    }

    private void ResourcesTreeView_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        ViewModel.OnExpandedFoldersChanged();
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
}
