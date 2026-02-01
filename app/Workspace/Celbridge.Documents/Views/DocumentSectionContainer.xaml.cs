using Celbridge.UserInterface.Helpers;
using Celbridge.UserInterface.Views.Controls;

namespace Celbridge.Documents.Views;

/// <summary>
/// Container that manages 1-3 document sections with resizable splitters between them.
/// </summary>
public sealed partial class DocumentSectionContainer : UserControl
{
    private const double MinSectionWidth = 200;

    private readonly List<DocumentSection> _sections = new();
    private readonly List<Splitter> _splitters = new();
    private readonly Dictionary<int, SplitterHelper> _splitterHelpers = new();

    // Track active splitter for drag completed event
    private int _activeSplitterIndex = -1;

    private int _sectionCount = 1;
    private int _activeSectionIndex = 0;
    private ResourceKey _activeDocument = ResourceKey.Empty;

    /// <summary>
    /// Event raised when the selected document changes in any section.
    /// </summary>
    public event Action<DocumentSection, ResourceKey>? SectionSelectionChanged;

    /// <summary>
    /// Event raised when the active document changes.
    /// This is the document that should be inspected and determines where new documents open.
    /// </summary>
    public event Action<ResourceKey>? ActiveDocumentChanged;

    /// <summary>
    /// Event raised when the open documents in any section change.
    /// </summary>
    public event Action<DocumentSection, List<ResourceKey>>? DocumentsLayoutChanged;

    /// <summary>
    /// Event raised when a tab close is requested in any section.
    /// </summary>
    public event Action<DocumentSection, ResourceKey>? CloseRequested;

    /// <summary>
    /// Event raised when a context menu action is requested on a document tab.
    /// </summary>
    public event Action<DocumentSection, DocumentTab, DocumentTabMenuAction>? ContextMenuActionRequested;

    /// <summary>
    /// Event raised when the section count changes.
    /// </summary>
    public event Action<int>? SectionCountChanged;

    /// <summary>
    /// Event raised when section ratios change (after splitter drag).
    /// </summary>
    public event Action<List<double>>? SectionRatiosChanged;

    /// <summary>
    /// Gets the current number of sections.
    /// </summary>
    public int SectionCount => _sectionCount;

    /// <summary>
    /// Gets the active document - the document being inspected and where new documents open.
    /// </summary>
    public ResourceKey ActiveDocument => _activeDocument;

    /// <summary>
    /// Gets the index of the active section (the section containing the active document).
    /// </summary>
    public int ActiveSectionIndex => _activeSectionIndex;

    public DocumentSectionContainer()
    {
        InitializeComponent();

        // Initialize with one section
        CreateSection(0);
        RebuildGrid();
    }

    /// <summary>
    /// Gets all sections.
    /// </summary>
    public IReadOnlyList<DocumentSection> Sections => _sections;

