using Celbridge.WorkspaceUI.Views;

namespace Celbridge.Documents.Views;

/// <summary>
/// Identifies an open document by its containing section and the tab that hosts its view.
/// </summary>
public record DocumentTabLocation(DocumentSectionView SectionView, DocumentTab Tab);

/// <summary>
/// Why the active document changed. Only a change that asks for the document without already holding the
/// keyboard carries focus to it.
/// </summary>
public enum ActiveDocumentChangeReason
{
    /// <summary>
    /// The user or a command made this document active: opening it, clicking its tab, moving it between
    /// sections, or closing the document that was active before it.
    /// </summary>
    Activated,

    /// <summary>
    /// The workspace restored the document that was active when the project was last open.
    /// </summary>
    Restored,

    /// <summary>
    /// The document's own surface reported that it took the keyboard, which makes it the active document.
    /// </summary>
    Focused
}

/// <summary>
/// Owns the document sections and the documents in them: which section holds which tab, which document is
/// active, what takes over when one closes, and moving tabs between sections. The geometry of the areas the
/// sections are laid out in belongs to DocumentAreaLayout, which this creates and exposes.
/// </summary>
public sealed partial class DocumentSectionContainer
{
    private readonly DocumentAreaLayout _areaLayout;
    private readonly Dictionary<DocumentSection, DocumentSectionView> _sections = new();

    private DocumentSection _activeSection = DocumentSection.MainLeft;
    private ResourceKey _activeDocument = ResourceKey.Empty;

    /// <summary>
    /// Event raised when the active document changes. This is the document that should be inspected, and
    /// unless it was restored, the document the keyboard follows.
    /// </summary>
    public event Action<ResourceKey, ActiveDocumentChangeReason>? ActiveDocumentChanged;

    /// <summary>
    /// Event raised when the open documents in any section change.
    /// </summary>
    public event Action<DocumentSectionView, List<ResourceKey>>? DocumentsLayoutChanged;

    /// <summary>
    /// Event raised when a tab close is requested in any section.
    /// </summary>
    public event Action<DocumentSectionView, ResourceKey>? CloseRequested;

    /// <summary>
    /// Event raised when a context menu action is requested on a document tab.
    /// </summary>
    public event Action<DocumentSectionView, DocumentTab, DocumentTabMenuAction>? ContextMenuActionRequested;

    /// <summary>
    /// Event raised when resource files are dropped into a section from the ResourceTree, with the
    /// insertion slot in the tab order the drop point maps to.
    /// </summary>
    public event Action<DocumentSectionView, List<IResource>, int>? FilesDropped;

    /// <summary>
    /// The geometry of the areas the sections are laid out in: area visibility, splits and their splitters,
    /// the section floors and the section chrome.
    /// </summary>
    public DocumentAreaLayout Areas => _areaLayout;

    /// <summary>
    /// Gets the active document - the document being inspected.
    /// </summary>
    public ResourceKey ActiveDocument => _activeDocument;

    /// <summary>
    /// Gets the section containing the active document.
    /// </summary>
    public DocumentSection ActiveSection => _activeSection;

    public DocumentSectionContainer(WorkspaceSurfaceContainer surfaceContainer)
    {
        // Every section exists for the lifetime of the container: a collapsed area keeps its tabs while
        // its sections are unmounted from the visual tree. They are created before the area layout so it
        // has a section to lay out in every grid it builds.
        foreach (var section in DocumentLayoutHelper.AllSections)
        {
            CreateSection(section);
        }

        _areaLayout = new DocumentAreaLayout(surfaceContainer, GetSection, MigrateSecondarySection);
    }

    /// <summary>
    /// Gets the view for the named section.
    /// </summary>
    public DocumentSectionView GetSection(DocumentSection section)
    {
        return _sections[section];
    }

    /// <summary>
    /// Gets every mounted section, in reading order.
    /// </summary>
    public IEnumerable<DocumentSectionView> GetMountedSections()
    {
        foreach (var section in _areaLayout.VisibleSections)
        {
            yield return _sections[section];
        }
    }

    /// <summary>
    /// Gets every section, mounted or not, in reading order.
    /// </summary>
    public IEnumerable<DocumentSectionView> GetAllSections()
    {
        foreach (var section in DocumentLayoutHelper.AllSections)
        {
            yield return _sections[section];
        }
    }

    /// <summary>
    /// Locates the open document tab for the given resource and the section that contains it.
    /// Returns null when no tab is currently open for the resource.
    /// </summary>
    public DocumentTabLocation? FindDocumentTab(ResourceKey fileResource)
    {
        foreach (var sectionView in GetAllSections())
        {
            var tab = sectionView.GetDocumentTab(fileResource);
            if (tab is not null)
            {
                return new DocumentTabLocation(sectionView, tab);
            }
        }

        return null;
    }

