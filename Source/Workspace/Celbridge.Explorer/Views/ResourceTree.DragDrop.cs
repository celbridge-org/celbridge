using Celbridge.DataTransfer;
using Celbridge.Explorer.Models;
using Celbridge.UserInterface.Helpers;
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
    // so a stale value here never affects them. The DataPackage property and the shared
    // ResourceDragState are also populated so cross-control consumers (e.g. DocumentSection) can
    // recognise the drag on Windows and on the Skia head respectively.
    private List<IResource>? _internalDragResources;

    private void ListView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        // Store the dragged items for later use, excluding the project folder
        var draggedResources = new List<IResource>();
        foreach (var item in e.Items)
        {
            if (item is ResourceViewItem treeItem && !treeItem.IsProjectFolder)
            {
                draggedResources.Add(treeItem.Resource);
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
        ResourceDragState.Begin(draggedResources);
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
        // Check for external drag (from File Explorer, etc.) first, identified by the StorageItems
        // format. Checking it before the internal-drag field means a stale field can never shadow a
        // real external drag.
        if (e.DataView?.Contains(StandardDataFormats.StorageItems) == true)
        {
            // External drag - allow drop on folder, file (uses parent), or empty space (project folder).
            // The dropped item names are not available synchronously here, so the
            // folder-level policy check covers read-only roots, hidden folders, and
            // fully-locked folders; the per-item visibility gate enforces the rest
            // when the transfer runs.
            var destFolder = ResolveDropTargetFolder(targetItem?.Resource);
            var destFolderKey = _resourceRegistry.GetResourceKey(destFolder);
            var canDrop = _operationService.CanAddToFolder(destFolderKey).IsSuccess;
            return (canDrop, IsInternalDrag: false);
        }

        // Check for internal drag (from our ListView), tracked via the field.
        if (_internalDragResources is not null)
        {
            // Internal drag - check if target is valid (folder or file)
            var targetIsValid = targetItem?.Resource is IFolderResource or IFileResource;
            if (!targetIsValid)
            {
                return (CanDrop: false, IsInternalDrag: true);
            }

            var destFolder = ResolveDropTargetFolder(targetItem!.Resource);
            var canDrop = CanDropInto(destFolder, _internalDragResources);
            return (canDrop, IsInternalDrag: true);
        }

        return (CanDrop: false, IsInternalDrag: false);
    }

    // Predicts whether the dragged resources can land in the destination folder:
    // the folder must accept additions (writable root, visible, not fully locked)
    // and every dragged item's destination key must itself be a permitted resource
    // (not hidden by the ignore-file, not locked). Mirrors the gate the move/copy
    // executor enforces so the no-drop cursor never disagrees with the outcome.
    private bool CanDropInto(IFolderResource destFolder, List<IResource>? draggedResources)
    {
        var destFolderKey = _resourceRegistry.GetResourceKey(destFolder);
        if (_operationService.CanAddToFolder(destFolderKey).IsFailure)
        {
            return false;
        }

        if (draggedResources is null)
        {
            return true;
        }

        foreach (var resource in draggedResources)
        {
            var sourceKey = _resourceRegistry.GetResourceKey(resource);

            // A no-op move back into the resource's current folder is always fine.
            if (sourceKey.GetParent() == destFolderKey)
            {
                continue;
            }

            var destKey = destFolderKey.Combine(resource.Name);
            bool isFolder = resource is IFolderResource;
            if (_operationService.CanCreateResource(destKey, isFolder).IsFailure)
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
            ResourceDragState.End();
            MoveResourcesToFolder(draggedResources, destFolder);
        }
    }

    private void MoveResourcesToFolder(List<IResource> resources, IFolderResource destFolder)
    {
        var destResource = _resourceRegistry.GetResourceKey(destFolder);
        var sourceResources = new List<ResourceKey>();

        foreach (var resource in resources)
        {
            var sourceResource = _resourceRegistry.GetResourceKey(resource);
            var resolvedDestResource = _resourceTransferService.ResolveDestinationResource(sourceResource, destResource);

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

        // A lone .cel file drop can only be an intentional creation in the
        // reserved namespace; refuse it. Multi-item and folder drops pass
        // through so the "copy resources from another Celbridge project"
        // workflow still works — any orphan .cel files that arrive surface
        // through the project-check reporter.
        if (sourcePaths.Count == 1)
        {
            var droppedFileName = Path.GetFileName(sourcePaths[0]);
            if (_sidecarService.IsSidecarFileName(droppedFileName))
            {
                return;
            }
        }

        var destFolderResource = _resourceRegistry.GetResourceKey(destFolder);
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