    /// <summary>
    /// Gets the section at the specified index.
    /// </summary>
    public DocumentSection GetSection(int index)
    {
        if (index < 0 || index >= _sections.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        return _sections[index];
    }

    /// <summary>
    /// Sets the number of visible sections (1, 2, or 3).
    /// </summary>
    public void SetSectionCount(int count)
    {
        if (count < 1 || count > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Section count must be 1, 2, or 3");
        }

        if (count == _sectionCount)
        {
            return;
        }

        var oldCount = _sectionCount;
        _sectionCount = count;

        // Create any new sections needed
        while (_sections.Count < count)
        {
            CreateSection(_sections.Count);
        }

        // If reducing sections, migrate documents to the left
        if (count < oldCount)
        {
            MigrateDocumentsLeft(count);
        }

        RebuildGrid();
        SectionCountChanged?.Invoke(count);

        // Fire ratios changed so the new layout is persisted
        // Use dispatcher to ensure layout has completed before capturing ratios
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            SectionRatiosChanged?.Invoke(GetSectionRatios());
        });
    }

    /// <summary>
    /// Sets the footer content for the rightmost visible section's tab strip.
    /// </summary>
    public void SetTabStripFooter(UIElement? content)
    {
        // Clear footer from all sections first
        foreach (var section in _sections)
        {
            section.SetTabStripFooter(null);
        }

        // Set footer on the last visible section
        if (_sectionCount > 0 && _sections.Count >= _sectionCount)
        {
            _sections[_sectionCount - 1].SetTabStripFooter(content);
        }
    }

    /// <summary>
    /// Gets the current proportional widths (ratios) of all visible sections.
    /// </summary>
    public List<double> GetSectionRatios()
    {
        var ratios = new List<double>();
        double totalWidth = 0;

        // Calculate total width of all sections
        for (int i = 0; i < _sectionCount && i < _sections.Count; i++)
        {
            totalWidth += _sections[i].ActualWidth;
        }

        if (totalWidth <= 0)
        {
            // Default to equal ratios if no layout yet
            for (int i = 0; i < _sectionCount; i++)
            {
                ratios.Add(1.0);
            }
            return ratios;
        }

        // Calculate ratio for each section
        for (int i = 0; i < _sectionCount && i < _sections.Count; i++)
        {
            ratios.Add(_sections[i].ActualWidth / totalWidth);
        }

        return ratios;
    }

    /// <summary>
    /// Sets the proportional widths (ratios) of visible sections.
    /// </summary>
    public void SetSectionRatios(List<double> ratios)
    {
        if (ratios == null || ratios.Count == 0)
        {
            return;
        }

        // Apply ratios as Star values to columns
        int ratioIndex = 0;
        for (int i = 0; i < RootGrid.ColumnDefinitions.Count && ratioIndex < ratios.Count; i++)
        {
            var colDef = RootGrid.ColumnDefinitions[i];
            // Skip splitter columns (odd indices)
            if (i % 2 == 0)
            {
                var ratio = Math.Max(ratios[ratioIndex], 0.1); // Minimum ratio to prevent collapse
                colDef.Width = new GridLength(ratio, GridUnitType.Star);
                ratioIndex++;
            }
        }
    }

    /// <summary>
    /// Finds the section containing a specific document.
    /// </summary>
    public DocumentSection? FindSectionContaining(ResourceKey fileResource)
    {
        for (int i = 0; i < _sectionCount && i < _sections.Count; i++)
        {
            if (_sections[i].ContainsDocument(fileResource))
            {
                return _sections[i];
            }
        }
        return null;
    }

    /// <summary>
    /// Handles a tab click message - updates the active document.
    /// This is the primary mechanism for changing the active document.
    /// </summary>
    public void HandleTabClicked(ResourceKey fileResource, int sectionIndex)
    {
        if (fileResource.IsEmpty)
        {
            return;
        }

        // Validate section index
        if (sectionIndex < 0 || sectionIndex >= _sectionCount)
        {
            return;
        }

        // Update the active document directly
        _activeSectionIndex = sectionIndex;
        _activeDocument = fileResource;

        // Update visual indicators across all sections
        UpdateTabSelectionIndicators();

        // Notify listeners of the active document change
        ActiveDocumentChanged?.Invoke(_activeDocument);
    }

    /// <summary>
    /// Called when a document is about to be closed. If it's the active document,
    /// selects the next best document (closest tab in same section, or from other sections).
    /// </summary>
    public void HandleDocumentClosing(ResourceKey closingResource, int closingSectionIndex, int closingTabIndex)
    {
        // Only need to select another document if the closing one is the active document
        if (closingResource != _activeDocument)
        {
            return;
        }

        // Find the next best document to select
        var nextDocument = FindNextDocumentToSelect(closingSectionIndex, closingTabIndex);

        if (nextDocument.HasValue)
        {
            // Select the next document
            _activeSectionIndex = nextDocument.Value.SectionIndex;
            _activeDocument = nextDocument.Value.Resource;

            // Select the tab in its section
            var section = _sections[nextDocument.Value.SectionIndex];
            var tab = section.GetDocumentTab(nextDocument.Value.Resource);
            if (tab != null)
            {
                section.SelectTab(tab);
            }

            UpdateTabSelectionIndicators();
            ActiveDocumentChanged?.Invoke(_activeDocument);
        }
        else
        {
            // No documents left to select
            _activeDocument = ResourceKey.Empty;
            _activeSectionIndex = 0;
            UpdateTabSelectionIndicators();
            ActiveDocumentChanged?.Invoke(_activeDocument);
        }
    }

    /// <summary>
    /// Finds the next best document to select when a document is closed.
    /// Prefers documents in the same section (closest to the closed tab's position),
    /// then falls back to other sections.
    /// </summary>
    private (ResourceKey Resource, int SectionIndex)? FindNextDocumentToSelect(int closingSectionIndex, int closingTabIndex)
    {
        // First, try to find a document in the same section
        if (closingSectionIndex >= 0 && closingSectionIndex < _sectionCount)
        {
            var sameSection = _sections[closingSectionIndex];
            var tabsInSection = sameSection.GetAllTabs().ToList();

            // Account for the tab that's being closed (it's still in the list)
            // After closing, the tab count will be tabsInSection.Count - 1
            int remainingTabs = tabsInSection.Count - 1;

            if (remainingTabs > 0)
            {
                // Find the closest tab to the closing position
                // If there's a tab to the right, select it; otherwise select the one to the left
                int nextIndex = closingTabIndex < remainingTabs
                    ? closingTabIndex + 1 // There's a tab to the right (which will shift into this position)
                    : closingTabIndex - 1; // Select the tab to the left

                if (nextIndex >= 0 && nextIndex < tabsInSection.Count)
                {
                    var nextTab = tabsInSection[nextIndex];
                    // Make sure we don't select the tab that's being closed
                    if (nextTab.ViewModel.FileResource != _activeDocument)
                    {
                        return (nextTab.ViewModel.FileResource, closingSectionIndex);
                    }
                }

                // If the calculated index didn't work, try any other tab in the section
                foreach (var tab in tabsInSection)
                {
                    if (tab.ViewModel.FileResource != _activeDocument)
                    {
                        return (tab.ViewModel.FileResource, closingSectionIndex);
                    }
                }
            }
        }

        // No documents in the same section, try other sections
        // Start from the closest section and work outward
        for (int distance = 1; distance < _sectionCount; distance++)
        {
            // Try section to the right
            int rightIndex = closingSectionIndex + distance;
            if (rightIndex < _sectionCount && rightIndex >= 0)
            {
                var rightSection = _sections[rightIndex];
                var firstTab = rightSection.GetAllTabs().FirstOrDefault();
                if (firstTab != null)
                {
                    return (firstTab.ViewModel.FileResource, rightIndex);
                }
            }

            // Try section to the left
            int leftIndex = closingSectionIndex - distance;
            if (leftIndex >= 0 && leftIndex < _sectionCount)
            {
                var leftSection = _sections[leftIndex];
                var firstTab = leftSection.GetAllTabs().FirstOrDefault();
                if (firstTab != null)
                {
                    return (firstTab.ViewModel.FileResource, leftIndex);
                }
            }
        }

        // No documents found in any section
        return null;
    }

    /// <summary>
    /// Sets the active document.
    /// </summary>
    public void SetActiveDocument(ResourceKey fileResource)
    {
        if (fileResource.IsEmpty)
        {
            _activeDocument = ResourceKey.Empty;
            _activeSectionIndex = 0;
            UpdateTabSelectionIndicators();
            ActiveDocumentChanged?.Invoke(_activeDocument);
            return;
        }

        // Find which section contains this document
        var (section, tab) = FindDocumentTab(fileResource);
        if (section != null && tab != null)
        {
            // Select the tab in its section
            section.SelectTab(tab);

            // Directly update active document (don't rely on events for programmatic selection)
            _activeSectionIndex = section.SectionIndex;
            _activeDocument = fileResource;
            UpdateTabSelectionIndicators();
            ActiveDocumentChanged?.Invoke(_activeDocument);
        }
    }

    /// <summary>
    /// Gets the DocumentTab for a given resource across all sections.
    /// </summary>
    public (DocumentSection? Section, DocumentTab? Tab) FindDocumentTab(ResourceKey fileResource)
    {
        for (int i = 0; i < _sectionCount && i < _sections.Count; i++)
        {
            var tab = _sections[i].GetDocumentTab(fileResource);
            if (tab != null)
            {
                return (_sections[i], tab);
            }
        }
        return (null, null);
    }

    /// <summary>
    /// Updates tab strip visibility across all sections for presenter mode.
    /// </summary>
    public void UpdateTabStripVisibility(bool showTabStrip)
    {
        foreach (var section in _sections)
        {
            section.UpdateTabStripVisibility(showTabStrip);
        }
    }

    /// <summary>
    /// Shuts down all sections.
    /// </summary>
    public void Shutdown()
    {
        foreach (var section in _sections)
        {
            section.Shutdown();
        }
    }

    /// <summary>
    /// Moves a tab from its current section to the target section.
    /// </summary>
    public bool MoveTabToSection(DocumentTab tab, int targetSectionIndex)
    {
        if (targetSectionIndex < 0 || targetSectionIndex >= _sectionCount)
        {
            return false;
        }

        // Find the source section
        var (sourceSection, foundTab) = FindDocumentTab(tab.ViewModel.FileResource);
        if (sourceSection == null || foundTab == null)
        {
            return false;
        }

        var targetSection = _sections[targetSectionIndex];
        if (sourceSection == targetSection)
        {
            return false; // Already in the target section
        }

        // Move the tab
        sourceSection.RemoveTab(tab);
        targetSection.AddTab(tab);
        targetSection.SelectTab(tab);

        // Always make the moved tab the active document
        _activeSectionIndex = targetSectionIndex;
        _activeDocument = tab.ViewModel.FileResource;
        UpdateTabSelectionIndicators();
        ActiveDocumentChanged?.Invoke(_activeDocument);

        return true;
    }

    private void CreateSection(int index)
    {
        var section = new DocumentSection
        {
            SectionIndex = index
        };

        section.SelectionChanged += OnSectionSelectionChanged;
        section.DocumentsLayoutChanged += OnSectionDocumentsLayoutChanged;
        section.CloseRequested += OnSectionCloseRequested;
        section.ContextMenuActionRequested += OnSectionContextMenuActionRequested;
        section.TabDroppedInside += OnSectionTabDroppedInside;

        _sections.Add(section);
    }

    private void MigrateDocumentsLeft(int newSectionCount)
    {
        // Move documents from sections that will be hidden to the rightmost visible section
        var targetSection = _sections[newSectionCount - 1];
        var targetSectionIndex = newSectionCount - 1;

        for (int i = newSectionCount; i < _sections.Count; i++)
        {
            var sourceSection = _sections[i];
            var tabsToMove = sourceSection.GetAllTabs().ToList();

            foreach (var tab in tabsToMove)
            {
                sourceSection.RemoveTab(tab);
                targetSection.AddTab(tab);
            }
        }

        // Update active section if it was in a hidden section
        if (_activeSectionIndex >= newSectionCount)
        {
            _activeSectionIndex = targetSectionIndex;
            UpdateTabSelectionIndicators();
        }
    }

    private void RebuildGrid()
    {
        // Unsubscribe from existing splitter events
        foreach (var splitter in _splitters)
        {
            splitter.DragStarted -= Splitter_DragStarted;
            splitter.DragDelta -= Splitter_DragDelta;
            splitter.DragCompleted -= Splitter_DragCompleted;
        }

        RootGrid.Children.Clear();
        RootGrid.ColumnDefinitions.Clear();
        _splitters.Clear();

        // Update VisibleSectionCount on all sections
        foreach (var section in _sections)
        {
            section.VisibleSectionCount = _sectionCount;
        }

        // Ensure all sections have events wired up (defensive check)
        for (int i = 0; i < _sections.Count; i++)
        {
            var section = _sections[i];

            // Unwire and rewire to ensure no duplicates
            section.SelectionChanged -= OnSectionSelectionChanged;
            section.DocumentsLayoutChanged -= OnSectionDocumentsLayoutChanged;
            section.CloseRequested -= OnSectionCloseRequested;
            section.ContextMenuActionRequested -= OnSectionContextMenuActionRequested;
            section.TabDroppedInside -= OnSectionTabDroppedInside;

            section.SelectionChanged += OnSectionSelectionChanged;
            section.DocumentsLayoutChanged += OnSectionDocumentsLayoutChanged;
            section.CloseRequested += OnSectionCloseRequested;
            section.ContextMenuActionRequested += OnSectionContextMenuActionRequested;
            section.TabDroppedInside += OnSectionTabDroppedInside;
        }

        for (int i = 0; i < _sectionCount; i++)
        {
            // Add section column with Star sizing for proportional layout
            // All sections use Star sizing so they resize proportionally with the window
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
                MinWidth = MinSectionWidth
            });

            var section = _sections[i];
            Grid.SetColumn(section, i * 2);
            RootGrid.Children.Add(section);

            // Add splitter after each section except the last
            if (i < _sectionCount - 1)
            {
                RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var splitter = CreateSplitter(i);
                Grid.SetColumn(splitter, i * 2 + 1);
                RootGrid.Children.Add(splitter);
                _splitters.Add(splitter);
            }
        }
    }

    private Splitter CreateSplitter(int index)
    {
        var splitter = new Splitter
        {
            Orientation = Orientation.Vertical,
            LineThickness = 2,
            GrabAreaSize = 8,
            Tag = index
        };

        splitter.DragStarted += Splitter_DragStarted;
        splitter.DragDelta += Splitter_DragDelta;
        splitter.DragCompleted += Splitter_DragCompleted;

        return splitter;
    }

    private void Splitter_DragStarted(object? sender, EventArgs e)
    {
        if (sender is Splitter splitter && splitter.Tag is int index)
        {
            _activeSplitterIndex = index;
            var leftColumnIndex = _activeSplitterIndex * 2;
            var rightColumnIndex = leftColumnIndex + 2;

            // Create or get the SplitterHelper for this splitter
            if (!_splitterHelpers.TryGetValue(index, out var helper))
            {
                helper = new SplitterHelper(RootGrid, GridResizeMode.Columns, leftColumnIndex, rightColumnIndex, minSize: MinSectionWidth);
                _splitterHelpers[index] = helper;
            }

            helper.OnDragStarted();
        }
    }

    private void Splitter_DragDelta(object? sender, double delta)
    {
        if (_activeSplitterIndex < 0)
        {
            return;
        }

        if (_splitterHelpers.TryGetValue(_activeSplitterIndex, out var helper))
        {
            helper.OnDragDelta(delta);
        }
    }

    private void Splitter_DragCompleted(object? sender, EventArgs e)
    {
        if (_activeSplitterIndex >= 0)
        {
            // Convert pixel widths back to proportional (star) sizing
            // so sections resize properly when the parent container resizes
            var leftColumnIndex = _activeSplitterIndex * 2;
            var rightColumnIndex = leftColumnIndex + 2;

            var leftColumn = RootGrid.ColumnDefinitions[leftColumnIndex];
            var rightColumn = RootGrid.ColumnDefinitions[rightColumnIndex];

            // Get current pixel widths
            var leftWidth = leftColumn.ActualWidth;
            var rightWidth = rightColumn.ActualWidth;
            var totalWidth = leftWidth + rightWidth;

            if (totalWidth > 0)
            {
                // Convert to proportional star values
                var leftRatio = leftWidth / totalWidth;
                var rightRatio = rightWidth / totalWidth;

                leftColumn.Width = new GridLength(leftRatio, GridUnitType.Star);
                rightColumn.Width = new GridLength(rightRatio, GridUnitType.Star);
            }
        }

        _activeSplitterIndex = -1;

        // Notify about ratio changes when splitter drag completes
        SectionRatiosChanged?.Invoke(GetSectionRatios());
    }

    private void OnSectionSelectionChanged(DocumentSection section, ResourceKey documentResource)
    {
        // This handles section-level selection (which tab is selected within a section's TabView).
        // This is distinct from the active document, which is updated via HandleTabClicked/SetActiveDocument.
        // Forward the event for any listeners that need to track section-level selection.
        SectionSelectionChanged?.Invoke(section, documentResource);
    }

    /// <summary>
    /// Updates the visual selection indicators on all tabs across all sections.
    /// </summary>
    private void UpdateTabSelectionIndicators()
    {
        for (int i = 0; i < _sectionCount && i < _sections.Count; i++)
        {
            var section = _sections[i];
            bool isActiveSection = i == _activeSectionIndex;

            foreach (var tab in section.GetAllTabs())
            {
                bool isActiveDocument = isActiveSection &&
                    tab.ViewModel.FileResource == _activeDocument;
                tab.UpdateActiveDocumentState(isActiveDocument);
            }
        }
    }

    private void OnSectionDocumentsLayoutChanged(DocumentSection section, List<ResourceKey> documents)
    {
        DocumentsLayoutChanged?.Invoke(section, documents);
    }

    private void OnSectionCloseRequested(DocumentSection section, ResourceKey fileResource)
    {
        CloseRequested?.Invoke(section, fileResource);
    }

    private void OnSectionContextMenuActionRequested(DocumentSection section, DocumentTab tab, DocumentTabMenuAction action)
    {
        ContextMenuActionRequested?.Invoke(section, tab, action);
    }

    private void OnSectionTabDroppedInside(DocumentSection targetSection, DocumentTab tab)
    {
        // Move the tab to the target section
        if (MoveTabToSection(tab, targetSection.SectionIndex))
        {
            NotifyLayoutChanged();
        }
    }

    private void NotifyLayoutChanged()
    {
        // Re-fire OpenDocumentsChanged for all visible sections to ensure the layout is persisted
        for (int i = 0; i < _sectionCount && i < _sections.Count; i++)
        {
            var section = _sections[i];
            var documents = section.GetOpenDocuments();
            DocumentsLayoutChanged?.Invoke(section, documents);
        }
    }
}
