using Celbridge.Platform;
using Celbridge.UserInterface.Helpers;
using Celbridge.UserInterface.Views.Controls;

namespace Celbridge.Documents.Views;

/// <summary>
/// Identifies an open document by its containing section and the tab that hosts its view.
/// </summary>
public record DocumentTabLocation(DocumentSectionView SectionView, DocumentTab Tab);

/// <summary>
/// Container that manages the three document areas, the sections within them, and the splitters that
/// size both.
/// </summary>
public sealed partial class DocumentSectionContainer : UserControl
{
    private const double MinSectionWidth = 200;
    private const double MinSectionHeight = 120;
    private const double MinBottomAreaHeight = 150;
    private const double MinSideAreaWidth = 200;
    private const double MinMainAreaWidth = 200;
    private const double MinMainAreaHeight = 150;
    private const double EdgeThickness = 1.0;
    private const double MinDragDistance = 5.0; // Minimum pixels to count as a real drag
    private const double DefaultSplitRatio = 0.5;

    private readonly Dictionary<DocumentSection, DocumentSectionView> _sections = new();
    private readonly Dictionary<DocumentArea, bool> _areaSplit = new();
    private readonly Dictionary<DocumentArea, double> _areaSplitRatio = new();
    private readonly Dictionary<DocumentArea, Splitter> _splitSplitters = new();
    private readonly Dictionary<DocumentArea, SplitterHelper> _splitHelpers = new();
    private readonly Dictionary<DocumentArea, UIElement> _areaToolbars = new();
    private readonly HashSet<DocumentArea> _visibleAreas = new();
    private readonly IPlatformInfo _platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();

    private SplitterHelper? _bottomAreaSplitterHelper;
    private SplitterHelper? _sideAreaSplitterHelper;

    private double _totalDragDelta = 0;

    private DocumentSection _activeSection = DocumentSection.MainLeft;
    private DocumentArea? _isolatedArea;
    private bool _isUtilityPanelPresented = true;
    private ResourceKey _activeDocument = ResourceKey.Empty;

    /// <summary>
    /// Event raised when the selected document changes in any section.
    /// </summary>
    public event Action<DocumentSectionView, ResourceKey>? SectionSelectionChanged;

    /// <summary>
    /// Event raised when the active document changes.
    /// This is the document that should be inspected and determines where new documents open.
    /// </summary>
    public event Action<ResourceKey>? ActiveDocumentChanged;

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
    /// Event raised when an area's split state or split position changes.
    /// </summary>
    public event Action<DocumentArea, bool, double>? AreaLayoutChanged;

    /// <summary>
    /// Event raised when a collapsible area is resized, carrying its new height (Bottom) or width (Side).
    /// </summary>
    public event Action<DocumentArea, double>? AreaSizeChanged;

    /// <summary>
    /// Event raised when an area splitter is double-clicked, asking for that area's default size.
    /// </summary>
    public event Action<DocumentArea>? AreaSizeResetRequested;

    /// <summary>
    /// Event raised when resource files are dropped into a section from the ResourceTree, with the
    /// insertion slot in the tab order the drop point maps to.
    /// </summary>
    public event Action<DocumentSectionView, List<IResource>, int>? FilesDropped;

    /// <summary>
    /// The sections that are currently mounted, in reading order.
    /// </summary>
    public IReadOnlyList<DocumentSection> VisibleSections
    {
        get
        {
            var visible = new List<DocumentSection>();
            foreach (var section in DocumentLayoutHelper.AllSections)
            {
                if (IsSectionMounted(section))
                {
                    visible.Add(section);
                }
            }

            return visible;
        }
    }

    /// <summary>
    /// Gets the active document - the document being inspected and where new documents open.
    /// </summary>
    public ResourceKey ActiveDocument => _activeDocument;

    /// <summary>
    /// Gets the section containing the active document.
    /// </summary>
    public DocumentSection ActiveSection => _activeSection;

    public DocumentSectionContainer()
    {
        InitializeComponent();

        foreach (var area in DocumentLayoutHelper.AllAreas)
        {
            _areaSplit[area] = false;
            _areaSplitRatio[area] = DefaultSplitRatio;
            _visibleAreas.Add(area);
        }

        // Every section exists for the lifetime of the container: a collapsed area keeps its tabs while
        // its sections are unmounted from the visual tree.
        foreach (var section in DocumentLayoutHelper.AllSections)
        {
            CreateSection(section);
        }

        InitializeAreaSplitters();

        foreach (var area in DocumentLayoutHelper.AllAreas)
        {
            RebuildArea(area);
            WatchAreaSize(area);
        }

        ApplyRootGridLayout();
    }

