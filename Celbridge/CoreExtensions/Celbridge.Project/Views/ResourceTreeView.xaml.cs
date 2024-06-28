using Celbridge.Project.Models;
using Celbridge.Project.ViewModels;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Project.Views;

public sealed partial class ResourceTreeView : UserControl
{
    public ResourceTreeViewModel ViewModel { get; }

    public ResourceTreeView()
    {
        this.InitializeComponent();

        var serviceProvider = ServiceLocator.ServiceProvider;
        ViewModel = serviceProvider.GetRequiredService<ResourceTreeViewModel>();

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
            ViewModel.OnAddFolder(folderResource);
        }
        else if (menuFlyoutItem.DataContext is FileResource fileResource)
        {
            // Add a folder to the folder containing the selected file
            var parentFolder = fileResource.ParentFolder;
            Guard.IsNotNull(parentFolder);

            ViewModel.OnAddFolder(parentFolder);
        }
        else
        {
            // Add a folder at the root of the resource tree
            ViewModel.OnAddFolder(null);
        }
    }

    private void OpenResource(object? sender, RoutedEventArgs e)
    {
        var element = sender as FrameworkElement;
        Guard.IsNotNull(element);
    }

    private void AddResource(object? sender, RoutedEventArgs e)
    {
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);
    }

    private void DeleteResource(object? sender, RoutedEventArgs e)
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
}
