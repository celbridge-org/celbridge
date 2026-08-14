using Celbridge.Commands;
using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Views;

/// <summary>
/// Document tab context-menu handling for WorkspacePanel: the close family, moving a tab between the
/// sections of its area, the clipboard and reveal actions, and reopening with a different editor.
/// </summary>
public sealed partial class WorkspacePanel
{
    private void OnDocumentTabContextMenuAction(DocumentTab tab, DocumentTabMenuAction action)
    {
        switch (action)
        {
            case DocumentTabMenuAction.Close:
                CloseTab(tab);
                break;
            case DocumentTabMenuAction.CloseOthers:
                CloseOtherTabs(tab);
                break;
            case DocumentTabMenuAction.CloseOthersRight:
                CloseOtherTabsRight(tab);
                break;
            case DocumentTabMenuAction.CloseOthersLeft:
                CloseOtherTabsLeft(tab);
                break;
            case DocumentTabMenuAction.CloseAll:
                CloseAllTabs(tab);
                break;
            case DocumentTabMenuAction.MoveToPrimarySection:
                MoveTabWithinArea(tab, toSecondarySection: false);
                break;
            case DocumentTabMenuAction.MoveToSecondarySection:
                MoveTabWithinArea(tab, toSecondarySection: true);
                break;
            case DocumentTabMenuAction.UnsplitArea:
                UnsplitArea(tab);
                break;
            case DocumentTabMenuAction.CopyResourceKey:
                CopyResourceKeyForTab(tab);
                break;
            case DocumentTabMenuAction.CopyFilePath:
                CopyFilePathForTab(tab);
                break;
            case DocumentTabMenuAction.SelectFile:
                SelectFileForTab(tab);
                break;
            case DocumentTabMenuAction.OpenFileExplorer:
                OpenFileExplorerForTab(tab);
                break;
            case DocumentTabMenuAction.OpenApplication:
                OpenApplicationForTab(tab);
                break;
            case DocumentTabMenuAction.RestoreChrome:
                RestoreChromeForTab(tab);
                break;
            case DocumentTabMenuAction.Reopen:
                _ = ReopenTab(tab);
                break;
            case DocumentTabMenuAction.ReopenWith:
                _ = ReopenTabWithDialog(tab);
                break;
        }
    }

    private void CloseTab(DocumentTab tab)
    {
        var fileResource = tab.ViewModel.FileResource;
        ViewModel.OnCloseDocumentRequested(fileResource);
    }

    // Moves a tab between the two sections of its own area, splitting the area first when it is not split
    // yet. Moving between areas is a drag.
    private void MoveTabWithinArea(DocumentTab tab, bool toSecondarySection)
    {
        var area = tab.Section.GetArea();

        if (!SectionContainer.Areas.IsAreaSplit(area))
        {
            // Only the split direction is on offer while unsplit, and only while the area has a document
            // to leave behind.
            if (!toSecondarySection ||
                !SectionContainer.Areas.CanStartAreaSplit(area))
            {
                return;
            }

            SectionContainer.Areas.SetAreaSplit(area, true);
        }

        var targetSection = toSecondarySection
            ? area.GetSecondarySection()
            : area.GetPrimarySection();

        if (SectionContainer.MoveTabToSection(tab, targetSection))
        {
            UpdateAllTabDisplayNames();
            NotifyLayoutChanged();
        }
    }

    // Folds a split area back, merging both sections into the primary one.
    private void UnsplitArea(DocumentTab tab)
    {
        var area = tab.Section.GetArea();
        if (!SectionContainer.Areas.IsAreaSplit(area))
        {
            return;
        }

        SectionContainer.Areas.SetAreaSplit(area, false);

        UpdateAllTabDisplayNames();
        NotifyLayoutChanged();
    }

    private void CloseOtherTabs(DocumentTab keepTab)
    {
        // Find which section contains the tab to keep
        var location = SectionContainer.FindDocumentTab(keepTab.ViewModel.FileResource);
        if (location is null)
        {
            return;
        }

        var sectionView = location.SectionView;

        var tabsToClose = new List<ResourceKey>();

        // Only close other tabs within the same section.
        foreach (var documentTab in sectionView.GetAllTabs())
        {
            if (documentTab != keepTab)
            {
                tabsToClose.Add(documentTab.ViewModel.FileResource);
            }
        }

        foreach (var fileResource in tabsToClose)
        {
            ViewModel.OnCloseDocumentRequested(fileResource);
        }
    }

    private void CloseOtherTabsRight(DocumentTab referenceTab)
    {
        // Find which section contains the reference tab
        var location = SectionContainer.FindDocumentTab(referenceTab.ViewModel.FileResource);
        if (location is null)
        {
            return;
        }

        var sectionView = location.SectionView;

        var tabsToClose = new List<ResourceKey>();
        bool foundReference = false;

        // Close tabs to the right within the same section.
        foreach (var documentTab in sectionView.GetAllTabs())
        {
            if (foundReference)
            {
                tabsToClose.Add(documentTab.ViewModel.FileResource);
            }
            if (documentTab == referenceTab)
            {
                foundReference = true;
            }
        }

        foreach (var fileResource in tabsToClose)
        {
            ViewModel.OnCloseDocumentRequested(fileResource);
        }
    }

