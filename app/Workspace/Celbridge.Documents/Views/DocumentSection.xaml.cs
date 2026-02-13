using Celbridge.Logging;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation.Collections;

namespace Celbridge.Documents.Views;

using IDocumentSectionLogger = ILogger<DocumentSection>;

/// <summary>
/// A document section containing a TabView for managing document tabs.
/// Multiple sections can be displayed side-by-side in the DocumentSectionContainer.
/// </summary>
public sealed partial class DocumentSection : UserControl
{
    private readonly IDocumentSectionLogger _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IPanelFocusService _panelFocusService;
    private bool _isShuttingDown = false;

    /// <summary>
    /// Static field to track the tab currently being dragged between sections.
    /// This is set when a drag starts and cleared after the drop is handled.
    /// </summary>
    private static DocumentTab? _draggedTab;

    /// <summary>
    /// Static field to track which section the dragged tab came from.
    /// </summary>
    private static DocumentSection? _dragSourceSection;

    // Localized strings
    private string NoDocumentsOpenString => _stringLocalizer.GetString("DocumentSection_DropFilesPrompt");

    /// <summary>
    /// The section index (0, 1, or 2) identifying this section's position.
    /// </summary>
    public int SectionIndex { get; set; }

    /// <summary>
    /// Event raised when the selected document changes in this section.
    /// </summary>
    public event Action<DocumentSection, ResourceKey>? SelectionChanged;

    /// <summary>
    /// Event raised when the open documents in this section change.
    /// </summary>
    public event Action<DocumentSection, List<ResourceKey>>? DocumentsLayoutChanged;

    /// <summary>
    /// Event raised when a tab close is requested.
    /// </summary>
    public event Action<DocumentSection, ResourceKey>? CloseRequested;

    /// <summary>
    /// Event raised when a context menu action is requested on a document tab.
    /// </summary>
    public event Action<DocumentSection, DocumentTab, DocumentTabMenuAction>? ContextMenuActionRequested;

    /// <summary>
    /// Event raised when a tab from another section is dropped into this section.
    /// </summary>
    public event Action<DocumentSection, DocumentTab>? TabDroppedInside;

    /// <summary>
    /// Event raised when resource files are dropped into this section from the ResourceTree.
    /// </summary>
    public event Action<DocumentSection, List<IResource>>? FilesDropped;

    public DocumentSection()
    {
        InitializeComponent();

        _logger = ServiceLocator.AcquireService<IDocumentSectionLogger>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _panelFocusService = ServiceLocator.AcquireService<IPanelFocusService>();

        // Disable tab add/remove animations so tabs snap into place immediately
        TabView.Loaded += (s, e) => DisableTabViewAnimations();
    }

    private void UserControl_GotFocus(object sender, RoutedEventArgs e)
    {
        _panelFocusService.SetFocusedPanel(FocusablePanel.Documents);
    }

    /// <summary>
    /// Disables the add/remove animations on the TabView's internal tab strip.
    /// </summary>
    private void DisableTabViewAnimations()
    {
        // The TabView uses a ListViewBase internally for the tab strip.
        // We need to find it and clear its ChildrenTransitions.
        var listView = VisualTreeHelperEx.FindDescendant<ListViewBase>(TabView);
        if (listView is not null)
        {
            listView.ItemContainerTransitions = new TransitionCollection();
        }
    }

    /// <summary>
    /// Sets the content to display in the tab strip footer area.
    /// </summary>
    public void SetTabStripFooter(UIElement? content)
    {
        FooterPresenter.Content = content;
    }

    /// <summary>
    /// Gets the list of open documents in this section.
    /// </summary>
    public List<ResourceKey> GetOpenDocuments()
    {
        var openDocuments = new List<ResourceKey>();
        foreach (var tabItem in TabView.TabItems)
        {
            var tab = tabItem as DocumentTab;
            if (tab is null)
            {
                // Log unexpected item type - TabView may contain internal items during drag operations
                _logger.LogWarning($"GetOpenDocuments: Unexpected item type in TabView.TabItems: {tabItem?.GetType().Name ?? "null"}");
                continue;
            }

            var fileResource = tab.ViewModel.FileResource;
            if (openDocuments.Contains(fileResource))
            {
                _logger.LogWarning($"GetOpenDocuments: Duplicate file resource: {fileResource}");
                continue;
            }

            openDocuments.Add(fileResource);
        }

        return openDocuments;
    }

