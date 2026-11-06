using Celbridge.DataTransfer;
using Celbridge.Explorer.Models;
using Celbridge.Resources;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;
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

    // The tree copies and pastes resources through the application's own file clipboard.
    public bool HostMediatedClipboard => true;

    public bool HasPlatformEditing => false;

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
            EditIntent.Undo => _operationService.CanUndo,
            EditIntent.Redo => _operationService.CanRedo,
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

            case EditIntent.Undo:
                _ = UndoAsync();
                break;

            case EditIntent.Redo:
                _ = RedoAsync();
                break;
        }
    }

    // The command queue waits while a dialog is open, so the alert opens after the command finishes.
    // A refused undo leaves its entry on the stack, so the alert shows. An empty stack shows no alert, because an
    // earlier queued press has already done the undo.
    private async Task UndoAsync()
    {
        var undoResult = await _commandService.ExecuteAsync<IUndoResourceCommand>();
        if (undoResult.IsFailure
            && _operationService.CanUndo)
        {
            var title = _stringLocalizer.GetString("ResourceTree_CannotUndo");
            var message = _stringLocalizer.GetString("ResourceTree_CannotUndoMessage");
            await _dialogService.ShowAlertDialogAsync(title, message);
        }
    }

    // The command queue waits while a dialog is open, so the alert opens after the command finishes.
    // A failed redo leaves its entry on the stack, so the alert shows. An empty stack shows no alert, because an
    // earlier queued press has already done the redo.
    private async Task RedoAsync()
    {
        var redoResult = await _commandService.ExecuteAsync<IRedoResourceCommand>();
        if (redoResult.IsFailure
            && _operationService.CanRedo)
        {
            var title = _stringLocalizer.GetString("ResourceTree_CannotRedo");
            var message = _stringLocalizer.GetString("ResourceTree_CannotRedoMessage");
            await _dialogService.ShowAlertDialogAsync(title, message);
        }
    }

    public bool TryHandleTabKey(bool shift)
    {
        // The resource tree does not act on Tab, so normal focus navigation proceeds.
        return false;
    }

    private EditIntent? ResolveEditIntent(VirtualKey key)
    {
        // Delete is modifier-agnostic so the macOS native Cmd+Backspace shortcut deletes the selection
        // just as plain Backspace and Delete do.
        if (EditKeyboard.IsDeleteKey(key))
        {
            return EditIntent.Delete;
        }

        if (!EditKeyboard.IsCommandModifierDown())
        {
            if (key == VirtualKey.F2)
            {
                return EditIntent.Rename;
            }

            return null;
        }

        var shift = EditKeyboard.IsShiftDown();

        return ExplorerEditShortcuts.ResolveIntent(key, shift, _platformInfo.TreatsCtrlYAsRedo);
    }

    private void PerformIntent(EditIntent intent)
    {
        _commandService.Execute<IPerformEditCommand>(command =>
        {
            command.Intent = intent;
        });
    }

    private bool HandleDelete(List<ResourceItem> selectedResources)
    {
        if (selectedResources.Count == 0)
        {
            return false;
        }

        var resourceKeys = GetResourceKeys(selectedResources);
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

        var resourceKey = selectedItem.Resource;
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

        _ = ExpandAndRestoreFocusAsync(selectedItem);
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
            _ = CollapseAndRestoreFocusAsync(selectedItem);
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

    private async Task ExpandAndRestoreFocusAsync(ResourceViewItem item)
    {
        await ViewModel.ExpandItemAsync(item);
        RestoreFocusToSelectedItem();
    }

    private async Task CollapseAndRestoreFocusAsync(ResourceViewItem item)
    {
        await ViewModel.CollapseItemAsync(item);
        RestoreFocusToSelectedItem();
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
                FocusIntent.RestoreFocus(container);
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

        var resourceKey = selectedItem.Resource;
        _commandService.Execute<IDuplicateResourceDialogCommand>(command =>
        {
            command.Resource = resourceKey;
        });
        return true;
    }

    private bool HandleCopy(List<ResourceItem> selectedResources)
    {
        if (selectedResources.Count == 0)
        {
            return false;
        }

        var resourceKeys = GetResourceKeys(selectedResources);
        _commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResources = resourceKeys;
            command.TransferMode = DataTransferMode.Copy;
        });
        return true;
    }

    private bool HandleCut(List<ResourceItem> selectedResources)
    {
        if (selectedResources.Count == 0)
        {
            return false;
        }

        var resourceKeys = GetResourceKeys(selectedResources);
        _commandService.Execute<ICopyResourceToClipboardCommand>(command =>
        {
            command.SourceResources = resourceKeys;
            command.TransferMode = DataTransferMode.Move;
        });
        return true;
    }

    private bool HandlePaste(ResourceViewItem? selectedItem)
    {
        var destFolderResource = ResolveDropTargetFolder(selectedItem);
        _commandService.Execute<IPasteResourceFromClipboardCommand>(command =>
        {
            command.DestFolderResource = destFolderResource;
        });
        return true;
    }

    private static List<ResourceKey> GetResourceKeys(List<ResourceItem> resources)
    {
        return resources
            .Select(resourceItem => resourceItem.Resource)
            .ToList();
    }
}
