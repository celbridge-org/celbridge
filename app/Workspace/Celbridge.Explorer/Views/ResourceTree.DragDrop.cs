using Celbridge.DataTransfer;
using Celbridge.Explorer.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.Explorer.Views;

public sealed partial class ResourceTree
{
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
        var dropInfo = EvaluateDropTarget(e, targetItem);
        ApplyDragVisuals(e, dropInfo);

        _dragOverItem = dropInfo.CanDrop ? targetItem : null;
        e.Handled = true;
    }

    private (bool CanDrop, bool IsInternalDrag) EvaluateDropTarget(DragEventArgs e, ResourceViewItem? targetItem)
    {
        // Check for internal drag (from our ListView)
        if (e.Data?.Properties?.ContainsKey("DraggedResources") == true)
        {
            // Internal drag - check if target is valid (folder or file)
            var canDrop = targetItem?.Resource is IFolderResource or IFileResource;
            return (canDrop, IsInternalDrag: true);
        }

        // Check for external drag (from File Explorer, etc.)
        if (e.DataView?.Contains(StandardDataFormats.StorageItems) == true)
        {
            // External drag - allow drop on folder, file (uses parent), or empty space (root folder)
            return (CanDrop: true, IsInternalDrag: false);
        }

        return (CanDrop: false, IsInternalDrag: false);
    }

    private void ApplyDragVisuals(DragEventArgs e, (bool CanDrop, bool IsInternalDrag) dropInfo)
    {
        if (dropInfo.CanDrop)
        {
            if (dropInfo.IsInternalDrag)
            {
                // For internal drags, check if Ctrl is pressed for copy operation
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
}
