using Celbridge.DataTransfer;
using Celbridge.Explorer.Models;
using Microsoft.Extensions.Localization;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.Explorer.Views;

public sealed partial class ResourceTree
{
    private ResourceViewItem? _dragOverItem;

    // Resources being dragged within this ListView. Tracked in a field rather than via the DataPackage's
    // custom properties because those managed properties do not round-trip to the DragOver/Drop events on
    // the Uno Skia head (only OS-serialisable formats survive), so the internal-drag path was never
    // recognised there. Set when an internal drag starts, read during drag-over and drop, and cleared
    // after an internal drop. External drags are identified by the StorageItems format and checked first,
    // so a stale value here never affects them. The DataPackage property is also populated so a
    // cross-control consumer (DocumentSectionView) can recognise the drag on Windows, where it round-trips.
    private List<ResourceItem>? _internalDragResources;

    // Forces the post-rebuild focus restore after a built-in-drag move, in case the OS drag loop dropped
    // tree focus before the pre-rebuild focus snapshot ran (which would otherwise miss it). The pointer-driven
    // drag (macOS) keeps focus on the tree, so its move path does not set this.
    private bool _restoreTreeFocusAfterMove;

    private void ListView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        // Store the dragged items for later use, excluding the project folder
        var draggedResources = new List<ResourceItem>();
        foreach (var item in e.Items)
        {
            if (item is ResourceViewItem treeItem && !treeItem.IsProjectFolder)
            {
                draggedResources.Add(treeItem.Item);
            }
        }

        // Cancel drag if no valid items (e.g., only the project folder was selected)
        if (draggedResources.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        _internalDragResources = draggedResources;
        e.Data.Properties["DraggedResources"] = draggedResources;
        e.Data.RequestedOperation = DataPackageOperation.Move;

        // Set text for drag visual - show count of items being dragged
        var count = draggedResources.Count;
        var text = count == 1
            ? draggedResources[0].Resource.ResourceName
            : _stringLocalizer.GetString("ResourceTree_DragItemCount", count);

        e.Data.Properties.Add(StandardDataFormats.Text, text);
    }

    private void ListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        // Clear the internal-drag tracking once the drag ends, including when it is cancelled (Escape, or
        // release over a surface where Drop never fires). A successful internal drop already cleared it in
        // ListView_Drop.
        _internalDragResources = null;
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
        // Check for external drag (from File Explorer, etc.) first, identified by the StorageItems
        // format. Checking it before the internal-drag field means a stale field can never shadow a
        // real external drag.
        if (e.DataView?.Contains(StandardDataFormats.StorageItems) == true)
        {
            // External drag - allow drop on folder, file (uses parent), or empty space (project folder).
            // The dropped item names are not available synchronously here, so the
            // folder-level policy check covers read-only roots, hidden folders, and
            // fully-locked folders. The per-item visibility gate enforces the rest
            // when the transfer runs.
            var destFolderKey = ResolveDropTargetFolder(targetItem);
            var canDrop = _operationService.CanAddToFolder(destFolderKey).IsSuccess;
            return (canDrop, IsInternalDrag: false);
        }

        // Check for internal drag (from our ListView), tracked via the field.
        if (_internalDragResources is not null)
        {
            // Internal drag - the target must be a row
            if (targetItem is null)
            {
                return (CanDrop: false, IsInternalDrag: true);
            }

            var destFolder = ResolveDropTargetFolder(targetItem);
            var canDrop = CanDropInto(destFolder, _internalDragResources);
            return (canDrop, IsInternalDrag: true);
        }

        return (CanDrop: false, IsInternalDrag: false);
    }

    // Predicts whether the dragged resources can land in the destination folder:
    // the folder must accept additions, and every dragged item's destination key
    // must itself be a permitted resource. Mirrors the gate the move/copy executor
    // enforces so the no-drop cursor never disagrees with the outcome.
    private bool CanDropInto(ResourceKey destFolderKey, List<ResourceItem>? draggedResources)
    {
        if (_operationService.CanAddToFolder(destFolderKey).IsFailure)
        {
            return false;
        }

        if (draggedResources is null)
        {
            return true;
        }

        foreach (var resourceItem in draggedResources)
        {
            var sourceKey = resourceItem.Resource;

            // A no-op move back into the resource's current folder is always fine.
            if (sourceKey.GetParent() == destFolderKey)
            {
                continue;
            }

            var destKey = destFolderKey.Combine(sourceKey.ResourceName);
            if (_operationService.CanCreateResource(destKey, resourceItem.IsFolder).IsFailure)
            {
                return false;
            }
        }

        return true;
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
                var captionKey = e.AcceptedOperation == DataPackageOperation.Copy ? "ResourceTree_Copy" : "ResourceTree_Move";
                e.DragUIOverride.Caption = _stringLocalizer.GetString(captionKey);
            }
            else
            {
                // External drag - always copy
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = _stringLocalizer.GetString("ResourceTree_Copy");
            }

            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsContentVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.DragUIOverride.Caption = _stringLocalizer.GetString("ResourceTree_CannotDropHere");
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
        var destFolder = ResolveDropTargetFolder(dropTargetItem);

        // Handle external drop (from file explorer) first, identified by the StorageItems format.
        if (e.DataView?.Contains(StandardDataFormats.StorageItems) == true)
        {
            _ = ProcessExternalDrop(e.DataView, destFolder);
            return;
        }

        // Handle internal drop (from our ListView), tracked via the field.
        if (_internalDragResources is not null)
        {
            var draggedResources = _internalDragResources;
            _internalDragResources = null;
            MoveResourcesToFolder(draggedResources, destFolder);
        }
    }

    private void MoveResourcesToFolder(List<ResourceItem> resources, ResourceKey destFolder)
    {
        var sourceResources = GetResourcesToTransfer(resources, destFolder);

        if (sourceResources.Count > 0)
        {
            _commandService.Execute<ICopyResourceCommand>(command =>
            {
                command.SourceResources = sourceResources;
                command.DestResource = destFolder;
                command.TransferMode = DataTransferMode.Move;
            });

            // The move triggers a tree rebuild that destroys the dragged row; restore focus to the tree once
            // the rebuild completes so the Explorer panel keeps focus and the utility rail button stays lit.
            _restoreTreeFocusAfterMove = true;
        }
    }

    // Leaves out each resource already in the destination folder, and a folder dropped onto itself.
    private static List<ResourceKey> GetResourcesToTransfer(List<ResourceItem> resources, ResourceKey destFolder)
    {
        var sourceResources = new List<ResourceKey>();

        foreach (var resourceItem in resources)
        {
            var sourceResource = resourceItem.Resource;
            if (sourceResource == destFolder
                || sourceResource.GetParent() == destFolder)
            {
                continue;
            }

            sourceResources.Add(sourceResource);
        }

        return sourceResources;
    }

    private async Task ProcessExternalDrop(DataPackageView dataView, ResourceKey destFolderResource)
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

        // A lone .cel file drop can only be an intentional creation in the
        // reserved namespace. Refuse it. Multi-item and folder drops pass
        // through so the "copy resources from another Celbridge project"
        // workflow still works — any orphan .cel files that arrive appear in
        // the project load report.
        if (sourcePaths.Count == 1)
        {
            var droppedFileName = Path.GetFileName(sourcePaths[0]);
            if (_sidecarService.IsSidecarFileName(droppedFileName))
            {
                return;
            }
        }

        var createResult = await _resourceTransferService.CreateResourceTransferAsync(
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