    private void CloseOtherTabsLeft(DocumentTab referenceTab)
    {
        // Find which section contains the reference tab
        var location = SectionContainer.FindDocumentTab(referenceTab.ViewModel.FileResource);
        if (location is null)
        {
            return;
        }

        var sectionView = location.SectionView;

        var tabsToClose = new List<ResourceKey>();

        // Close tabs to the left within the same section.
        foreach (var documentTab in sectionView.GetAllTabs())
        {
            if (documentTab == referenceTab)
            {
                break;
            }
            tabsToClose.Add(documentTab.ViewModel.FileResource);
        }

        foreach (var fileResource in tabsToClose)
        {
            ViewModel.OnCloseDocumentRequested(fileResource);
        }
    }

    private void CloseAllTabs(DocumentTab referenceTab)
    {
        // Find which section contains the reference tab
        var location = SectionContainer.FindDocumentTab(referenceTab.ViewModel.FileResource);
        if (location is null)
        {
            return;
        }

        var sectionView = location.SectionView;

        var tabsToClose = new List<ResourceKey>();

        // Only close tabs within the same section.
        foreach (var documentTab in sectionView.GetAllTabs())
        {
            tabsToClose.Add(documentTab.ViewModel.FileResource);
        }

        foreach (var fileResource in tabsToClose)
        {
            ViewModel.OnCloseDocumentRequested(fileResource);
        }
    }

    private void SelectFileForTab(DocumentTab tab)
    {
        ViewModel.SelectFileForTab(tab.ViewModel.FileResource);
    }

    private void CopyResourceKeyForTab(DocumentTab tab)
    {
        ViewModel.CopyResourceKeyForTab(tab.ViewModel.FileResource);
    }

    private void CopyFilePathForTab(DocumentTab tab)
    {
        ViewModel.CopyFilePathForTab(tab.ViewModel.FilePath);
    }

    private void OpenFileExplorerForTab(DocumentTab tab)
    {
        ViewModel.OpenFileExplorerForTab(tab.ViewModel.FileResource);
    }

    private void OpenApplicationForTab(DocumentTab tab)
    {
        ViewModel.OpenApplicationForTab(tab.ViewModel.FileResource);
    }

    private void RestoreChromeForTab(DocumentTab tab)
    {
        if (tab.Content is IDocumentChromeOwner chromeOwner)
        {
            chromeOwner.RestoreChrome();
        }
    }

    private Task ReopenTab(DocumentTab tab)
    {
        // Reopen using the current editor (no dialog)
        return ReopenTabWithEditor(tab, tab.ViewModel.EditorId);
    }

    private async Task ReopenTabWithDialog(DocumentTab tab)
    {
        var fileResource = tab.ViewModel.FileResource;

        var selectedEditorId = tab.ViewModel.EditorId;

        var pickList = ViewModel.GetEditorPickList(fileResource, tab.ViewModel.EditorId);
        if (pickList is not null)
        {
            // Multiple editors available, show choice dialog.
            var title = _stringLocalizer.GetString("OpenWithDialog_Title");
            var message = _stringLocalizer.GetString("OpenWithDialog_Message");

            var choiceResult = await _dialogService.ShowChoiceDialogAsync(
                title, message, pickList.Labels, pickList.SelectedIndex, checkbox: null);
            if (choiceResult.IsFailure)
            {
                return;
            }

            selectedEditorId = pickList.EditorIds[choiceResult.Value.SelectedIndex];

            await ViewModel.SetPreferredEditorAsync(fileResource, selectedEditorId);
        }

        await ReopenTabWithEditor(tab, selectedEditorId);
    }

    private async Task ReopenTabWithEditor(DocumentTab tab, EditorId editorId)
    {
        var fileResource = tab.ViewModel.FileResource;

        // Capture state before closing so we can restore it after reopening
        var section = tab.Section;
        var currentLocation = SectionContainer.FindDocumentTab(fileResource);
        var tabIndex = currentLocation?.SectionView.GetTabIndex(tab) ?? 0;

        string? editorState = null;
        if (tab.ViewModel.DocumentView is not null)
        {
            editorState = await tab.ViewModel.DocumentView.TrySaveEditorStateAsync();
        }

        // Close then reopen via the command service, which processes them sequentially
        var closeResult = await _commandService.ExecuteAsync<ICloseDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
        });

        if (closeResult.IsFailure)
        {
            return;
        }

        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
            command.EditorId = editorId;
            command.EditorStateJson = editorState;
            command.TargetSection = section;
            command.TargetTabIndex = tabIndex;
        });
    }
}
