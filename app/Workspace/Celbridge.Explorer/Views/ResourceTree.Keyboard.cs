using Celbridge.DataTransfer;
using Celbridge.Explorer.Models;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.Explorer.Views;

public sealed partial class ResourceTree
{
    private void ListView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

        var selectedItem = ViewModel.SelectedItem;
        var selectedResources = ViewModel.GetSelectedResources();

        e.Handled = (ctrl, e.Key) switch
        {
            (false, VirtualKey.Delete) => HandleDelete(selectedResources),
            (false, VirtualKey.F2) => HandleRename(selectedItem),
            (false, VirtualKey.Right) => HandleExpand(selectedItem),
            (false, VirtualKey.Left) => HandleCollapse(selectedItem),
            (false, VirtualKey.Enter) => HandleOpen(selectedItem),
            (false, VirtualKey.Escape) => HandleClearSelection(),
            (true, VirtualKey.A) => HandleSelectAllSiblings(),
            (true, VirtualKey.D) => HandleDuplicate(selectedItem),
            (true, VirtualKey.C) => HandleCopy(selectedResources),
            (true, VirtualKey.X) => HandleCut(selectedResources),
            (true, VirtualKey.V) => HandlePaste(selectedItem),
            _ => false
        };
    }

    private bool HandleDelete(List<IResource> selectedResources)
    {
        if (selectedResources.Count == 0)
        {
            return false;
        }

        var resourceKeys = _resourceRegistry.GetResourceKeys(selectedResources);
        _commandService.Execute<IDeleteResourceDialogCommand>(command =>
        {
            command.Resources = resourceKeys;
        });
        return true;
    }

    private bool HandleRename(ResourceViewItem? selectedItem)
    {
        if (selectedItem == null || selectedItem.IsRootFolder)
        {
            return false;
        }

        var resourceKey = _resourceRegistry.GetResourceKey(selectedItem.Resource);
        _commandService.Execute<IRenameResourceDialogCommand>(command =>
        {
            command.Resource = resourceKey;
        });
        return true;
    }

    private bool HandleExpand(ResourceViewItem? selectedItem)
    {
        if (selectedItem == null || !selectedItem.IsFolder || selectedItem.IsExpanded)
        {
            return false;
        }

        ViewModel.ExpandItem(selectedItem);
        return true;
    }

    private bool HandleCollapse(ResourceViewItem? selectedItem)
    {
        if (selectedItem == null)
        {
            return false;
        }

        if (selectedItem.IsFolder && selectedItem.IsExpanded)
        {
            ViewModel.CollapseItem(selectedItem);
            return true;
        }

        return ViewModel.SelectParentFolder();
    }

    private bool HandleOpen(ResourceViewItem? selectedItem)
    {
        if (selectedItem == null)
        {
            return false;
        }

        OpenResource(selectedItem);
        return true;
    }

    private bool HandleClearSelection()
    {
        ResourceListView.SelectedItems.Clear();
        return true;
    }

    private bool HandleSelectAllSiblings()
    {
        ResourceListView.SelectedItems.Clear();

        var siblings = ViewModel.GetSiblingItems();
        foreach (var item in siblings)
        {
            ResourceListView.SelectedItems.Add(item);
        }

        return true;
    }

    private bool HandleDuplicate(ResourceViewItem? selectedItem)
    {
        if (selectedItem == null || selectedItem.IsRootFolder)
        {
            return false;
        }

        var resourceKey = _resourceRegistry.GetResourceKey(selectedItem.Resource);
        _commandService.Execute<IDuplicateResourceDialogCommand>(command =>
        {
            command.Resource = resourceKey;
        });
        return true;
    }

    private bool HandleCopy(List<IResource> selectedResources)
    {
        if (selectedResources.Count == 0)
        {
            return false;
        }

        var resourceKeys = _resourceRegistry.GetResourceKeys(selectedResources);
        _commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResources = resourceKeys;
            command.TransferMode = DataTransferMode.Copy;
        });
        return true;
    }

    private bool HandleCut(List<IResource> selectedResources)
    {
        if (selectedResources.Count == 0)
        {
            return false;
        }

        var resourceKeys = _resourceRegistry.GetResourceKeys(selectedResources);
        _commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResources = resourceKeys;
            command.TransferMode = DataTransferMode.Move;
        });
        return true;
    }

    private bool HandlePaste(ResourceViewItem? selectedItem)
    {
        var destFolderResource = _resourceRegistry.GetContextMenuItemFolder(selectedItem?.Resource);
        _commandService.Execute<IPasteResourceFromClipboardCommand>(command =>
        {
            command.DestFolderResource = destFolderResource;
        });
        return true;
    }
}