    /// <summary>
    /// Makes the specified document the active document. Every caller states its reason, because the reason
    /// decides whether the keyboard follows and a caller that already holds it must not ask for it again.
    /// </summary>
    public void ActivateDocument(
        ResourceKey fileResource,
        DocumentSection section,
        ActiveDocumentChangeReason reason)
    {
        if (fileResource.IsEmpty)
        {
            return;
        }

        // Enforce the invariant: the active document's tab must be the selected tab in its section
        var sectionView = _sections[section];
        var tab = sectionView.GetDocumentTab(fileResource);
        if (tab is not null)
        {
            sectionView.SelectTab(tab);
        }

        _activeSection = section;
        _activeDocument = fileResource;

        UpdateTabSelectionIndicators();

        ActiveDocumentChanged?.Invoke(_activeDocument, reason);
    }

    /// <summary>
    /// Called when a document is about to be closed. If it's the active document,
    /// selects the next best document (closest tab in same section, or from other sections).
    /// </summary>
    public void HandleDocumentClosing(ResourceKey closingResource, DocumentSection closingSection, int closingTabIndex)
    {
        // Only need to select another document if the closing one is the active document
        if (closingResource != _activeDocument)
        {
            return;
        }

        var nextDocument = FindNextDocumentToSelect(closingSection, closingTabIndex);

        if (nextDocument is not null)
        {
            _activeSection = nextDocument.Section;
            _activeDocument = nextDocument.Resource;

            var sectionView = _sections[nextDocument.Section];
            var tab = sectionView.GetDocumentTab(nextDocument.Resource);
            if (tab is not null)
            {
                sectionView.SelectTab(tab);
            }

            UpdateTabSelectionIndicators();
            ActiveDocumentChanged?.Invoke(_activeDocument, ActiveDocumentChangeReason.Activated);
        }
        else
        {
            // No documents left to select
            _activeDocument = ResourceKey.Empty;
            _activeSection = DocumentSection.MainLeft;
            UpdateTabSelectionIndicators();
            ActiveDocumentChanged?.Invoke(_activeDocument, ActiveDocumentChangeReason.Activated);
        }
    }

    /// <summary>
    /// The document that takes over as active when the current one closes.
    /// </summary>
    private record NextDocument(ResourceKey Resource, DocumentSection Section);

    /// <summary>
    /// Finds the next best document to select when a document is closed.
    /// Prefers documents in the same section (closest to the closed tab's position),
    /// then falls back to other mounted sections in reading order.
    /// </summary>
    private NextDocument? FindNextDocumentToSelect(DocumentSection closingSection, int closingTabIndex)
    {
        // First, try to find a document in the same section
        var sameSectionView = _sections[closingSection];
        var tabsInSection = sameSectionView.GetAllTabs().ToList();

        // Account for the tab that's being closed (it's still in the list)
        int remainingTabs = tabsInSection.Count - 1;

        if (remainingTabs > 0)
        {
            // If there's a tab to the right, select it; otherwise select the one to the left
            int nextIndex = closingTabIndex < remainingTabs
                ? closingTabIndex + 1
                : closingTabIndex - 1;

            if (nextIndex >= 0 && nextIndex < tabsInSection.Count)
            {
                var nextTab = tabsInSection[nextIndex];
                if (nextTab.ViewModel.FileResource != _activeDocument)
                {
                    return new NextDocument(nextTab.ViewModel.FileResource, closingSection);
                }
            }

            // If the calculated index didn't work, try any other tab in the section
            foreach (var tab in tabsInSection)
            {
                if (tab.ViewModel.FileResource != _activeDocument)
                {
                    return new NextDocument(tab.ViewModel.FileResource, closingSection);
                }
            }
        }

        // No documents left in the same section, so scan the other selectable sections in reading order.
        foreach (var section in _areaLayout.SelectableSections)
        {
            if (section == closingSection)
            {
                continue;
            }

            var firstTab = _sections[section].GetAllTabs().FirstOrDefault();
            if (firstTab is not null)
            {
                return new NextDocument(firstTab.ViewModel.FileResource, section);
            }
        }

        return null;
    }

    /// <summary>
    /// Restores the given document as the active document, falling back to the selected tab of the
    /// first populated section when it is empty or no longer open. Guarantees that while any
    /// documents are open, exactly one is the active document - the invariant restore depends on.
    /// </summary>
    public void SetActiveDocument(ResourceKey fileResource)
    {
        // Restore inserts tabs with Activate=false, so a section can end up with tabs but no
        // selected tab. Give every populated section a selected tab first, so the fallback can
        // choose from real per-section selections.
        EnsureVisibleTabsSelected();

        // Prefer the requested (previously active) document when it is still open.
        var location = fileResource.IsEmpty ? null : FindDocumentTab(fileResource);

        // The requested document can be empty (never saved) or point to a document that failed to
        // restore (e.g. its file was deleted between sessions). Fall back to the selected tab of
        // the first populated section, scanning sections in reading order.
        if (location is null)
        {
            location = FindFallbackActiveDocument();
        }

        ApplyActiveDocument(location, ActiveDocumentChangeReason.Restored);
    }

