using Celbridge.Commands;
using Celbridge.Platform;

namespace Celbridge.Documents.Views;

/// <summary>
/// Document tab context-menu handling for WorkspacePanel: the close family, moving a tab between the
/// sections of its area, the clipboard and reveal actions, and reopening.
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
            case DocumentTabMenuAction.CopyRelativePath:
                CopyRelativePathForTab(tab);
                ReturnKeyboardAfterMenuItem();
                break;
            case DocumentTabMenuAction.CopyFullPath:
                CopyFullPathForTab(tab);
                ReturnKeyboardAfterMenuItem();
                break;
            case DocumentTabMenuAction.RevealInExplorer:
                RevealInExplorerForTab(tab);
                break;
            case DocumentTabMenuAction.OpenFileExplorer:
                OpenFileExplorerForTab(tab);
                ReturnKeyboardAfterMenuItem();
                break;
            case DocumentTabMenuAction.OpenApplication:
                OpenApplicationForTab(tab);
                ReturnKeyboardAfterMenuItem();
                break;
            case DocumentTabMenuAction.Reopen:
                ReopenTab(tab);
                break;
        }
    }

    // UNO-BUG: on macOS, closing a menu drops the keyboard on the window's first focusable element, and that element
    // ignores keys. On Windows, the keyboard returns to the tab's header.
    private void ReturnKeyboardAfterMenuItem()
    {
        var platformInfo = _serviceProvider.AcquireService<IPlatformInfo>();
        if (platformInfo.HostedWebViewFocusIsNative)
        {
            FocusActiveDocument();
        }
    }

    private void CloseTab(DocumentTab tab)
    {
        var fileResource = tab.ViewModel.FileResource;
        _ = CloseFromTabStripAsync([fileResource]);
    }

    // Closes documents for a tab strip gesture, then returns the keyboard to the active document if that document
    // stayed active. The gesture takes the keyboard off the document.
    private async Task CloseFromTabStripAsync(IReadOnlyList<ResourceKey> fileResources)
    {
        var activeDocument = ActiveDocument;
        await Task.WhenAll(fileResources.Select(ViewModel.CloseDocumentAsync));

        // Activating another document already gives that document the keyboard.
        if (!_isShuttingDown
            && ActiveDocument == activeDocument)
        {
            FocusActiveDocument();
        }
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

        // Folding the area moves the second section's tabs. A moved tab gives up the keyboard if the keyboard
        // was in the tab. Unsplit All then activates the tab that showed the menu, and the activation carries
        // the keyboard to that tab.
        SectionContainer.ActivateDocument(
            tab.ViewModel.FileResource,
            tab.Section,
            ActiveDocumentChangeReason.Activated);

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

        _ = CloseFromTabStripAsync(tabsToClose);
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

        _ = CloseFromTabStripAsync(tabsToClose);
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

        _ = CloseFromTabStripAsync(tabsToClose);
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

        _ = CloseFromTabStripAsync(tabsToClose);
    }

    private void RevealInExplorerForTab(DocumentTab tab)
    {
        ViewModel.RevealInExplorerForTab(tab.ViewModel.FileResource);
    }

    private void CopyRelativePathForTab(DocumentTab tab)
    {
        ViewModel.CopyRelativePathForTab(tab.ViewModel.FileResource);
    }

    private void CopyFullPathForTab(DocumentTab tab)
    {
        ViewModel.CopyFullPathForTab(tab.ViewModel.FilePath);
    }

    private void OpenFileExplorerForTab(DocumentTab tab)
    {
        ViewModel.OpenFileExplorerForTab(tab.ViewModel.FileResource);
    }

    private void OpenApplicationForTab(DocumentTab tab)
    {
        ViewModel.OpenApplicationForTab(tab.ViewModel.FileResource);
    }

    // Closes the document and opens the document again in the same editor and the same place in the
    // section. The editor's view state is restored.
    private void ReopenTab(DocumentTab tab)
    {
        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = tab.ViewModel.FileResource;
            command.Refresh = DocumentRefresh.Reopen;
        });
    }
}
