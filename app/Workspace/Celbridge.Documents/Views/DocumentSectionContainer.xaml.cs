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

    // Track drag state for resizing
    private double _leftColumnStartWidth;
    private double _rightColumnStartWidth;
    private int _activeSplitterIndex = -1;

    private int _sectionCount = 1;

    /// <summary>
    /// Event raised when the selected document changes in any section.
    /// </summary>
    public event Action<DocumentSection, ResourceKey>? SelectionChanged;

    /// <summary>
    /// Event raised when the open documents in any section change.
    /// </summary>
    public event Action<DocumentSection, List<ResourceKey>>? OpenDocumentsChanged;

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

        return true;
    }

    private void CreateSection(int index)
    {
        var section = new DocumentSection
        {
            SectionIndex = index
        };

        section.SelectionChanged += OnSectionSelectionChanged;
        section.OpenDocumentsChanged += OnSectionOpenDocumentsChanged;
        section.CloseRequested += OnSectionCloseRequested;
        section.ContextMenuActionRequested += OnSectionContextMenuActionRequested;

        _sections.Add(section);
    }

    private void MigrateDocumentsLeft(int newSectionCount)
    {
        // Move documents from sections that will be hidden to the rightmost visible section
        var targetSection = _sections[newSectionCount - 1];

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

            _leftColumnStartWidth = RootGrid.ColumnDefinitions[leftColumnIndex].ActualWidth;
            _rightColumnStartWidth = RootGrid.ColumnDefinitions[rightColumnIndex].ActualWidth;
        }
    }

    private void Splitter_DragDelta(object? sender, double delta)
    {
        if (_activeSplitterIndex < 0)
        {
            return;
        }

        var leftColumnIndex = _activeSplitterIndex * 2;
        var rightColumnIndex = leftColumnIndex + 2;


        var newLeftWidth = _leftColumnStartWidth + delta;
        var newRightWidth = _rightColumnStartWidth - delta;

        // Enforce minimum widths
        if (newLeftWidth >= MinSectionWidth && newRightWidth >= MinSectionWidth)
        {
            RootGrid.ColumnDefinitions[leftColumnIndex].Width = new GridLength(newLeftWidth, GridUnitType.Pixel);
            RootGrid.ColumnDefinitions[rightColumnIndex].Width = new GridLength(newRightWidth, GridUnitType.Pixel);
        }
    }

    private void Splitter_DragCompleted(object? sender, EventArgs e)
    {
        _activeSplitterIndex = -1;

        // Notify about ratio changes when splitter drag completes
        SectionRatiosChanged?.Invoke(GetSectionRatios());
    }

    private void OnSectionSelectionChanged(DocumentSection section, ResourceKey documentResource)
    {
        SelectionChanged?.Invoke(section, documentResource);
    }

    private void OnSectionOpenDocumentsChanged(DocumentSection section, List<ResourceKey> documents)
    {
        OpenDocumentsChanged?.Invoke(section, documents);
    }

    private void OnSectionCloseRequested(DocumentSection section, ResourceKey fileResource)
    {
        CloseRequested?.Invoke(section, fileResource);
    }

    private void OnSectionContextMenuActionRequested(DocumentSection section, DocumentTab tab, DocumentTabMenuAction action)
    {
        ContextMenuActionRequested?.Invoke(section, tab, action);
    }
}
