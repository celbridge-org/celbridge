using Celbridge.DataTransfer;
using Celbridge.Explorer.Models;
using Celbridge.UserInterface.DragDrop;
using Celbridge.UserInterface.Helpers;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.Explorer.Views;

/// <summary>
/// Pointer-driven resource drag support for the resource tree, used on heads where the built-in
/// ListView drag-and-drop is disabled. The tree is both a drag source (a press on an item starts a
/// managed drag through the shared coordinator) and a drop target (a folder the drag can move or copy
/// resources into). External file drops from the operating system still arrive through the built-in
/// drag-and-drop. Kept in its own partial so the desktop-only drag surface stays discoverable.
/// </summary>
public sealed partial class ResourceTree : IResourceDropTarget
{
    private IResourceDragCoordinator? _resourceDragCoordinator;
    private readonly PointerEventHandler _listPointerPressedHandler;
    private FrameworkElement? _dropHighlightElement;

    /// <summary>
    /// Disables the built-in ListView drag source on heads that use the pointer-driven coordinator, and
    /// wires the pointer press that starts a managed drag. No-op on heads that keep the built-in
    /// drag-and-drop. External file drops keep working because AllowDrop and the Drop handler stay.
    /// </summary>
    private void ConfigurePointerDrag()
    {
        if (!_platformInfo.UsesPointerDrivenTabDrag)
        {
            return;
        }

        _resourceDragCoordinator = ServiceLocator.AcquireService<IResourceDragCoordinator>();
        ResourceListView.CanDragItems = false;
        ResourceListView.AddHandler(PointerPressedEvent, _listPointerPressedHandler, handledEventsToo: true);
    }

    private void RegisterAsDropTarget()
    {
        _resourceDragCoordinator?.RegisterDropTarget(this);
    }

    private void UnregisterAsDropTarget()
    {
        _resourceDragCoordinator?.UnregisterDropTarget(this);
    }

    private void ResourceListView_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_resourceDragCoordinator is null)
        {
            return;
        }

        var pointerPoint = e.GetCurrentPoint(ResourceListView);
        if (!pointerPoint.Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Presses on interactive children (the expand/collapse chevron button) never start a drag.
        if (IsPressOnInteractiveChild(e.OriginalSource))
        {
            return;
        }

        var pressedItem = FindItemAtPosition(pointerPoint.Position);
        if (pressedItem is null ||
            pressedItem.IsProjectFolder)
        {
            return;
        }

        var resources = GatherDragResources(pressedItem);
        if (resources.Count == 0)
        {
            return;
        }

        _resourceDragCoordinator.OnResourcePressed(resources, e);
    }

    // The dragged set is the current selection when the pressed item is part of it, otherwise just the
    // pressed item. The project folder is never dragged.
    private List<IResource> GatherDragResources(ResourceViewItem pressedItem)
    {
        var selectedItems = ResourceListView.SelectedItems.OfType<ResourceViewItem>().ToList();
        var draggedItems = selectedItems.Contains(pressedItem)
            ? selectedItems
            : new List<ResourceViewItem> { pressedItem };

        var resources = new List<IResource>();
        foreach (var item in draggedItems)
        {
            if (!item.IsProjectFolder)
            {
                resources.Add(item.Resource);
            }
        }

        return resources;
    }

    public string? UpdateDragOver(Point windowPoint, IReadOnlyList<IResource> resources)
    {
        if (!TryResolveTreeDrop(windowPoint, out var destFolder, out var targetItem))
        {
            ClearDragFeedback();
            return null;
        }

        if (!CanDropInto(destFolder, resources.ToList()))
        {
            ClearDragFeedback();
            return null;
        }

        // Highlight the specific folder row under the pointer; empty space below the items (which drops
        // into the project root) has no row to highlight.
        if (targetItem is not null)
        {
            HighlightDropTarget(targetItem);
        }
        else
        {
            ClearDragFeedback();
        }

        var captionKey = IsCopyRequested() ? "ResourceTree_Copy" : "ResourceTree_Move";
        return _stringLocalizer.GetString(captionKey);
    }

    public void ClearDragFeedback()
    {
        if (_dropHighlightElement is not null)
        {
            _dropHighlightElement.Background = null;
            _dropHighlightElement = null;
        }
    }

    public bool TryDrop(Point windowPoint, IReadOnlyList<IResource> resources)
    {
        ClearDragFeedback();

        if (!TryResolveTreeDrop(windowPoint, out var destFolder, out _))
        {
            return false;
        }

        var draggedResources = resources.ToList();
        if (!CanDropInto(destFolder, draggedResources))
        {
            return false;
        }

        var transferMode = IsCopyRequested() ? DataTransferMode.Copy : DataTransferMode.Move;
        TransferResourcesToFolder(draggedResources, destFolder, transferMode);

        return true;
    }

    // Resolves a drop over the tree at the given window point. Returns false when the point is outside
    // the tree. When inside, destFolder is the folder the drop would land in - the folder or file's
    // parent under the pointer, or the project root for empty space below the items - and targetItem is
    // the specific item under the pointer, or null for that empty space.
    private bool TryResolveTreeDrop(Point windowPoint, out IFolderResource destFolder, out ResourceViewItem? targetItem)
    {
        destFolder = ViewModel.ProjectFolder;
        targetItem = null;

        if (XamlRoot?.Content is not UIElement windowContent)
        {
            return false;
        }

        var listPoint = windowContent.TransformToVisual(ResourceListView).TransformPoint(windowPoint);
        if (listPoint.X < 0 ||
            listPoint.Y < 0 ||
            listPoint.X >= ResourceListView.ActualWidth ||
            listPoint.Y >= ResourceListView.ActualHeight)
        {
            return false;
        }

        targetItem = FindItemAtPosition(listPoint);
        destFolder = ResolveDropTargetFolder(targetItem?.Resource);

        return true;
    }

    private void HighlightDropTarget(ResourceViewItem targetItem)
    {
        var container = ResourceListView.ContainerFromItem(targetItem) as ListViewItem;
        var contentGrid = container is null ? null : VisualTree.FindDescendantByName(container, "ContentGrid") as FrameworkElement;
        if (contentGrid is null)
        {
            ClearDragFeedback();
            return;
        }

        if (ReferenceEquals(_dropHighlightElement, contentGrid))
        {
            return;
        }

        ClearDragFeedback();
        contentGrid.Background = CreateDropHighlightBrush();
        _dropHighlightElement = contentGrid;
    }

    private void TransferResourcesToFolder(List<IResource> resources, IFolderResource destFolder, DataTransferMode transferMode)
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
                command.TransferMode = transferMode;
            });
        }
    }

    private static bool IsPressOnInteractiveChild(object? originalSource)
    {
        var current = originalSource as DependencyObject;
        while (current is not null)
        {
            if (current is ButtonBase)
            {
                return true;
            }
            if (current is ListView)
            {
                break;
            }
            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static bool IsCopyRequested()
    {
        return InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
    }

    private static Brush CreateDropHighlightBrush()
    {
        var color = GetAccentColor();
        color.A = 0x40;

        return new SolidColorBrush(color);
    }

    private static Windows.UI.Color GetAccentColor()
    {
        if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var resource) &&
            resource is SolidColorBrush brush)
        {
            return brush.Color;
        }

        return Microsoft.UI.Colors.DodgerBlue;
    }
}
