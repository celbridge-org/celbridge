namespace Celbridge.Documents.Views;

/// <summary>
/// Where to place a utility when docking it into a document tab. Section names the section the tab lands in,
/// and a null TabOrder appends the tab rather than inserting it at a stored position. Activate selects the
/// docked tab and makes it the active document.
/// </summary>
public record DockUtilityPlacement(DocumentSection Section, int? TabOrder, bool Activate);

/// <summary>
/// Utility docking support for WorkspacePanel: presenting a utility as a document tab that borrows the
/// utility's live WebView, and returning it to the Utility Panel.
/// </summary>
public sealed partial class WorkspacePanel
{
    /// <summary>
    /// Docks a utility into a document tab: creates a tab hosting the utility's borrowed controller (reusing its
    /// live WebView) and stamps the utility tab metadata. The controller's WebView is reparented into the tab
    /// once it is in the visual tree.
    /// </summary>
    public Result DockUtility(CustomUtilityView panelView, DockUtilityPlacement placement)
    {
        var resource = panelView.FileResource;
        var editorId = panelView.UtilityId;

        var resolveResult = ViewModel.ResolveResourcePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for utility resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var filePath = resolveResult.Value;

        var section = EnsureSectionMounted(placement.Section);
        var sectionView = SectionContainer.GetSection(section);

        var documentTab = new DocumentTab();
        documentTab.ViewModel.FileResource = resource;
        documentTab.ViewModel.FilePath = filePath;
        documentTab.ViewModel.EditorId = editorId;
        ApplyEditorTabMetadata(documentTab, editorId);

        // The tab borrows a live utility rather than opening a document, which is what suppresses its open
        // and close announcements and sends its close back to the panel.
        documentTab.ViewModel.IsDockedUtility = true;

        var dockedView = new DockedUtilityDocumentView(_serviceProvider, _messengerService, panelView.Controller);
        dockedView.EditorId = editorId;
        dockedView.Bind(resource, filePath);

        var tabOrder = placement.TabOrder;
        if (tabOrder is not null)
        {
            sectionView.InsertTab(documentTab, tabOrder.Value);
        }
        else
        {
            sectionView.AddTab(documentTab);
        }

        // No open announcement: a utility is presented by docking, never opened as a document, and the
        // documents service refuses to open one. The view model suppresses the matching close.
        documentTab.ViewModel.DocumentView = dockedView;
        documentTab.Content = dockedView;

        if (placement.Activate)
        {
            sectionView.SelectTab(documentTab);
        }

        // Reparent the borrowed WebView into the tab now that the tab is in the visual tree.
        dockedView.Dock();

        if (placement.Activate)
        {
            SectionContainer.ActivateDocument(resource, section, ActiveDocumentChangeReason.Activated);
        }

        return Result.Ok();
    }

    /// <summary>
    /// Activates the open document tab for a docked utility (used when its rail button is clicked).
    /// </summary>
    public void ActivateUtilityTab(ResourceKey resource)
    {
        var location = SectionContainer.FindDocumentTab(resource);
        if (location is not null)
        {
            SectionContainer.ActivateDocument(
                resource,
                location.SectionView.Section,
                ActiveDocumentChangeReason.Activated);
        }
    }

    /// <summary>
    /// Removes a docked utility's document tab. The caller reparents the controller's WebView back to the
    /// Utility Panel first, so the tab (and its now-empty docked view) is dropped without any teardown.
    /// </summary>
    public void RemoveUtilityTab(ResourceKey resource)
    {
        var location = SectionContainer.FindDocumentTab(resource);
        if (location is null)
        {
            return;
        }

        var sectionView = location.SectionView;
        var documentTab = location.Tab;

        int tabIndex = sectionView.GetTabIndex(documentTab);
        SectionContainer.HandleDocumentClosing(resource, sectionView.Section, tabIndex);

        _ = documentTab.ViewModel.DocumentView?.PrepareToClose();
        RemoveTabFromSection(sectionView, documentTab);

        UpdateAllTabDisplayNames();
    }

    // Decides what a tab is called: a utility's manifest title, the title its editor gives its tabs, or
    // the file name. Idempotent, because a caller that does not name the editor applies it again once the
    // created view reports which editor it is.
    private void ApplyEditorTabMetadata(DocumentTab documentTab, EditorId editorId)
    {
        var utilityInfo = ViewModel.ResolveUtilityTabInfo(editorId);
        if (utilityInfo is not null)
        {
            documentTab.ViewModel.IsUtilityEditor = true;
            documentTab.ViewModel.HasFixedTitle = true;
            documentTab.ViewModel.UtilityIconName = utilityInfo.IconName;
            documentTab.ViewModel.DocumentName = utilityInfo.Title;
            documentTab.ViewModel.UtilityTooltip = utilityInfo.Tooltip;
            return;
        }

        documentTab.ViewModel.IsUtilityEditor = false;

        var editorTabTitle = ViewModel.ResolveEditorTabTitle(editorId);
        documentTab.ViewModel.HasFixedTitle = !string.IsNullOrEmpty(editorTabTitle);
        documentTab.ViewModel.DocumentName = documentTab.ViewModel.HasFixedTitle
            ? editorTabTitle
            : documentTab.ViewModel.FileResource.ResourceName;
    }
}