    /// <summary>
    /// Hands the active document to whatever is still open when the one recorded as active no longer
    /// has a tab. For a caller that kept a closing document active because it meant to put it straight
    /// back, and could not.
    /// </summary>
    public void ReconcileMissingActiveDocument()
    {
        if (_activeDocument.IsEmpty
            || FindDocumentTab(_activeDocument) is not null)
        {
            return;
        }

        // The keyboard follows, because the surface that held it has been torn down.
        ApplyActiveDocument(FindFallbackActiveDocument(), ActiveDocumentChangeReason.Activated);
    }

    // Makes the located document active, or records that none is. A null location means no section has
    // a tab to hand it to.
    private void ApplyActiveDocument(DocumentTabLocation? location, ActiveDocumentChangeReason reason)
    {
        if (location is not null)
        {
            // Directly update the active document; programmatic selection does not rely on events.
            location.SectionView.SelectTab(location.Tab);
            _activeSection = location.SectionView.Section;
            _activeDocument = location.Tab.ViewModel.FileResource;
        }
        else
        {
            // No documents are open, so there is no active document.
            _activeDocument = ResourceKey.Empty;
            _activeSection = DocumentSection.MainLeft;
        }

        UpdateTabSelectionIndicators();
        ActiveDocumentChanged?.Invoke(_activeDocument, reason);
    }

    /// <summary>
    /// Returns the selected document tab of the first populated selectable section, scanning in reading
    /// order, or null when no section has a selected tab.
    /// </summary>
    private DocumentTabLocation? FindFallbackActiveDocument()
    {
        foreach (var section in _areaLayout.SelectableSections)
        {
            var sectionView = _sections[section];
            var selectedResource = sectionView.GetSelectedDocument();
            if (selectedResource.IsEmpty)
            {
                continue;
            }

            var tab = sectionView.GetDocumentTab(selectedResource);
            if (tab is not null)
            {
                return new DocumentTabLocation(sectionView, tab);
            }
        }

        return null;
    }

    /// <summary>
    /// Moves a tab from its current section to the target section, appending it to the tab strip or
    /// inserting it at the given insertion slot.
    /// </summary>
    public bool MoveTabToSection(DocumentTab tab, DocumentSection targetSection, int? insertionSlot = null)
    {
        var location = FindDocumentTab(tab.ViewModel.FileResource);
        if (location is null)
        {
            return false;
        }

        var sourceSectionView = location.SectionView;
        var targetSectionView = _sections[targetSection];
        if (sourceSectionView == targetSectionView)
        {
            return false; // Already in the target section
        }

        bool wasSelectedInSource = sourceSectionView.GetSelectedDocument() == tab.ViewModel.FileResource;
        int sourceTabIndex = sourceSectionView.GetTabIndex(tab);

        sourceSectionView.RemoveTab(tab);
        if (insertionSlot is int slot)
        {
            targetSectionView.InsertTab(tab, slot);
        }
        else
        {
            targetSectionView.AddTab(tab);
        }
        targetSectionView.SelectTab(tab);

        // Restore a visible selection in the source section. The Uno Skia TabView does not reliably
        // select a neighbouring tab when its selected tab is removed, which leaves every tab in the
        // strip rendered in the unselected style (the whole row reads as disabled).
        if (wasSelectedInSource &&
            sourceSectionView.TabCount > 0)
        {
            int neighbourIndex = Math.Clamp(sourceTabIndex, 0, sourceSectionView.TabCount - 1);
            var neighbourTab = sourceSectionView.GetAllTabs().ElementAt(neighbourIndex);
            sourceSectionView.SelectTab(neighbourTab);
        }

        // Always make the moved tab the active document
        _activeSection = targetSection;
        _activeDocument = tab.ViewModel.FileResource;

        // Emptying the source section folds its area back, which can migrate the moved tab straight back
        // out of the target section. Reconcile before reporting the active document, so the fold's own
        // correction to the active section is the one that is broadcast.
        _areaLayout.ReconcileAreaSplit(sourceSectionView.Section.GetArea());

        UpdateTabSelectionIndicators();
        ActiveDocumentChanged?.Invoke(_activeDocument, ActiveDocumentChangeReason.Activated);

        // Flash the tab at its new section so the address change stands out.
        tab.FlashAttentionDeferred();

        return true;
    }