    /// <summary>
    /// Gets the currently selected document in this section, or ResourceKey.Empty if none.
    /// </summary>
    public ResourceKey GetSelectedDocument()
    {
        var documentTab = TabView.SelectedItem as DocumentTab;
        if (documentTab is not null)
        {
            return documentTab.ViewModel.FileResource;
        }
        return ResourceKey.Empty;
    }

    /// <summary>
    /// Checks if a document is open in this section.
    /// </summary>
    public bool ContainsDocument(ResourceKey fileResource)
    {
        foreach (var tabItem in TabView.TabItems)
        {
            if (tabItem is DocumentTab tab && fileResource == tab.ViewModel.FileResource)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Gets the DocumentTab for a given resource, or null if not found.
    /// </summary>
    public DocumentTab? GetDocumentTab(ResourceKey fileResource)
    {
        foreach (var tabItem in TabView.TabItems)
        {
            if (tabItem is DocumentTab tab && fileResource == tab.ViewModel.FileResource)
            {
                return tab;
            }
        }
        return null;
    }

    /// <summary>
    /// Gets or sets the number of currently visible sections. Used to determine which move options to show.
    /// </summary>
    public int VisibleSectionCount
    {
        get => _visibleSectionCount;
        set
        {
            _visibleSectionCount = value;
            // Update all tabs with the new count
            foreach (var tabItem in TabView.TabItems)
            {
                if (tabItem is DocumentTab tab)
                {
                    tab.VisibleSectionCount = value;
                }
            }
        }
    }

    private int _visibleSectionCount = 1;

    /// <summary>
    /// Adds a document tab to this section.
    /// </summary>
    public void AddTab(DocumentTab tab)
    {
        tab.SectionIndex = SectionIndex;
        // Set from cached value - stays in sync via VisibleSectionCount property setter
        tab.VisibleSectionCount = VisibleSectionCount;
        tab.ContextMenuActionRequested += OnDocumentTabContextMenuAction;
        tab.DragStarted += OnDocumentTabDragStarted;
        TabView.TabItems.Add(tab);
        UpdateEmptyPlaceholderVisibility();
    }

    /// <summary>
    /// Removes a document tab from this section.
    /// </summary>
    public void RemoveTab(DocumentTab tab)
    {
        tab.ContextMenuActionRequested -= OnDocumentTabContextMenuAction;
        tab.DragStarted -= OnDocumentTabDragStarted;
        TabView.TabItems.Remove(tab);
        UpdateEmptyPlaceholderVisibility();
    }

    /// <summary>
    /// Selects a document tab in this section.
    /// </summary>
    public void SelectTab(DocumentTab tab)
    {
        TabView.SelectedItem = tab;
    }

    /// <summary>
    /// Clears the selection in this section.
    /// </summary>
    public void ClearSelection()
    {
        TabView.SelectedItem = null;
    }

    /// <summary>
    /// Gets the number of tabs in this section.
    /// </summary>
    public int TabCount => TabView.TabItems.Count;

    /// <summary>
    /// Gets the index of the specified tab, or -1 if not found.
    /// </summary>
    public int GetTabIndex(DocumentTab tab)
    {
        return TabView.TabItems.IndexOf(tab);
    }

    /// <summary>
    /// Gets all document tabs in this section.
    /// </summary>
    public IEnumerable<DocumentTab> GetAllTabs()
    {
        foreach (var tabItem in TabView.TabItems)
        {
            if (tabItem is DocumentTab tab)
            {
                yield return tab;
            }
        }
    }

    /// <summary>
    /// Forces a refresh of the selected tab's content.
    /// </summary>
    public void RefreshSelectedTab()
    {
        var selectedItem = TabView.SelectedItem;
        TabView.SelectedItem = null;
        TabView.SelectedItem = selectedItem;
    }

    /// <summary>
    /// Updates the tab strip visibility for presenter mode.
    /// </summary>
    public void UpdateTabStripVisibility(bool showTabStrip)
    {
        if (TabView.IsLoaded)
        {
            try
            {
                var tabListView = VisualTreeHelperEx.FindDescendant<ListView>(TabView);
                if (tabListView != null)
                {
                    tabListView.Visibility = showTabStrip ? Visibility.Visible : Visibility.Collapsed;
                }

                var tabStripContainer = VisualTreeHelperEx.FindDescendantByName(TabView, "TabContainerGrid");
                if (tabStripContainer is FrameworkElement container)
                {
                    container.Visibility = showTabStrip ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch
            {
                // Silently handle any template traversal errors
            }
        }
    }

    /// <summary>
    /// Prepares this section for shutdown.
    /// </summary>
    public void Shutdown()
    {
        _isShuttingDown = true;

        foreach (var tabItem in TabView.TabItems)
        {
            if (tabItem is not DocumentTab documentTab)
            {
                continue;
            }

            documentTab.ContextMenuActionRequested -= OnDocumentTabContextMenuAction;
            documentTab.DragStarted -= OnDocumentTabDragStarted;

            var documentView = documentTab.Content as IDocumentView;
            if (documentView != null)
            {
                _ = documentView.PrepareToClose();
            }
        }

        TabView.TabItems.Clear();
    }

    private void TabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        ResourceKey documentResource = ResourceKey.Empty;

        var documentTab = TabView.SelectedItem as DocumentTab;
        if (documentTab is not null)
        {
            documentResource = documentTab.ViewModel.FileResource;
        }

        SelectionChanged?.Invoke(this, documentResource);
    }

    private void TabView_TabItemsChanged(TabView sender, IVectorChangedEventArgs args)
    {
        if (_isShuttingDown)
        {
            return;
        }

        var documentResources = GetOpenDocuments();
        DocumentsLayoutChanged?.Invoke(this, documentResources);

        ToolTipService.SetToolTip(TabView, null);
        UpdateEmptyPlaceholderVisibility();
    }

    private void TabView_CloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        var tab = args.Tab as DocumentTab;
        Guard.IsNotNull(tab);

        var fileResource = tab.ViewModel.FileResource;

        CloseRequested?.Invoke(this, fileResource);
    }

    private void OnDocumentTabContextMenuAction(DocumentTab tab, DocumentTabMenuAction action)
    {
        ContextMenuActionRequested?.Invoke(this, tab, action);
    }

    private void OnDocumentTabDragStarted(DocumentTab tab)
    {
        // Set the static drag state when a tab starts being dragged
        _draggedTab = tab;
        _dragSourceSection = this;
    }

    private void UpdateEmptyPlaceholderVisibility()
    {
        EmptyPlaceholder.Visibility = TabView.TabItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TabView.Visibility = TabView.TabItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TabView_TabDroppedOutside(TabView sender, TabViewTabDroppedOutsideEventArgs args)
    {
        if (_isShuttingDown)
        {
            return;
        }

        var tab = args.Tab as DocumentTab;
        if (tab is null)
        {
            return;
        }

        // The tab was dropped outside the TabView but the drag is now complete.
        // Check if another section handled the drop (via DragOver/Drop during the drag).
        // If not, the drag state will still be set and we should clear it.

        // The DragOver/Drop events on other sections fire DURING the drag (before TabDroppedOutside),
        // so if a drop was handled, _draggedTab will already be cleared.

        // If we get here and no drop occurred, just clear the state.
        if (_draggedTab == tab)
        {
            ClearDragState();
        }
    }

    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        // Accept drags from other sections (for dropping on empty sections or anywhere in the section)
        if (_draggedTab != null && _dragSourceSection != null && _dragSourceSection != this)
        {
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.IsCaptionVisible = false;
            e.DragUIOverride.IsGlyphVisible = false;
            e.Handled = true;
            return;
        }

        // Accept file drags from ResourceTree (check both Data and DataView for cross-control drags)
        var hasDataProps = e.Data?.Properties?.ContainsKey("DraggedResources") == true;
        var hasDataViewProps = e.DataView?.Properties?.ContainsKey("DraggedResources") == true;

        if (hasDataProps || hasDataViewProps)
        {
            // Match the source's requested operation (Move) for compatibility
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = "Open";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = false;
            e.Handled = true;
        }
    }

    private void RootGrid_DragLeave(object sender, DragEventArgs e)
    {
        // Intentionally empty - drag leave doesn't need special handling
    }

    private void RootGrid_Drop(object sender, DragEventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        // Handle drop from other sections
        if (_draggedTab != null && _dragSourceSection != null && _dragSourceSection != this)
        {
            var tab = _draggedTab;

            // Clear the drag state
            ClearDragState();

            // Raise event to notify container to move the tab
            TabDroppedInside?.Invoke(this, tab);
            e.Handled = true;
            return;
        }

        // Handle file drop from ResourceTree (check both Data and DataView for cross-control drags)
        List<IResource>? draggedResources = null;
        if (e.Data?.Properties?.TryGetValue("DraggedResources", out var draggedObj) == true)
        {
            draggedResources = draggedObj as List<IResource>;
        }
        else if (e.DataView?.Properties?.TryGetValue("DraggedResources", out var draggedViewObj) == true)
        {
            draggedResources = draggedViewObj as List<IResource>;
        }

        if (draggedResources != null)
        {
            FilesDropped?.Invoke(this, draggedResources);
            e.Handled = true;
        }
    }

    private void TabView_DragOver(object sender, DragEventArgs e)
    {
        // Accept drags from other sections
        if (_draggedTab != null && _dragSourceSection != null && _dragSourceSection != this)
        {
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.IsCaptionVisible = false;
            e.DragUIOverride.IsGlyphVisible = false;
            e.Handled = true;
            return;
        }

        // Accept file drags from ResourceTree (check both Data and DataView for cross-control drags)
        var hasDataProps = e.Data?.Properties?.ContainsKey("DraggedResources") == true;
        var hasDataViewProps = e.DataView?.Properties?.ContainsKey("DraggedResources") == true;

        if (hasDataProps || hasDataViewProps)
        {
            // Match the source's requested operation (Move) for compatibility
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = "Open";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = false;
            e.Handled = true;
        }
    }

    private void TabView_Drop(object sender, DragEventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        // Handle drop from other sections
        if (_draggedTab != null && _dragSourceSection != null && _dragSourceSection != this)
        {
            var tab = _draggedTab;

            // Clear the drag state
            ClearDragState();

            // Raise event to notify container to move the tab
            TabDroppedInside?.Invoke(this, tab);
            e.Handled = true;
            return;
        }

        // Handle file drop from ResourceTree (check both Data and DataView for cross-control drags)
        List<IResource>? draggedResources = null;
        if (e.Data?.Properties?.TryGetValue("DraggedResources", out var draggedObj) == true)
        {
            draggedResources = draggedObj as List<IResource>;
        }
        else if (e.DataView?.Properties?.TryGetValue("DraggedResources", out var draggedViewObj) == true)
        {
            draggedResources = draggedViewObj as List<IResource>;
        }

        if (draggedResources != null)
        {
            FilesDropped?.Invoke(this, draggedResources);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Clears any pending drag state. Should be called after a drag operation completes.
    /// </summary>
    public static void ClearDragState()
    {
        _draggedTab = null;
        _dragSourceSection = null;
    }

    /// <summary>
    /// Inserts a tab at the specified index.
    /// </summary>
    public void InsertTab(DocumentTab tab, int index)
    {
        tab.SectionIndex = SectionIndex;
        tab.VisibleSectionCount = VisibleSectionCount;
        tab.ContextMenuActionRequested += OnDocumentTabContextMenuAction;
        tab.DragStarted += OnDocumentTabDragStarted;

        if (index < 0 || index >= TabView.TabItems.Count)
        {
            TabView.TabItems.Add(tab);
        }
        else
        {
            TabView.TabItems.Insert(index, tab);
        }

        UpdateEmptyPlaceholderVisibility();
    }
}
