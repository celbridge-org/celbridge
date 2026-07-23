using Celbridge.Logging;
using Celbridge.Platform;
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
    private readonly IPlatformInfo _platformInfo;
    private readonly PointerEventHandler _tabStripWheelHandler;
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
    /// Event raised when resource files are dropped into this section from the ResourceTree, with the
    /// insertion slot in the tab order the drop point maps to.
    /// </summary>
    public event Action<DocumentSection, List<IResource>, int>? FilesDropped;

    public DocumentSection()
    {
        InitializeComponent();

        _logger = ServiceLocator.AcquireService<IDocumentSectionLogger>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();
        _tabPointerPressedHandler = OnTabPointerPressed;
        _tabStripWheelHandler = OnTabStripPointerWheelChanged;
        DisableBuiltInTabDrag();

        // Disable tab add/remove animations so tabs snap into place immediately
        TabView.Loaded += (s, e) => DisableTabViewAnimations();

        // On macOS the strip does not re-reveal the selected tab when it gets narrower, so a window resize
        // or a change in the number of sections can leave the active tab clipped off-screen. Re-scroll it
        // into view whenever the strip's width changes.
        if (_platformInfo.RequiresMacOSTabScrollIntoView)
        {
            TabView.SizeChanged += OnTabViewSizeChanged;
        }

        // On macOS the overflowing strip does not scroll in response to the mouse wheel, so translate the
        // wheel into horizontal scrolling ourselves. Subscribe for handled events too because the strip's
        // internal ScrollViewer can mark the wheel handled without scrolling.
        if (_platformInfo.RequiresMacOSTabWheelScroll)
        {
            TabView.AddHandler(PointerWheelChangedEvent, _tabStripWheelHandler, handledEventsToo: true);
        }
    }

    private void OnTabViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        if (TabView.SelectedItem is DocumentTab selectedTab)
        {
            ScrollTabIntoView(selectedTab);
        }
    }

    /// <summary>
    /// Throws if the calling code is not on the UI thread. TabView.TabItems has WinUI thread
    /// affinity and reading it off-thread produces a COMException that is hard to diagnose.
    /// Worker-thread readers should go through the cached snapshot on DocumentsService instead.
    /// </summary>
    private void EnsureUIThread()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            throw new InvalidOperationException(
                "DocumentSection must be accessed on the UI thread. " +
                "Worker-thread reads should go through the cached snapshot on DocumentsService.");
        }
    }

    /// <summary>
    /// Disables the add/remove animations on the TabView's internal tab strip.
    /// </summary>
    private void DisableTabViewAnimations()
    {
        // The TabView uses a ListViewBase internally for the tab strip.
        // We need to find it and clear its ChildrenTransitions.
        var listView = VisualTree.FindDescendant<ListViewBase>(TabView);
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
        EnsureUIThread();

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
        EnsureUIThread();

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
        EnsureUIThread();

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
        EnsureUIThread();

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
        AddTabPointerPressedHandler(tab);
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
        RemoveTabPointerPressedHandler(tab);
        TabView.TabItems.Remove(tab);
        DetachStrandedContainer(tab);
        UpdateEmptyPlaceholderVisibility();
    }

    /// <summary>
    /// Works around an Uno Skia TabView bug where TabItems.Remove can leave the removed tab's
    /// container parented to this strip's ItemsStackPanel (seen when it was the selected or last
    /// tab). While that stale parent stands, adding the tab to another section's strip fails to
    /// render its header: the tab is in the model and its content shows, but the header stays blank
    /// until some later reorder rebuilds the panel. Detaching the container here lets the
    /// destination strip take ownership. The packaged Windows head runs the real WinUI TabView and
    /// does not hit this.
    /// </summary>
    private void DetachStrandedContainer(DocumentTab tab)
    {
        var tabListView = VisualTree.FindDescendant<ListViewBase>(TabView);
        var itemsPanel = tabListView?.ItemsPanelRoot;
        if (itemsPanel is not null &&
            itemsPanel.Children.Contains(tab))
        {
            itemsPanel.Children.Remove(tab);
        }
    }

    /// <summary>
    /// Selects a document tab in this section.
    /// </summary>
    public void SelectTab(DocumentTab tab)
    {
        SetSelectedItemWithLayoutRetry(tab, () => ScrollTabIntoView(tab));
    }

    /// <summary>
    /// Assigns TabView.SelectedItem, retrying once on the next dispatcher cycle if Uno throws a layout
    /// exception. On the macOS Skia head, selecting a tab in a strip that has not been measured yet makes
    /// Uno throw a layout exception for the selected tab's corner render (an invalid NaN/Infinity frame
    /// size) while bringing the tab into view, which would otherwise crash the workspace. The common path
    /// stays synchronous so tab selection order during restore is preserved.
    /// </summary>
    private void SetSelectedItemWithLayoutRetry(object? selectedItem, Action onSelected)
    {
        try
        {
            TabView.SelectedItem = selectedItem;
        }
        catch (InvalidOperationException) when (_platformInfo.RequiresMacOSLayoutRetry)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                TabView.SelectedItem = selectedItem;
                onSelected();
            });
            return;
        }

        onSelected();
    }

    /// <summary>
    /// Scrolls the tab strip so the given tab is visible when it lies outside the visible area. macOS only:
    /// the Uno TabView does not bring the selected tab into view on its own, so the strip's scroll offset is
    /// driven directly once its layout has settled.
    /// </summary>
    private void ScrollTabIntoView(DocumentTab tab)
    {
        if (!_platformInfo.RequiresMacOSTabScrollIntoView)
        {
            // The packaged Windows TabView brings the selected tab into view on its own.
            return;
        }

        // Defer to the next dispatcher cycle so the tab strip has completed layout. A tab that was just
        // added, or a selection that changes the strip's extent, has no scroll geometry to act on until
        // the layout pass runs, so measuring synchronously here would read stale bounds.
        DispatcherQueue.TryEnqueue(() =>
        {
            var tabListView = VisualTree.FindDescendant<ListViewBase>(TabView);
            if (tabListView is null)
            {
                return;
            }

            var scrollViewer = VisualTree.FindDescendant<ScrollViewer>(tabListView);
            if (scrollViewer is null)
            {
                return;
            }

            // Realize the container for an off-screen (virtualized) tab and force the strip to lay out, so
            // the measurements below reflect the settled geometry rather than a transient resize state.
            tabListView.ScrollIntoView(tab, ScrollIntoViewAlignment.Default);
            tabListView.UpdateLayout();

            var container = tabListView.ContainerFromItem(tab) as FrameworkElement;
            if (container is null ||
                container.ActualWidth == 0)
            {
                // Uno can realize a tab many positions off-screen without arranging it (zero bounds). Its
                // ScrollIntoView above is the best available fallback; driving the scroll from zero geometry
                // would only push the strip to the wrong place.
                return;
            }

            // Position of the tab relative to the visible viewport. A negative value means the tab is clipped
            // off the leading edge; a right edge past the viewport width means it is clipped off the trailing
            // edge. The minimum scroll that clears the offending edge keeps the rest of the strip stable.
            double tabViewportX = TabViewportLeft(container, scrollViewer);
            double tabWidth = container.ActualWidth;
            double viewportWidth = scrollViewer.ViewportWidth;
            double currentOffset = scrollViewer.HorizontalOffset;

            double? targetOffset = null;
            if (tabViewportX < 0)
            {
                targetOffset = currentOffset + tabViewportX;
            }
            else if (tabViewportX + tabWidth > viewportWidth)
            {
                targetOffset = currentOffset + (tabViewportX + tabWidth - viewportWidth);
            }

            if (targetOffset is null)
            {
                return;
            }

            double clampedOffset = Math.Clamp(targetOffset.Value, 0, scrollViewer.ScrollableWidth);
            scrollViewer.ChangeView(clampedOffset, null, null, disableAnimation: true);
        });
    }

    /// <summary>
    /// The x offset of a tab container relative to the tab strip's scroll viewport.
    /// </summary>
    private static double TabViewportLeft(FrameworkElement container, ScrollViewer scrollViewer)
    {
        var origin = new Windows.Foundation.Point(0, 0);
        return container.TransformToVisual(scrollViewer).TransformPoint(origin).X;
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
    public int TabCount
    {
        get
        {
            EnsureUIThread();
            return TabView.TabItems.Count;
        }
    }

    /// <summary>
    /// Gets the index of the specified tab, or -1 if not found.
    /// </summary>
    public int GetTabIndex(DocumentTab tab)
    {
        EnsureUIThread();
        return TabView.TabItems.IndexOf(tab);
    }

    /// <summary>
    /// Gets all document tabs in this section.
    /// </summary>
    public IEnumerable<DocumentTab> GetAllTabs()
    {
        EnsureUIThread();

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
        SetSelectedItemWithLayoutRetry(selectedItem, () => { });
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
                var tabListView = VisualTree.FindDescendant<ListView>(TabView);
                if (tabListView != null)
                {
                    tabListView.Visibility = showTabStrip ? Visibility.Visible : Visibility.Collapsed;
                }

                var tabStripContainer = VisualTree.FindDescendantByName(TabView, "TabContainerGrid");
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
            RemoveTabPointerPressedHandler(documentTab);

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

        // Removing tabs can leave the strip scrolled past the end of its shrunken content, clipping
        // tabs at the leading edge while showing a blank gap at the trailing edge. Re-clamp once
        // layout has settled.
        _ = DispatcherQueue.TryEnqueue(ClampTabStripScrollOffset);
    }

    private void ClampTabStripScrollOffset()
    {
        var scrollViewer = GetTabStripScrollViewer();
        if (scrollViewer is not null &&
            scrollViewer.HorizontalOffset > scrollViewer.ScrollableWidth)
        {
            scrollViewer.ChangeView(scrollViewer.ScrollableWidth, null, null, disableAnimation: true);
        }
    }

    private ScrollViewer? GetTabStripScrollViewer()
    {
        var tabListView = VisualTree.FindDescendant<ListViewBase>(TabView);

        return tabListView is null ? null : VisualTree.FindDescendant<ScrollViewer>(tabListView);
    }

    /// <summary>
    /// Scrolls the overflowing tab strip horizontally in response to the mouse wheel. macOS only: the Uno
    /// TabView does not translate vertical wheel input into horizontal strip scrolling the way the packaged
    /// Windows TabView does, so the strip's scroll offset is driven directly.
    /// </summary>
    private void OnTabStripPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        var scrollViewer = GetTabStripScrollViewer();
        if (scrollViewer is null ||
            scrollViewer.ScrollableWidth <= 0)
        {
            return;
        }

        // Only the tab strip should react. A wheel over the document content below it falls outside the
        // strip's ScrollViewer, so leave it unhandled to scroll that content.
        var pointerPoint = e.GetCurrentPoint(scrollViewer);
        var position = pointerPoint.Position;
        bool isOverTabStrip = position.X >= 0 &&
            position.X <= scrollViewer.ViewportWidth &&
            position.Y >= 0 &&
            position.Y <= scrollViewer.ActualHeight;
        if (!isOverTabStrip)
        {
            return;
        }

        int wheelDelta = pointerPoint.Properties.MouseWheelDelta;
        if (wheelDelta == 0)
        {
            return;
        }

        // A forward wheel notch (positive delta) reveals earlier tabs, matching the packaged Windows TabView.
        ScrollTabStripBy(-wheelDelta);
        e.Handled = true;
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
        // Keep the TabView visible even when the section has no tabs, so its tab strip footer (which
        // hosts the split-editor toolbar on the rightmost section) stays accessible. The empty
        // placeholder renders behind the empty strip.
        EmptyPlaceholder.Visibility = TabView.TabItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

        if (IsResourceDragInFlight(e))
        {
            // Match the source's requested operation (Move) for compatibility
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = _stringLocalizer.GetString("ResourceTree_Open");
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = false;
            e.Handled = true;
        }
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

        var draggedResources = TakeResourceDragPayload(e);
        if (draggedResources != null)
        {
            int insertionSlot = GetInsertionSlot(e.GetPosition(this).X, this);
            FilesDropped?.Invoke(this, draggedResources, insertionSlot);
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

        if (IsResourceDragInFlight(e))
        {
            // Match the source's requested operation (Move) for compatibility
            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = _stringLocalizer.GetString("ResourceTree_Open");
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

        var draggedResources = TakeResourceDragPayload(e);
        if (draggedResources != null)
        {
            int insertionSlot = GetInsertionSlot(e.GetPosition(this).X, this);
            FilesDropped?.Invoke(this, draggedResources, insertionSlot);
            e.Handled = true;
        }
    }

    // Resource drags from ResourceTree carry their payload in the DataPackage's custom properties, which
    // round-trip on the Windows head. The Skia head recognises these drags through the pointer-driven
    // coordinator instead, so they never reach this built-in drag-and-drop path.
    private static bool IsResourceDragInFlight(DragEventArgs e)
    {
        if (e.Data?.Properties?.ContainsKey("DraggedResources") == true)
        {
            return true;
        }

        return e.DataView?.Properties?.ContainsKey("DraggedResources") == true;
    }

    private static List<IResource>? TakeResourceDragPayload(DragEventArgs e)
    {
        if (e.Data?.Properties?.TryGetValue("DraggedResources", out var draggedObj) == true)
        {
            return draggedObj as List<IResource>;
        }

        if (e.DataView?.Properties?.TryGetValue("DraggedResources", out var draggedViewObj) == true)
        {
            return draggedViewObj as List<IResource>;
        }

        return null;
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
        AddTabPointerPressedHandler(tab);

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
