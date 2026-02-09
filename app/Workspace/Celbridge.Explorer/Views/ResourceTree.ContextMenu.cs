using Celbridge.DataTransfer;
using Celbridge.Explorer.Menu;
using Celbridge.Workspace;
using Windows.Foundation;

namespace Celbridge.Explorer.Views;

public sealed partial class ResourceTree
{
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
        var hasClipboardData = await CheckClipboardHasResources(contentDescription, clickedResource);

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

    private async Task<bool> CheckClipboardHasResources(
        ClipboardContentDescription contentDescription,
        IResource? clickedResource)
    {
        if (contentDescription.ContentType != ClipboardContentType.Resource)
        {
            return false;
        }

        var destFolder = ResolveDropTargetFolder(clickedResource);
        var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var destFolderKey = resourceRegistry.GetResourceKey(destFolder);

        var getResult = await _dataTransferService.GetClipboardResourceTransfer(destFolderKey);
        if (getResult.IsSuccess)
        {
            var content = getResult.Value;
            return content.TransferItems.Count > 0;
        }

        return false;
    }
}
