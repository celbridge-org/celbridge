namespace Celbridge.Documents.Views;

/// <summary>
/// Where to place a utility when docking it into a document tab. A null Address docks into Main's primary
/// section and appends the tab. A non-null Address targets a specific section and tab order. Activate
/// selects the docked tab and makes it the active document.
/// </summary>
public record DockUtilityPlacement(DocumentAddress? Address, bool Activate);

/// <summary>
/// Utility docking support for DocumentsPanel: presenting a utility as a document tab that borrows the
/// utility's live WebView, and returning it to the Utility Panel.
/// </summary>
public sealed partial class DocumentsPanel
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

        var address = placement.Address;

        DocumentSection section;
        if (address is not null)
        {
            section = EnsureSectionMounted(address.Section);
        }
        else
        {
            section = DocumentLayoutHelper.DefaultOpenSection;
        }

        var sectionView = SectionContainer.GetSection(section);

        var documentTab = new DocumentTab();
        documentTab.ViewModel.FileResource = resource;
        documentTab.ViewModel.FilePath = filePath;
        documentTab.ViewModel.EditorId = editorId;
        ApplyUtilityTabMetadata(documentTab, editorId);

        var dockedView = new DockedUtilityDocumentView(_serviceProvider, _messengerService, panelView.Controller);
        dockedView.EditorId = editorId;
        dockedView.Bind(resource, filePath);

        if (address is not null)
        {
            sectionView.InsertTab(documentTab, address.TabOrder);
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
            SectionContainer.ActivateDocument(resource, section);
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
            SectionContainer.ActivateDocument(resource, location.SectionView.Section);
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

    private void ApplyUtilityTabMetadata(DocumentTab documentTab, EditorId editorId)
    {
        var utilityInfo = ViewModel.ResolveUtilityTabInfo(editorId);
        if (utilityInfo is null)
        {
            return;
        }

        documentTab.ViewModel.IsUtility = true;
        documentTab.ViewModel.UtilityIconName = utilityInfo.IconName;
        documentTab.ViewModel.DocumentName = utilityInfo.Title;
        documentTab.ViewModel.UtilityTooltip = utilityInfo.Tooltip;
    }
}
