using Celbridge.BaseLibrary.Resources;
using Celbridge.Project.ViewModels;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

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

        Loaded += ResourceTreeView_Loaded;
        Unloaded += ResourceTreeView_Unloaded;
    }

    private void ResourceTreeView_Loaded(object sender, RoutedEventArgs e)
    {
        ResourcesTreeView.Collapsed += ResourcesTreeView_Collapsed;
        ResourcesTreeView.Expanding += ResourcesTreeView_Expanding;
        ViewModel.OnLoaded();
    }

    private void ResourceTreeView_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnUnloaded();
    }

    private void AddFolder(object? sender, RoutedEventArgs e)
    {
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);

        if (menuFlyoutItem.DataContext is IFolderResource destFolder)
        {
            // Add a folder to the selected folder
            ViewModel.ShowAddResourceDialog(ResourceType.Folder, destFolder);
        }
        else if (menuFlyoutItem.DataContext is IFileResource destFile)
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
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);

        if (menuFlyoutItem.DataContext is IFolderResource destFolder)
        {
            // Add a file to the selected folder
            ViewModel.ShowAddResourceDialog(ResourceType.File, destFolder);
        }
        else if (menuFlyoutItem.DataContext is IFileResource destFile)
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
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);

        var resource = menuFlyoutItem.DataContext as IResource;
        Guard.IsNotNull(resource);

        ViewModel.CutResourceToClipboard(resource);
    }

    private void CopyResource(object sender, RoutedEventArgs e)
    {
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);

        var resource = menuFlyoutItem.DataContext as IResource;
        Guard.IsNotNull(resource);

        ViewModel.CopyResourceToClipboard(resource);
    }

    private void PasteResource(object sender, RoutedEventArgs e)
    {
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);

        var destResource = menuFlyoutItem.DataContext as IResource;

        // Resource is permitted to be null here (indicates the root folder)
        ViewModel.PasteResourceFromClipboard(destResource);
    }

    private void DeleteResource(object? sender, RoutedEventArgs e)
    {
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);

        var resource = menuFlyoutItem.DataContext as IResource;
        Guard.IsNotNull(resource);

        ViewModel.ShowDeleteResourceDialog(resource);
    }

    private void RenameResource(object? sender, RoutedEventArgs e)
    {
        var menuFlyoutItem = sender as MenuFlyoutItem;
        Guard.IsNotNull(menuFlyoutItem);

        var resource = menuFlyoutItem.DataContext as IResource;
        Guard.IsNotNull(resource);

        ViewModel.ShowRenameResourceDialog(resource);
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
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

        if (e.Key == VirtualKey.Delete)
        {
            if (ResourcesTreeView.SelectedItem is IResource resource)
            {
                ViewModel.ShowDeleteResourceDialog(resource);
            }
        }
        else if (control)
        {
            var selectedResource = ResourcesTreeView.SelectedItem as IResource;
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
            
            if (e.Key == VirtualKey.V)
            {
                // Resource is permitted to be null here (indicates the root folder)
                ViewModel.PasteResourceFromClipboard(selectedResource);
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

        ViewModel.MoveResourcesToFolder(resources, newParent);
    }
}
