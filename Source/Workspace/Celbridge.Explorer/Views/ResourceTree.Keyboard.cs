using Celbridge.DataTransfer;
using Celbridge.Explorer.Models;
using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;
using Windows.System;

namespace Celbridge.Explorer.Views;

public sealed partial class ResourceTree : IEditTarget
{
    private void ListView_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Standard edit verbs route to the focus service's active target (this tree) so the keyboard,
        // the macOS Edit menu, and the in-window menu all share one path.
        var intent = ResolveEditIntent(e.Key);
        if (intent is not null)
        {
            if (CanPerformEdit(intent.Value))
            {
                PerformIntent(intent.Value);
                e.Handled = true;
            }

            return;
        }

        if (EditKeyboard.IsCommandModifierDown())
        {
            return;
        }

        // Tree navigation is Explorer-specific and stays local.
        var selectedItem = ViewModel.SelectedItem;

        e.Handled = e.Key switch
        {
            VirtualKey.Right => HandleExpand(selectedItem),
            VirtualKey.Left => HandleCollapse(selectedItem),
            VirtualKey.Enter => HandleOpen(selectedItem),
            VirtualKey.Escape => HandleClearSelection(),
            _ => false
        };
    }

    public bool CanPerformEdit(EditIntent intent)
    {
        var selectedItem = ViewModel.SelectedItem;

        return intent switch
        {
            EditIntent.Copy => ViewModel.GetSelectedResources().Count > 0,
            EditIntent.Cut => ViewModel.GetSelectedResources().Count > 0,
            EditIntent.Delete => ViewModel.GetSelectedResources().Count > 0,
            EditIntent.Paste => true,
            EditIntent.SelectAll => true,
            EditIntent.Duplicate => selectedItem is not null && !selectedItem.IsProjectFolder,
            EditIntent.Rename => selectedItem is not null && !selectedItem.IsProjectFolder,
            _ => false
        };
    }

    public void PerformEdit(EditIntent intent)
    {
        var selectedItem = ViewModel.SelectedItem;
        var selectedResources = ViewModel.GetSelectedResources();

        switch (intent)
        {
            case EditIntent.Copy:
                HandleCopy(selectedResources);
                break;

            case EditIntent.Cut:
                HandleCut(selectedResources);
                break;

            case EditIntent.Paste:
                HandlePaste(selectedItem);
                break;

            case EditIntent.SelectAll:
                HandleSelectAll(selectedItem);
                break;

            case EditIntent.Delete:
                HandleDelete(selectedResources);
                break;

            case EditIntent.Duplicate:
                HandleDuplicate(selectedItem);
                break;

            case EditIntent.Rename:
                HandleRename(selectedItem);
                break;
        }
    }

    public bool TryHandleTabKey(bool shift)
    {
        // The resource tree does not act on Tab, so normal focus navigation proceeds.
        return false;
    }

    private static EditIntent? ResolveEditIntent(VirtualKey key)
    {
        if (!EditKeyboard.IsCommandModifierDown())
        {
            if (EditKeyboard.IsDeleteKey(key))
            {
                return EditIntent.Delete;
            }

            if (key == VirtualKey.F2)
            {
                return EditIntent.Rename;
            }

            return null;
        }

        if (EditKeyboard.IsShiftDown())
        {
            return null;
        }

        return key switch
        {
            VirtualKey.A => EditIntent.SelectAll,
            VirtualKey.D => EditIntent.Duplicate,
            VirtualKey.C => EditIntent.Copy,
            VirtualKey.X => EditIntent.Cut,
            VirtualKey.V => EditIntent.Paste,
            _ => null
        };
    }

    private void PerformIntent(EditIntent intent)
    {
        _commandService.Execute<IPerformEditCommand>(command =>
        {
            command.Intent = intent;
        });
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
        if (selectedItem == null || selectedItem.IsProjectFolder)
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
        RestoreFocusToSelectedItem();
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
            RestoreFocusToSelectedItem();
            return true;
        }

        if (ViewModel.SelectParentFolder())
        {
            RestoreFocusToSelectedItem();
            return true;
        }

        return false;
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

    // Expand/collapse rebuilds the tree, replacing every ListView container and discarding keyboard focus,
    // which leaves subsequent arrow keys with no target. Re-focus the anchor item's new container once the
    // rebuild has regenerated it, so keyboard navigation continues.
    private void RestoreFocusToSelectedItem()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var item = ViewModel.SelectedItem;
            if (item is null)
            {
                return;
            }

            ResourceListView.ScrollIntoView(item);

            if (ResourceListView.ContainerFromItem(item) is ListViewItem container)
            {
                container.Focus(FocusState.Programmatic);
            }
        });
    }

    private bool HandleClearSelection()
    {
        ResourceListView.SelectedItems.Clear();
        return true;
    }

    private bool HandleSelectAll(ResourceViewItem? selectedItem)
    {
        var siblings = ViewModel.GetSiblingItems(selectedItem);

        ResourceListView.SelectedItems.Clear();
        foreach (var item in siblings)
        {
            ResourceListView.SelectedItems.Add(item);
        }

        return true;
    }

    private bool HandleDuplicate(ResourceViewItem? selectedItem)
    {
        if (selectedItem == null || selectedItem.IsProjectFolder)
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
        var destFolderResource = _resourceTransferService.GetContextMenuItemFolder(selectedItem?.Resource);
        _commandService.Execute<IPasteResourceFromClipboardCommand>(command =>
        {
            command.DestFolderResource = destFolderResource;
        });
        return true;
    }
}