    /// <summary>
    /// Updates tab strip visibility across all sections for presenter mode.
    /// </summary>
    public void UpdateTabStripVisibility(bool showTabStrip)
    {
        foreach (var sectionView in GetAllSections())
        {
            sectionView.UpdateTabStripVisibility(showTabStrip);
        }
    }

    /// <summary>
    /// Shuts down all sections.
    /// </summary>
    public void Shutdown()
    {
        foreach (var sectionView in GetAllSections())
        {
            sectionView.Shutdown();
        }
    }

    private void CreateSection(DocumentSection section)
    {
        var sectionView = new DocumentSectionView
        {
            Section = section
        };

        sectionView.DocumentsLayoutChanged += OnSectionDocumentsLayoutChanged;
        sectionView.CloseRequested += OnSectionCloseRequested;
        sectionView.ContextMenuActionRequested += OnSectionContextMenuActionRequested;
        sectionView.TabDroppedInside += OnSectionTabDroppedInside;
        sectionView.FilesDropped += OnSectionFilesDropped;
        sectionView.TabPointerPressed += OnSectionTabPointerPressed;

        _sections[section] = sectionView;
    }

    /// <summary>
    /// Moves every tab in an area's secondary section into its primary one, ahead of the secondary
    /// section being unmounted.
    /// </summary>
    private void MigrateSecondarySection(DocumentArea area)
    {
        var primarySectionView = _sections[area.GetPrimarySection()];
        var secondarySectionView = _sections[area.GetSecondarySection()];

        var tabsToMove = secondarySectionView.GetAllTabs().ToList();
        foreach (var tab in tabsToMove)
        {
            secondarySectionView.RemoveTab(tab);
            primarySectionView.AddTab(tab);
        }

        if (_activeSection == area.GetSecondarySection())
        {
            _activeSection = area.GetPrimarySection();
        }

        // Migrating tabs does not re-select the active document in the target section, so re-apply it.
        if (!_activeDocument.IsEmpty)
        {
            var activeLocation = FindDocumentTab(_activeDocument);
            if (activeLocation is not null)
            {
                activeLocation.SectionView.SelectTab(activeLocation.Tab);
            }
        }

        UpdateTabSelectionIndicators();

        foreach (var tab in tabsToMove)
        {
            tab.FlashAttentionDeferred();
        }
    }

    /// <summary>
    /// Updates the visual selection indicators on all tabs across all sections.
    /// </summary>
    private void UpdateTabSelectionIndicators()
    {
        foreach (var sectionView in GetAllSections())
        {
            bool isActiveSection = sectionView.Section == _activeSection;

            foreach (var tab in sectionView.GetAllTabs())
            {
                bool isActiveDocument = isActiveSection &&
                    tab.ViewModel.FileResource == _activeDocument;
                tab.UpdateActiveDocumentState(isActiveDocument);
            }
        }
    }

    private void OnSectionDocumentsLayoutChanged(DocumentSectionView sectionView, List<ResourceKey> documents)
    {
        DocumentsLayoutChanged?.Invoke(sectionView, documents);
    }

    private void OnSectionCloseRequested(DocumentSectionView sectionView, ResourceKey fileResource)
    {
        CloseRequested?.Invoke(sectionView, fileResource);
    }

    private void OnSectionContextMenuActionRequested(DocumentSectionView sectionView, DocumentTab tab, DocumentTabMenuAction action)
    {
        ContextMenuActionRequested?.Invoke(sectionView, tab, action);
    }

    private void OnSectionTabDroppedInside(DocumentSectionView targetSection, DocumentTab tab)
    {
        if (MoveTabToSection(tab, targetSection.Section))
        {
            NotifyLayoutChanged();
        }
    }

    private void OnSectionFilesDropped(DocumentSectionView targetSection, List<IResource> resources, int insertionSlot)
    {
        FilesDropped?.Invoke(targetSection, resources, insertionSlot);
    }

    /// <summary>
    /// Ensures each section that contains tabs has a selected tab.
    /// Sections that already have a selection are left unchanged.
    /// </summary>
    private void EnsureVisibleTabsSelected()
    {
        foreach (var sectionView in GetAllSections())
        {
            if (sectionView.TabCount > 0 && sectionView.GetSelectedDocument().IsEmpty)
            {
                var firstTab = sectionView.GetAllTabs().First();
                sectionView.SelectTab(firstTab);
            }
        }
    }

    private void NotifyLayoutChanged()
    {
        // Re-fire the layout notification for every section so the stored addresses stay in step.
        foreach (var sectionView in GetAllSections())
        {
            var documents = sectionView.GetOpenDocuments();
            DocumentsLayoutChanged?.Invoke(sectionView, documents);
        }
    }
}
