using Celbridge.DataTransfer;
using Celbridge.Explorer.Menu;
using Windows.Foundation;

namespace Celbridge.Explorer.Views;

public sealed partial class ResourceTree
{
    private async void ListView_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        // If right-clicking on an already-selected item, preserve the multi-selection
        // If right-clicking on an unselected item, select only that item
        // If right-clicking on the project folder or empty space, track the project folder as the clicked resource
        var position = e.GetPosition(ResourceListView);
        var clickedItem = FindItemAtPosition(position);

        ResourceItem clickedResource;
        if (clickedItem != null)
        {
            clickedResource = clickedItem.Item;

            if (clickedItem.IsProjectFolder)
            {
                // Project folder is not selectable, clear selection
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
            // Right-clicking empty space - target the project folder
            clickedResource = new ResourceItem(ResourceKey.Empty, IsFolder: true);
            ResourceListView.SelectedItems.Clear();
        }

        // Build and show the context menu
        await ShowContextMenuAsync(position, clickedResource);
    }

    private async Task ShowContextMenuAsync(Point position, ResourceItem clickedResource)
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

    private async Task<ExplorerMenuContext> BuildMenuContext(ResourceItem clickedResource)
    {
        var selectedResources = ViewModel.GetSelectedResources();
        var isProjectFolderTargeted = clickedResource.Resource.IsEmpty;

        // Check clipboard state
        var contentDescription = _dataTransferService.GetClipboardContentDescription();
        var hasClipboardData = await CheckClipboardHasResources(contentDescription, clickedResource);

        // Precompute the policy permissibility so the synchronous menu-option
        // GetState calls read a settled answer rather than re-querying. This is
        // the single source of truth the executor also uses, so the menu state
        // cannot drift from enforcement.
        var canModifySelection = await EvaluateSelectionModifiable(selectedResources);
        var canAddToTargetFolder = EvaluateTargetFolderAddable(clickedResource);

        var context = new ExplorerMenuContext(
            ClickedResource: clickedResource,
            SelectedResources: selectedResources,
            IsProjectFolderTargeted: isProjectFolderTargeted,
            HasClipboardData: hasClipboardData,
            ClipboardContentType: contentDescription.ContentType,
            ClipboardOperation: contentDescription.ContentOperation
        )
        {
            CanModifySelection = canModifySelection,
            CanAddToTargetFolder = canAddToTargetFolder,
        };

        return context;
    }

    // Returns whether every selected resource can be deleted, renamed, moved, or
    // cut. The pre-checked confirmation dialog surfaces the specific reason when
    // the user attempts a blocked destructive action.
    private async Task<bool> EvaluateSelectionModifiable(IReadOnlyList<ResourceItem> selectedResources)
    {
        foreach (var resourceItem in selectedResources)
        {
            if (resourceItem.Resource.IsEmpty)
            {
                continue;
            }

            var canModifyResult = await _operationService.CanModifyResourceAsync(resourceItem.Resource);
            if (canModifyResult.IsFailure)
            {
                return false;
            }
        }

        return true;
    }

    // Returns whether new resources can be added into the folder the menu would target.
    private bool EvaluateTargetFolderAddable(ResourceItem clickedResource)
    {
        var targetFolderKey = ExplorerMenuContext.GetTargetFolder(clickedResource);

        return _operationService.CanAddToFolder(targetFolderKey).IsSuccess;
    }

    private async Task<bool> CheckClipboardHasResources(
        ClipboardContentDescription contentDescription,
        ResourceItem clickedResource)
    {
        if (contentDescription.ContentType != ClipboardContentType.Resource)
        {
            return false;
        }

        var destFolderKey = ExplorerMenuContext.GetTargetFolder(clickedResource);

        var getResult = await _dataTransferService.GetClipboardResourceTransfer(destFolderKey);
        if (getResult.IsSuccess)
        {
            var content = getResult.Value;
            return content.TransferItems.Count > 0;
        }

        return false;
    }
}