    // An area that shrinks below the room for two sections can no longer be split, which the tab context
    // menu reflects.
    private void WatchAreaSize(DocumentArea area)
    {
        var areaGrid = GetAreaGrid(area);
        areaGrid.SizeChanged += (s, e) =>
        {
            UpdateSectionMoveTargets(area);
        };
    }

    /// <summary>
    /// Whether the area currently has room for two sections at their minimum size.
    /// </summary>
    public bool CanSplitArea(DocumentArea area)
    {
        var areaGrid = GetAreaGrid(area);

        if (area.SplitsHorizontally())
        {
            return areaGrid.ActualWidth >= MinSectionWidth * 2;
        }

        return areaGrid.ActualHeight >= MinSectionHeight * 2;
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
        foreach (var section in VisibleSections)
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
    /// Whether the section is currently in the visual tree: its area is presented and, for a secondary
    /// section, that area is split.
    /// </summary>
    public bool IsSectionMounted(DocumentSection section)
    {
        return IsAreaPresented(section.GetArea())
            && IsSectionInAreaLayout(section);
    }

    // Whether the area's split state lays the section out: a primary section always, a secondary one
    // only while its area is split.
    private bool IsSectionInAreaLayout(DocumentSection section)
    {
        return !section.IsSecondarySection() || _areaSplit[section.GetArea()];
    }

    /// <summary>
    /// The sections a fallback active document can be chosen from: those a visible area lays out,
    /// ignoring any isolation. Closing the last document in an isolated area moves to a document
    /// elsewhere rather than reporting that none are left, and the isolation follows it.
    /// </summary>
    private IEnumerable<DocumentSection> SelectableSections
    {
        get
        {
            foreach (var section in DocumentLayoutHelper.AllSections)
            {
                if (_visibleAreas.Contains(section.GetArea()) &&
                    IsSectionInAreaLayout(section))
                {
                    yield return section;
                }
            }
        }
    }

    /// <summary>
    /// The area currently shown on its own, or null when the areas are laid out normally.
    /// </summary>
    public DocumentArea? IsolatedArea => _isolatedArea;

    /// <summary>
    /// Shows a single area filling the whole panel, hiding the other two, or restores the normal layout
    /// when passed null. The isolated area keeps its own split, and every area's visibility, size and
    /// split state are left untouched underneath, so clearing the isolation restores what the user had.
    /// </summary>
    public void SetIsolatedArea(DocumentArea? area)
    {
        if (_isolatedArea == area)
        {
            return;
        }

        _isolatedArea = area;

        ApplyRootGridLayout();
    }

    /// <summary>
    /// Sets whether the Utility Panel is showing alongside the document areas, which decides whether the
    /// areas draw their left edge or leave it flush against the application border.
    /// </summary>
    public void SetUtilityPanelPresented(bool isPresented)
    {
        if (_isUtilityPanelPresented == isPresented)
        {
            return;
        }

        _isUtilityPanelPresented = isPresented;

        ApplyRootGridLayout();
    }

    // While an area is isolated it is the only one presented. Otherwise the collapsible areas follow the
    // surface visibility the user chose.
    private bool IsAreaPresented(DocumentArea area)
    {
        if (_isolatedArea is DocumentArea isolatedArea)
        {
            return isolatedArea == area;
        }

        return _visibleAreas.Contains(area);
    }

    /// <summary>
    /// Whether the area is currently showing both of its sections.
    /// </summary>
    public bool IsAreaSplit(DocumentArea area)
    {
        return _areaSplit[area];
    }

    /// <summary>
    /// Whether the area is currently visible. Main is always visible.
    /// </summary>
    public bool IsAreaVisible(DocumentArea area)
    {
        return _visibleAreas.Contains(area);
    }

    /// <summary>
    /// Gets the share of a split area taken by its primary section.
    /// </summary>
    public double GetAreaSplitRatio(DocumentArea area)
    {
        return _areaSplitRatio[area];
    }

    /// <summary>
    /// Splits the area into two sections, or folds its secondary section back into the primary one.
    /// Folding migrates the secondary section's tabs rather than closing them.
    /// </summary>
    public void SetAreaSplit(DocumentArea area, bool isSplit)
    {
        if (_areaSplit[area] == isSplit)
        {
            return;
        }

        _areaSplit[area] = isSplit;

        if (!isSplit)
        {
            MigrateSecondarySection(area);
        }

        RebuildArea(area);

        AreaLayoutChanged?.Invoke(area, isSplit, _areaSplitRatio[area]);
    }

    /// <summary>
    /// Whether a document in the area can be moved into a new split: the area must be unsplit, have room
    /// for two sections, and hold more than one document so the split does not empty its primary section.
    /// </summary>
    public bool CanStartAreaSplit(DocumentArea area)
    {
        if (_areaSplit[area] ||
            !CanSplitArea(area))
        {
            return false;
        }

        return _sections[area.GetPrimarySection()].TabCount > 1;
    }

    /// <summary>
    /// Folds a split area back when either of its sections has run out of documents, so a split section is
    /// never left empty. The surviving documents always end up in the primary section.
    /// </summary>
    public void ReconcileAreaSplit(DocumentArea area)
    {
        if (!_areaSplit[area])
        {
            return;
        }

        var primarySectionView = _sections[area.GetPrimarySection()];
        var secondarySectionView = _sections[area.GetSecondarySection()];

        if (primarySectionView.TabCount > 0 &&
            secondarySectionView.TabCount > 0)
        {
            return;
        }

        // Unsplitting migrates the secondary section's tabs into the primary one, which covers both
        // cases: an empty secondary migrates nothing, an empty primary receives everything.
        SetAreaSplit(area, false);
    }

    /// <summary>
    /// Sets the share of a split area taken by its primary section.
    /// </summary>
    public void SetAreaSplitRatio(DocumentArea area, double ratio)
    {
        if (double.IsNaN(ratio)
            || double.IsInfinity(ratio)
            || ratio <= 0
            || ratio >= 1)
        {
            return;
        }

        _areaSplitRatio[area] = ratio;

        if (_areaSplit[area])
        {
            ApplySplitRatio(area);
        }
    }

    /// <summary>
    /// Shows or hides an area. Hiding leaves its sections and their tabs intact, so the documents in a
    /// collapsed area stay open and reappear where they were. Main is always visible.
    /// </summary>
    public void SetAreaVisible(DocumentArea area, bool isVisible)
    {
        if (!area.IsCollapsible())
        {
            return;
        }

        if (isVisible)
        {
            _visibleAreas.Add(area);
        }
        else
        {
            _visibleAreas.Remove(area);
        }

        ApplyRootGridLayout();
    }

    /// <summary>
    /// Sets the height of the Bottom area or the width of the Side area. Ignored unless the area is
    /// presented alongside another one, because a sole presented area fills the panel.
    /// </summary>
    public void SetAreaSize(DocumentArea area, double size)
    {
        if (size <= 0 ||
            !IsAreaPresented(area))
        {
            return;
        }

        if (area == DocumentArea.Bottom)
        {
            if (IsAreaPresented(DocumentArea.Main))
            {
                BottomAreaRow.Height = new GridLength(size);
            }
        }
        else if (area == DocumentArea.Side)
        {
            if (IsAreaPresented(DocumentArea.Main) ||
                IsAreaPresented(DocumentArea.Bottom))
            {
                SideAreaColumn.Width = new GridLength(size);
            }
        }
    }

    /// <summary>
    /// Sets the toolbar hosted in an area's tab strip footer. The toolbar is re-placed on the section
    /// nearest the area's top-right corner whenever that area is rebuilt.
    /// </summary>
    public void SetAreaToolbar(DocumentArea area, UIElement toolbar)
    {
        _areaToolbars[area] = toolbar;
        PlaceAreaToolbar(area);
    }

    /// <summary>
    /// Finds the section containing a specific document, including sections in a collapsed area.
    /// </summary>
    public DocumentSectionView? FindSectionContaining(ResourceKey fileResource)
    {
        foreach (var sectionView in GetAllSections())
        {
            if (sectionView.ContainsDocument(fileResource))
            {
                return sectionView;
            }
        }

        return null;
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
    /// Makes the specified document the active document.
    /// </summary>
    public void ActivateDocument(ResourceKey fileResource, DocumentSection section)
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

        ActiveDocumentChanged?.Invoke(_activeDocument);
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
            ActiveDocumentChanged?.Invoke(_activeDocument);
        }
        else
        {
            // No documents left to select
            _activeDocument = ResourceKey.Empty;
            _activeSection = DocumentSection.MainLeft;
            UpdateTabSelectionIndicators();
            ActiveDocumentChanged?.Invoke(_activeDocument);
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
        foreach (var section in SelectableSections)
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
        ActiveDocumentChanged?.Invoke(_activeDocument);
    }

    /// <summary>
    /// Returns the selected document tab of the first populated selectable section, scanning in reading
    /// order, or null when no section has a selected tab.
    /// </summary>
    private DocumentTabLocation? FindFallbackActiveDocument()
    {
        foreach (var section in SelectableSections)
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
        ReconcileAreaSplit(sourceSectionView.Section.GetArea());

        UpdateTabSelectionIndicators();
        ActiveDocumentChanged?.Invoke(_activeDocument);

        // Flash the tab at its new section so the address change stands out.
        tab.FlashAttentionDeferred();

        return true;
    }

    /// <summary>
    /// Folds every area back to a single section and restores equal split positions.
    /// </summary>
    public async Task ResetAreaLayoutAsync()
    {
        foreach (var area in DocumentLayoutHelper.AllAreas)
        {
            _areaSplitRatio[area] = DefaultSplitRatio;
            SetAreaSplit(area, false);
        }

        var tcs = new TaskCompletionSource<bool>();

        // Wait for layout to complete so callers that persist the result read settled state.
        DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var area in DocumentLayoutHelper.AllAreas)
            {
                AreaLayoutChanged?.Invoke(area, false, DefaultSplitRatio);
            }

            tcs.SetResult(true);
        });

        await tcs.Task;
    }

    private void CreateSection(DocumentSection section)
    {
        var sectionView = new DocumentSectionView
        {
            Section = section
        };

        sectionView.SelectionChanged += OnSectionSelectionChanged;
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

    private Grid GetAreaGrid(DocumentArea area)
    {
        switch (area)
        {
            case DocumentArea.Main:
                return MainAreaGrid;

            case DocumentArea.Bottom:
                return BottomAreaGrid;

            default:
                return SideAreaGrid;
        }
    }

    /// <summary>
    /// Rebuilds an area's internal grid for its current split state. Sections that stay mounted are left
    /// attached: reparenting a section resets its TabView measurement and leaves the tab strip stuck in
    /// an overflow-scroll state until the next real resize.
    /// </summary>
    private void RebuildArea(DocumentArea area)
    {
        var areaGrid = GetAreaGrid(area);
        bool isSplit = _areaSplit[area];
        bool isHorizontal = area.SplitsHorizontally();

        var primarySectionView = _sections[area.GetPrimarySection()];
        var secondarySectionView = _sections[area.GetSecondarySection()];

        if (_splitSplitters.TryGetValue(area, out var existingSplitter))
        {
            existingSplitter.DragStarted -= Splitter_DragStarted;
            existingSplitter.DragDelta -= Splitter_DragDelta;
            existingSplitter.DragCompleted -= Splitter_DragCompleted;
            existingSplitter.DoubleClicked -= Splitter_DoubleClicked;
            areaGrid.Children.Remove(existingSplitter);
            _splitSplitters.Remove(area);
            _splitHelpers.Remove(area);
        }

        if (!isSplit &&
            areaGrid.Children.Contains(secondarySectionView))
        {
            areaGrid.Children.Remove(secondarySectionView);
        }

        areaGrid.ColumnDefinitions.Clear();
        areaGrid.RowDefinitions.Clear();

        double ratio = _areaSplitRatio[area];

        if (isHorizontal)
        {
            areaGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(isSplit ? ratio : 1, GridUnitType.Star),
                MinWidth = MinSectionWidth
            });
        }
        else
        {
            areaGrid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(isSplit ? ratio : 1, GridUnitType.Star),
                MinHeight = MinSectionHeight
            });
        }

        SetSectionPosition(primarySectionView, isHorizontal, 0);
        if (!areaGrid.Children.Contains(primarySectionView))
        {
            areaGrid.Children.Add(primarySectionView);
        }

        if (isSplit)
        {
            if (isHorizontal)
            {
                areaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                areaGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1 - ratio, GridUnitType.Star),
                    MinWidth = MinSectionWidth
                });
            }
            else
            {
                areaGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                areaGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = new GridLength(1 - ratio, GridUnitType.Star),
                    MinHeight = MinSectionHeight
                });
            }

            var splitter = CreateSplitSplitter(area, isHorizontal);
            SetSectionPosition(splitter, isHorizontal, 1);
            areaGrid.Children.Add(splitter);
            _splitSplitters[area] = splitter;

            SetSectionPosition(secondarySectionView, isHorizontal, 2);
            if (!areaGrid.Children.Contains(secondarySectionView))
            {
                areaGrid.Children.Add(secondarySectionView);
            }
        }

        UpdateSectionMoveTargets(area);
        PlaceAreaToolbar(area);
        ApplyAreaSectionChrome(area);
    }

    private static void SetSectionPosition(FrameworkElement element, bool isHorizontal, int index)
    {
        if (isHorizontal)
        {
            Grid.SetColumn(element, index);
            Grid.SetRow(element, 0);
        }
        else
        {
            Grid.SetRow(element, index);
            Grid.SetColumn(element, 0);
        }
    }

    private Splitter CreateSplitSplitter(DocumentArea area, bool isHorizontal)
    {
        var splitter = new Splitter
        {
            // A horizontally split area is divided by a vertical splitter, and the reverse.
            Orientation = isHorizontal ? Orientation.Vertical : Orientation.Horizontal,
            Tag = area
        };

        splitter.DragStarted += Splitter_DragStarted;
        splitter.DragDelta += Splitter_DragDelta;
        splitter.DragCompleted += Splitter_DragCompleted;
        splitter.DoubleClicked += Splitter_DoubleClicked;

        return splitter;
    }

    /// <summary>
    /// Places an area's toolbar on the section nearest that area's top-right corner: the right-hand
    /// section of a horizontally split area, and the top section of the Side area.
    /// </summary>
    private void PlaceAreaToolbar(DocumentArea area)
    {
        if (!_areaToolbars.TryGetValue(area, out var toolbar))
        {
            return;
        }

        var primarySectionView = _sections[area.GetPrimarySection()];
        var secondarySectionView = _sections[area.GetSecondarySection()];

        bool toolbarOnSecondary = area.SplitsHorizontally() && _areaSplit[area];

        primarySectionView.SetTabStripFooter(toolbarOnSecondary ? null : toolbar);
        secondarySectionView.SetTabStripFooter(toolbarOnSecondary ? toolbar : null);
    }

    // Pushes the area state the tab context menu needs down onto its tabs: whether the area is split, and
    // whether it has room to be.
    private void UpdateSectionMoveTargets(DocumentArea area)
    {
        bool isSplit = _areaSplit[area];
        bool canSplit = CanSplitArea(area);

        foreach (var section in area.GetSections())
        {
            var sectionView = _sections[section];
            sectionView.IsAreaSplit = isSplit;
            sectionView.CanSplitArea = canSplit;
        }
    }

    private void ApplySplitRatio(DocumentArea area)
    {
        var areaGrid = GetAreaGrid(area);
        double ratio = _areaSplitRatio[area];

        if (area.SplitsHorizontally())
        {
            if (areaGrid.ColumnDefinitions.Count == 3)
            {
                areaGrid.ColumnDefinitions[0].Width = new GridLength(ratio, GridUnitType.Star);
                areaGrid.ColumnDefinitions[2].Width = new GridLength(1 - ratio, GridUnitType.Star);
            }
        }
        else
        {
            if (areaGrid.RowDefinitions.Count == 3)
            {
                areaGrid.RowDefinitions[0].Height = new GridLength(ratio, GridUnitType.Star);
                areaGrid.RowDefinitions[2].Height = new GridLength(1 - ratio, GridUnitType.Star);
            }
        }
    }

    private void InitializeAreaSplitters()
    {
        _bottomAreaSplitterHelper = new SplitterHelper(
            RootGrid,
            GridResizeMode.Rows,
            2,
            minSize: MinBottomAreaHeight,
            invertDelta: true,
            maxSizeFunc: () => RootGrid.ActualHeight - MinMainAreaHeight);

        _sideAreaSplitterHelper = new SplitterHelper(
            RootGrid,
            GridResizeMode.Columns,
            2,
            minSize: MinSideAreaWidth,
            invertDelta: true,
            maxSizeFunc: () => RootGrid.ActualWidth - MinMainAreaWidth);

        BottomAreaSplitter.DragStarted += (s, e) => _bottomAreaSplitterHelper?.OnDragStarted();
        BottomAreaSplitter.DragDelta += (s, delta) => _bottomAreaSplitterHelper?.OnDragDelta(delta);
        BottomAreaSplitter.DragCompleted += (s, e) => AreaSizeChanged?.Invoke(DocumentArea.Bottom, BottomAreaRow.ActualHeight);
        BottomAreaSplitter.DoubleClicked += (s, e) => AreaSizeResetRequested?.Invoke(DocumentArea.Bottom);

        SideAreaSplitter.DragStarted += (s, e) => _sideAreaSplitterHelper?.OnDragStarted();
        SideAreaSplitter.DragDelta += (s, delta) => _sideAreaSplitterHelper?.OnDragDelta(delta);
        SideAreaSplitter.DragCompleted += (s, e) => AreaSizeChanged?.Invoke(DocumentArea.Side, SideAreaColumn.ActualWidth);
        SideAreaSplitter.DoubleClicked += (s, e) => AreaSizeResetRequested?.Invoke(DocumentArea.Side);
    }

    // Sizes the root grid for the areas currently presented. Main shares its column with Bottom, so an
    // area only takes a fixed size while it sits alongside another one; the sole presented area takes
    // the whole panel.
    private void ApplyRootGridLayout()
    {
        bool isMainPresented = IsAreaPresented(DocumentArea.Main);
        bool isBottomPresented = IsAreaPresented(DocumentArea.Bottom);
        bool isSidePresented = IsAreaPresented(DocumentArea.Side);
        bool isMainColumnPresented = isMainPresented || isBottomPresented;

        MainAreaGrid.Visibility = isMainPresented ? Visibility.Visible : Visibility.Collapsed;
        BottomAreaGrid.Visibility = isBottomPresented ? Visibility.Visible : Visibility.Collapsed;
        SideAreaGrid.Visibility = isSidePresented ? Visibility.Visible : Visibility.Collapsed;

        // A splitter only earns its place between two presented areas.
        bool showBottomSplitter = isMainPresented && isBottomPresented;
        bool showSideSplitter = isSidePresented && isMainColumnPresented;
        BottomAreaSplitter.Visibility = showBottomSplitter ? Visibility.Visible : Visibility.Collapsed;
        SideAreaSplitter.Visibility = showSideSplitter ? Visibility.Visible : Visibility.Collapsed;

        // Main's row is only zeroed to hand its column over to the Bottom area. The Side area spans all
        // three rows, so zeroing them when it is the only presented area would leave it no height. Main's
        // own minimums stay at zero: they are enforced while dragging, by the splitter helpers.
        bool mainRowTakesRemainingHeight = isMainPresented || !isBottomPresented;
        MainAreaRow.Height = mainRowTakesRemainingHeight
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);

        if (!isBottomPresented)
        {
            BottomAreaRow.Height = new GridLength(0);
        }
        else if (!isMainPresented)
        {
            BottomAreaRow.Height = new GridLength(1, GridUnitType.Star);
        }

        BottomAreaRow.MinHeight = showBottomSplitter ? MinBottomAreaHeight : 0;

        MainAreaColumn.Width = isMainColumnPresented ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        if (!isSidePresented)
        {
            SideAreaColumn.Width = new GridLength(0);
        }
        else if (!isMainColumnPresented)
        {
            SideAreaColumn.Width = new GridLength(1, GridUnitType.Star);
        }

        SideAreaColumn.MinWidth = showSideSplitter ? MinSideAreaWidth : 0;

        ApplySectionChrome();
    }

    // A section is the rectangle a document actually sits in, so the chrome is drawn per section rather than
    // per area: the gutter splitting one area into two divides two such rectangles, exactly as the gutter
    // between two areas does.
    private void ApplySectionChrome()
    {
        foreach (var area in DocumentLayoutHelper.AllAreas)
        {
            ApplyAreaSectionChrome(area);
        }
    }

    // An area draws the edges that face another panel and leaves bare the edges that meet the application
    // border, which is its own boundary. The top edge always faces the title bar gutter.
    private Thickness ResolveAreaEdges(DocumentArea area)
    {
        double facingUtilityPanel = ResolveEdge(_isUtilityPanelPresented);

        if (area == DocumentArea.Side)
        {
            // The Side area's left edge faces the main column, or the Utility Panel when no other area is
            // presented alongside it.
            bool isMainColumnPresented = IsAreaPresented(DocumentArea.Main) || IsAreaPresented(DocumentArea.Bottom);
            double sideLeft = ResolveEdge(isMainColumnPresented || _isUtilityPanelPresented);

            return new Thickness(sideLeft, EdgeThickness, 0, 0);
        }

        double facingSide = ResolveEdge(IsAreaPresented(DocumentArea.Side));

        if (area == DocumentArea.Bottom)
        {
            return new Thickness(facingUtilityPanel, EdgeThickness, facingSide, 0);
        }

        double facingBottom = ResolveEdge(IsAreaPresented(DocumentArea.Bottom));

        return new Thickness(facingUtilityPanel, EdgeThickness, facingSide, facingBottom);
    }

    // Divides an area's outer edges between its sections. An unsplit area has one section that takes them
    // all; a split one gives each section the outer edges on its own side plus an inner edge facing the
    // split gutter. Splitting an area moves that inner edge onto a section that did not have one, so this
    // runs on every rebuild rather than only when the root grid layout changes.
    private void ApplyAreaSectionChrome(DocumentArea area)
    {
        var areaEdges = ResolveAreaEdges(area);

        var primarySectionView = _sections[area.GetPrimarySection()];
        var secondarySectionView = _sections[area.GetSecondarySection()];

        if (!_areaSplit[area])
        {
            // Nothing is internal to an unsplit area, so every edge it draws also shapes its corners.
            ApplySectionEdges(primarySectionView, areaEdges, areaEdges);
            return;
        }

        if (area.SplitsHorizontally())
        {
            var leftEdges = new Thickness(areaEdges.Left, areaEdges.Top, EdgeThickness, areaEdges.Bottom);
            var rightEdges = new Thickness(EdgeThickness, areaEdges.Top, areaEdges.Right, areaEdges.Bottom);

            var leftOuterEdges = new Thickness(areaEdges.Left, areaEdges.Top, 0, areaEdges.Bottom);
            var rightOuterEdges = new Thickness(0, areaEdges.Top, areaEdges.Right, areaEdges.Bottom);

            ApplySectionEdges(primarySectionView, leftEdges, leftOuterEdges);
            ApplySectionEdges(secondarySectionView, rightEdges, rightOuterEdges);

            return;
        }

        var topEdges = new Thickness(areaEdges.Left, areaEdges.Top, areaEdges.Right, EdgeThickness);
        var bottomEdges = new Thickness(areaEdges.Left, EdgeThickness, areaEdges.Right, areaEdges.Bottom);

        var topOuterEdges = new Thickness(areaEdges.Left, areaEdges.Top, areaEdges.Right, 0);
        var bottomOuterEdges = new Thickness(areaEdges.Left, 0, areaEdges.Right, areaEdges.Bottom);

        ApplySectionEdges(primarySectionView, topEdges, topOuterEdges);
        ApplySectionEdges(secondarySectionView, bottomEdges, bottomOuterEdges);
    }

    // A section draws every edge that faces a gutter, but only the edges on the outside of its area shape its
    // corners. The two sections of a split area therefore share one rounded perimeter with a square cut down
    // the middle, which is what marks them as belonging to the same area.
    private void ApplySectionEdges(DocumentSectionView sectionView, Thickness drawnEdges, Thickness outerEdges)
    {
        sectionView.SetGutterChrome(drawnEdges, ResolveCorners(outerEdges));
    }

    private static double ResolveEdge(bool facesNeighbour)
    {
        if (facesNeighbour)
        {
            return EdgeThickness;
        }

        return 0;
    }

    // A corner is rounded where both of the edges meeting there face a gutter outside the area, so a corner
    // sitting on the application border or on an area's internal split stays square. The bottom corners are
    // filled by the document view rather than by the section's own chrome, so they only round on a head that
    // clips a hosted web view to the rounding.
    private CornerRadius ResolveCorners(Thickness edges)
    {
        double radius = (double)Application.Current.Resources["PanelCornerRadius"];

        double bottomRadius = 0;
        if (_platformInfo.ClipsHostedWebViewToCorners)
        {
            bottomRadius = radius;
        }

        double topLeft = ResolveCorner(edges.Left, edges.Top, radius);
        double topRight = ResolveCorner(edges.Top, edges.Right, radius);
        double bottomRight = ResolveCorner(edges.Right, edges.Bottom, bottomRadius);
        double bottomLeft = ResolveCorner(edges.Bottom, edges.Left, bottomRadius);

        return new CornerRadius(topLeft, topRight, bottomRight, bottomLeft);
    }

    private static double ResolveCorner(double firstEdge, double secondEdge, double radius)
    {
        if (firstEdge > 0 &&
            secondEdge > 0)
        {
            return radius;
        }

        return 0;
    }

    private void Splitter_DragStarted(object? sender, EventArgs e)
    {
        if (sender is not Splitter splitter || splitter.Tag is not DocumentArea area)
        {
            return;
        }

        _totalDragDelta = 0;

        if (!_splitHelpers.TryGetValue(area, out var helper))
        {
            var areaGrid = GetAreaGrid(area);
            var mode = area.SplitsHorizontally() ? GridResizeMode.Columns : GridResizeMode.Rows;
            double minSize = area.SplitsHorizontally() ? MinSectionWidth : MinSectionHeight;
            helper = new SplitterHelper(areaGrid, mode, 0, 2, minSize: minSize);
            _splitHelpers[area] = helper;
        }

        helper.OnDragStarted();
    }

    private void Splitter_DragDelta(object? sender, double delta)
    {
        if (sender is not Splitter splitter || splitter.Tag is not DocumentArea area)
        {
            return;
        }

        _totalDragDelta += Math.Abs(delta);

        if (_splitHelpers.TryGetValue(area, out var helper))
        {
            helper.OnDragDelta(delta);
        }
    }

    private void Splitter_DragCompleted(object? sender, EventArgs e)
    {
        if (sender is not Splitter splitter || splitter.Tag is not DocumentArea area)
        {
            return;
        }

        // Skip if no significant drag occurred (e.g., just a click without dragging)
        if (_totalDragDelta < MinDragDistance)
        {
            return;
        }

        double ratio = MeasureSplitRatio(area);
        if (ratio > 0 && ratio < 1)
        {
            _areaSplitRatio[area] = ratio;

            // Convert back to proportional Star sizing so the split holds its share as the area resizes.
            ApplySplitRatio(area);

            AreaLayoutChanged?.Invoke(area, true, ratio);
        }
    }

    private void Splitter_DoubleClicked(object? sender, EventArgs e)
    {
        if (sender is not Splitter splitter || splitter.Tag is not DocumentArea area)
        {
            return;
        }

        _areaSplitRatio[area] = DefaultSplitRatio;
        ApplySplitRatio(area);

        AreaLayoutChanged?.Invoke(area, true, DefaultSplitRatio);
    }

    /// <summary>
    /// The share of the area currently taken by its primary section, measured from the settled grid.
    /// </summary>
    private double MeasureSplitRatio(DocumentArea area)
    {
        var areaGrid = GetAreaGrid(area);

        double primarySize;
        double secondarySize;

        if (area.SplitsHorizontally())
        {
            if (areaGrid.ColumnDefinitions.Count != 3)
            {
                return 0;
            }
            primarySize = areaGrid.ColumnDefinitions[0].ActualWidth;
            secondarySize = areaGrid.ColumnDefinitions[2].ActualWidth;
        }
        else
        {
            if (areaGrid.RowDefinitions.Count != 3)
            {
                return 0;
            }
            primarySize = areaGrid.RowDefinitions[0].ActualHeight;
            secondarySize = areaGrid.RowDefinitions[2].ActualHeight;
        }

        double total = primarySize + secondarySize;
        if (total <= 0)
        {
            return 0;
        }

        return primarySize / total;
    }

    private void OnSectionSelectionChanged(DocumentSectionView sectionView, ResourceKey documentResource)
    {
        // This handles section-level selection (which tab is selected within a section's TabView).
        // This is distinct from the active document, which is updated via ActivateDocument/SetActiveDocument.
        SectionSelectionChanged?.Invoke(sectionView, documentResource);
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
