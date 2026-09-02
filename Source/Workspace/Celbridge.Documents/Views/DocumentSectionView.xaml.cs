using Celbridge.Documents.Helpers;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Platform;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.WebHost;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Foundation.Collections;

namespace Celbridge.Documents.Views;

using IDocumentSectionViewLogger = ILogger<DocumentSectionView>;

/// <summary>
/// A document section containing a TabView for managing document tabs.
/// Multiple sections can be displayed side-by-side in the DocumentSectionContainer.
/// </summary>
public sealed partial class DocumentSectionView : UserControl
{
    private readonly IDocumentSectionViewLogger _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IPlatformInfo _platformInfo;
    private readonly IMessengerService _messengerService;
    private readonly IWebViewFocusRegistry _webViewFocusRegistry;
    private readonly PointerEventHandler _tabStripWheelHandler;
    private readonly PointerEventHandler _documentPointerPressedHandler;
    private bool _isShuttingDown = false;
    private bool _scrollButtonsHidden;
    private bool _scrollIndicatorAttached;
    private bool _tabStripWheelHandlerAttached;
    private readonly WheelGestureAxisTracker _wheelGestureAxisTracker;
    private Storyboard? _perimeterStoryboard;

    // How far below the top of the strip the active document indicator sits, inside the gap the tabs keep
    // above themselves.
    private const double ActiveDocumentIndicatorInset = 1;

    // How far inside its tab each end of the active document indicator stops. The bar floats above the tab
    // rather than touching it, so the eye lines it up against the tab's top edge, which the corner radius
    // draws in on both sides. The trailing end is held further in than the leading one: a tab has its
    // neighbour beside it to be read against, while the last tab in the strip has only the gutter, and the
    // overhang shows there.
    private const double ActiveDocumentIndicatorLeadingInset = 2;
    private const double ActiveDocumentIndicatorTrailingInset = 6;

    // The tab list template's two overflow arrows, named by their containers because that is what carries
    // the width each arrow costs the strip.
    private static readonly string[] TabStripScrollButtonContainerNames =
    {
        "ScrollDecreaseButtonContainer",
        "ScrollIncreaseButtonContainer"
    };

    // The tab strip band from the TabView template, resolved once so composing a minimum does not walk the
    // section's visual tree on every query.
    private FrameworkElement? _tabStripContainer;

    /// <summary>
    /// Static field to track the tab currently being dragged between sections.
    /// This is set when a drag starts and cleared after the drop is handled.
    /// </summary>
    private static DocumentTab? _draggedTab;

    /// <summary>
    /// Static field to track which section the dragged tab came from.
    /// </summary>
    private static DocumentSectionView? _dragSourceSectionView;

    // Localized strings
    private string NoDocumentsOpenString => _stringLocalizer.GetString("DocumentSection_DropFilesPrompt");

    /// <summary>
    /// Identifies which of the six tab strips this section is.
    /// </summary>
    public DocumentSection Section { get; set; }

    /// <summary>
    /// Event raised when the open documents in this section change.
    /// </summary>
    public event Action<DocumentSectionView, List<ResourceKey>>? DocumentsLayoutChanged;

    /// <summary>
    /// Event raised when a tab close is requested.
    /// </summary>
    public event Action<DocumentSectionView, ResourceKey>? CloseRequested;

    /// <summary>
    /// Event raised when a context menu action is requested on a document tab.
    /// </summary>
    public event Action<DocumentSectionView, DocumentTab, DocumentTabMenuAction>? ContextMenuActionRequested;

    /// <summary>
    /// Event raised when a tab from another section is dropped into this section.
    /// </summary>
    public event Action<DocumentSectionView, DocumentTab>? TabDroppedInside;

    /// <summary>
    /// Event raised when resource files are dropped into this section from the ResourceTree, with the
    /// insertion slot in the tab order the drop point maps to.
    /// </summary>
    public event Action<DocumentSectionView, List<IResource>, int>? FilesDropped;

    public DocumentSectionView()
    {
        InitializeComponent();

        _logger = ServiceLocator.AcquireService<IDocumentSectionViewLogger>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _webViewFocusRegistry = ServiceLocator.AcquireService<IWebViewFocusRegistry>();
        _tabPointerPressedHandler = OnTabPointerPressed;
        _tabStripWheelHandler = OnTabStripPointerWheelChanged;
        _wheelGestureAxisTracker = new WheelGestureAxisTracker();
        _documentPointerPressedHandler = OnDocumentPointerPressed;
        DisableBuiltInTabDrag();

        // A press inside a document makes it the active document. Subscribed for handled events too, and on
        // the section root rather than on each document view, because the controls a document is built from
        // mark their own presses handled and a view is adopted into the tab long after this runs.
        RootGrid.AddHandler(PointerPressedEvent, _documentPointerPressedHandler, handledEventsToo: true);

        TabView.Loaded += OnTabViewLoaded;
        TabView.SelectionChanged += OnTabViewSelectionChanged;

        // A narrower strip moves the scroll indicator and can change whether it is needed at all. It also
        // leaves the selected tab clipped off-screen, so a window resize or a change in the number of
        // sections has to re-scroll it into view.
        TabView.SizeChanged += OnTabViewSizeChanged;
    }

    // The web surfaces report a click anywhere over them, while managed focus only moves for a press that
    // lands on a focusable control. Reporting from the pointer gives a document built from managed controls
    // the same region: press its empty space and it becomes the active document, as pressing a control does.
    private void OnDocumentPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var documentView = FocusTracking.FindDocumentView(source);
        if (documentView is null)
        {
            // The tab strip and the empty-section placeholder sit outside every document view. A press on a
            // tab header reports through the tab's own tap handler instead.
            return;
        }

        var message = new DocumentViewFocusedMessage(documentView.FileResource);
        _messengerService.Send(message);

        // A press over a web surface belongs to the page: it takes the keyboard natively and reports that
        // itself, so handing focus to the document as well would take the caret off whatever was clicked.
        if (IsPressOverWebSurface(source))
        {
            return;
        }

        // Focus settles after the press, so whether it reached anything is only known once it has. Deferred
        // rather than resolved here so a press that did reach a control leaves that control focused.
        _ = DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => FocusDocumentIfUnclaimed(documentView));
    }

    // Whether the press landed inside a web surface. Read from where the press landed rather than from which
    // surface holds focus, because a page reports gaining and losing the keyboard over its own message
    // channel, which has not necessarily arrived by the time focus is handed over.
    private bool IsPressOverWebSurface(DependencyObject source)
    {
        foreach (var ancestor in VisualTree.GetAncestors(source, includeSelf: true))
        {
            if (_webViewFocusRegistry.IsRegisteredWebSurface(ancestor))
            {
                return true;
            }
        }

        return false;
    }

    // Hands the keyboard to a document whose press landed on nothing that could take it, so pressing its
    // empty space leaves the document holding focus rather than nothing at all.
    private void FocusDocumentIfUnclaimed(IDocumentView documentView)
    {
        if (_isShuttingDown)
        {
            return;
        }

        // The section left the visual tree between the press and this callback, so there is no focus to read
        // and nothing to give it to.
        var xamlRoot = XamlRoot;
        if (xamlRoot is null)
        {
            return;
        }

        var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
        if (focusedElement is not null
            && ReferenceEquals(FocusTracking.FindDocumentView(focusedElement), documentView))
        {
            return;
        }

        _logger.LogTrace("Giving focus to {Document}, whose press reached nothing focusable", documentView.FileResource);

        documentView.FocusDocument();
    }

    private void OnTabViewLoaded(object sender, RoutedEventArgs e)
    {
        // Disable tab add/remove animations so tabs snap into place immediately
        DisableTabViewAnimations();

        ClearAddTabButtonWidthReservation();

        // The border lines come from the strip's template, and the pair inside the tab list only appears
        // once that list has laid out, so a section that starts empty is covered by applying now and again
        // on the next dispatcher cycle. The scroll arrows live in that same late template.
        UpdateTabStripBorderLines();
        FixTabStripBandHeight();
        HideTabStripScrollButtons();
        AttachTabStripScrollHandlers();

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            UpdateTabStripBorderLines();
            FixTabStripBandHeight();
            HideTabStripScrollButtons();
            AttachTabStripScrollHandlers();
        });
    }

    private void OnTabViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        RevealSelectedTab();

        // The strip is still mid-arrange while its size change is raised, so its leading edge only settles on
        // the following cycle.
        _ = DispatcherQueue.TryEnqueue(UpdateTabStripOverlays);
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
                "DocumentSectionView must be accessed on the UI thread. " +
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
    /// Sets the content to display before the tabs, at the leading edge of the tab strip.
    /// </summary>
    public void SetTabStripHeader(UIElement? content)
    {
        HeaderPresenter.Content = content;
    }

    /// <summary>
    /// Sets the content to display after the tabs, at the trailing edge of the tab strip.
    /// </summary>
    public void SetTabStripFooter(UIElement? content)
    {
        FooterPresenter.Content = content;
    }

    /// <summary>
    /// Sets the edges the section draws against the gutters around it, and the corners where two of those
    /// edges meet.
    /// </summary>
    public void SetGutterChrome(Thickness edges, CornerRadius corners)
    {
        RootGrid.BorderThickness = edges;
        RootGrid.CornerRadius = corners;

        // The placeholder fills the section, so it has to repeat the rounding or it squares the corners off
        // again while the section is empty.
        EmptyPlaceholder.CornerRadius = corners;

        // The flash outline traces the same shape as the chrome, at its own heavier thickness.
        PerimeterOverlay.BorderThickness = AttentionFlash.ResolveOutline(edges);
        PerimeterOverlay.CornerRadius = corners;
    }

    /// <summary>
    /// Briefly pulses an accent outline around the section's perimeter.
    /// </summary>
    public void FlashPerimeter()
    {
        _perimeterStoryboard?.Stop();
        _perimeterStoryboard = AttentionFlash.Play(PerimeterOverlay, AttentionFlash.OutlinePeakOpacity);
    }

    /// <summary>
    /// The smallest size this section can take: the document floor plus the chrome the section draws around
    /// the document it hosts.
    /// </summary>
    public Size MinimumSize => WorkspaceMinimumSize.ComposeSection(MeasureChrome());

    // The chrome a section takes around its document: the tab strip band above it, and an edge on every side.
    // Every edge is allowed for whether or not it is currently drawn, so the minimum does not move as the
    // section's neighbours change.
    private Size MeasureChrome()
    {
        double edges = SectionChromeCalculator.EdgeThickness * 2;

        return new Size(edges, MeasureTabStripHeight() + edges);
    }

    // The band the TabView template lays the tab strip out in, which is the row above the document content, so
    // its height is the whole vertical chrome. It is fixed at the authored height rather than measured, so a
    // section's minimum does not move as its tabs open and close.
    private double MeasureTabStripHeight()
    {
        _tabStripContainer ??= VisualTree.FindDescendantByName(TabView, "TabContainerGrid") as FrameworkElement;

        // Presentation mode collapses the band, so the section really does have no strip above its document.
        if (_tabStripContainer is not null &&
            _tabStripContainer.Visibility == Visibility.Collapsed)
        {
            return 0;
        }

        return WorkspaceConstants.SectionTabStripHeight;
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
    /// Whether this section's area is currently split. Used to determine which move options to show.
    /// </summary>
    public bool IsAreaSplit
    {
        get => _isAreaSplit;
        set
        {
            _isAreaSplit = value;
            foreach (var tabItem in TabView.TabItems)
            {
                if (tabItem is DocumentTab tab)
                {
                    tab.IsAreaSplit = value;
                }
            }
        }
    }

    /// <summary>
    /// Whether this section's area has room for two sections. Used to determine whether the tab context
    /// menu offers to start a split.
    /// </summary>
    public bool CanSplitArea
    {
        get => _canSplitArea;
        set
        {
            _canSplitArea = value;
            foreach (var tabItem in TabView.TabItems)
            {
                if (tabItem is DocumentTab tab)
                {
                    tab.CanSplitArea = value;
                }
            }
        }
    }

    private bool _isAreaSplit = false;
    private bool _canSplitArea = false;

    /// <summary>
    /// Adds a document tab to this section.
    /// </summary>
    public void AddTab(DocumentTab tab)
    {
        tab.Section = Section;
        // Set from cached value - stays in sync via the IsAreaSplit property setter
        tab.IsAreaSplit = IsAreaSplit;
        tab.CanSplitArea = CanSplitArea;
        tab.ContextMenuActionRequested += OnDocumentTabContextMenuAction;
        tab.DragStarted += OnDocumentTabDragStarted;
        AddTabPointerPressedHandler(tab);
        TabView.TabItems.Add(tab);
        UpdateEmptySectionVisuals();
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
        UpdateEmptySectionVisuals();
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
    // UNO-BUG: TabItems.Remove strands the removed tab's container.
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
    /// Restores the invariant that the selected tab is visible. Called after anything that changes the
    /// strip's geometry, and a no-op while the selected tab is already fully in view.
    /// </summary>
    private void RevealSelectedTab()
    {
        if (TabView.SelectedItem is DocumentTab selectedTab)
        {
            ScrollTabIntoView(selectedTab);
        }
    }

    /// <summary>
    /// Scrolls the tab strip so the given tab is visible when it lies outside the visible area. The strip's
    /// scroll offset is driven directly, because neither head reveals a tab that is selected in the same pass
    /// that adds it, and the strip's own overflow arrows are hidden.
    /// </summary>
    private void ScrollTabIntoView(DocumentTab tab)
    {
        // Defer to the next dispatcher cycle so the tab strip has completed layout. A tab that was just
        // added, or a selection that changes the strip's extent, has no scroll geometry to act on until
        // the layout pass runs, so measuring synchronously here would read stale bounds.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isShuttingDown)
            {
                return;
            }

            var tabListView = VisualTree.FindDescendant<ListViewBase>(TabView);
            var scrollViewer = GetTabStripScrollViewer();
            if (tabListView is null ||
                scrollViewer is null)
            {
                return;
            }

            // Settle the strip before measuring it, so the offset and the bounds read below belong to the
            // same arrangement.
            tabListView.UpdateLayout();

            if (tabListView.ContainerFromItem(tab) is FrameworkElement container &&
                container.ActualWidth > 0)
            {
                ScrollToRevealTab(container, scrollViewer);

                return;
            }

            // A virtualized tab has no bounds to measure, and the strip's own reveal is the only thing that
            // will realize it. Where it lands is then corrected on the following cycle, once it has settled.
            tabListView.ScrollIntoView(tab, ScrollIntoViewAlignment.Default);

            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (_isShuttingDown)
                {
                    return;
                }

                if (tabListView.ContainerFromItem(tab) is FrameworkElement realized &&
                    realized.ActualWidth > 0)
                {
                    ScrollToRevealTab(realized, scrollViewer);
                }
            });
        });
    }

    // Scrolls the strip by the least it takes to bring the tab fully into view, and leaves the strip alone
    // while the tab is already there.
    private static void ScrollToRevealTab(FrameworkElement container, ScrollViewer scrollViewer)
    {
        if (GetRevealOffset(container, scrollViewer) is not double targetOffset)
        {
            return;
        }

        double clampedOffset = Math.Clamp(targetOffset, 0, scrollViewer.ScrollableWidth);
        scrollViewer.ChangeView(clampedOffset, null, null, disableAnimation: true);
    }

    /// <summary>
    /// The scroll offset that brings a tab container fully into the strip's viewport, or null when the tab is
    /// already fully visible. A tab whose left edge sits before the viewport is clipped off the leading edge,
    /// and one whose right edge sits past the viewport width is clipped off the trailing edge. Revealing by
    /// the minimum that clears the offending edge keeps the rest of the strip where the user left it.
    /// </summary>
    private static double? GetRevealOffset(FrameworkElement container, ScrollViewer scrollViewer)
    {
        double tabViewportX = TabViewportLeft(container, scrollViewer);
        double tabWidth = container.ActualWidth;
        double viewportWidth = scrollViewer.ViewportWidth;
        double currentOffset = scrollViewer.HorizontalOffset;

        // A tab too wide for the strip cannot be brought fully into view, so its leading edge is the edge to
        // show: the document's name starts there, while the trailing edge carries only the close button. The
        // trailing check below would otherwise stay true however far the strip scrolled, and take the name off
        // the leading edge to satisfy it.
        if (tabWidth >= viewportWidth)
        {
            if (tabViewportX == 0)
            {
                return null;
            }

            return currentOffset + tabViewportX;
        }

        if (tabViewportX < 0)
        {
            return currentOffset + tabViewportX;
        }

        if (tabViewportX + tabWidth > viewportWidth)
        {
            return currentOffset + (tabViewportX + tabWidth - viewportWidth);
        }

        return null;
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

    private void TabView_TabItemsChanged(TabView sender, IVectorChangedEventArgs args)
    {
        if (_isShuttingDown)
        {
            return;
        }

        var documentResources = GetOpenDocuments();
        DocumentsLayoutChanged?.Invoke(this, documentResources);

        ToolTipService.SetToolTip(TabView, null);
        UpdateEmptySectionVisuals();

        // Removing tabs can leave the strip scrolled past the end of its shrunken content, clipping
        // tabs at the leading edge while showing a blank gap at the trailing edge. Re-clamp once
        // layout has settled.
        _ = DispatcherQueue.TryEnqueue(ClampTabStripScrollOffset);

        // Adding or removing a tab changes the width the strip lays its tabs out in, which can leave the
        // selected tab clipped. Enqueued after the clamp so the clamp cannot undo the reveal.
        _ = DispatcherQueue.TryEnqueue(RevealSelectedTab);

        // Opening or closing a tab changes how much there is to scroll, and the indicator is attached late
        // enough that a section's first tabs can arrive before it exists.
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            AttachTabStripScrollHandlers();
            UpdateTabStripOverlays();
        });
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

    /// <summary>
    /// Fixes the tab strip band at its authored height, so what a tab happens to contain cannot set the height
    /// of the strip. Left to itself the band takes the tallest tab, which puts one file type's icon in charge
    /// of the strip's height and of the section minimum composed from it.
    /// </summary>
    private void FixTabStripBandHeight()
    {
        if (VisualTree.FindDescendantByName(TabView, "TabContainerGrid") is not FrameworkElement band)
        {
            return;
        }

        band.Height = WorkspaceConstants.SectionTabStripHeight;
    }

    /// <summary>
    /// Hides the tab strip's overflow scroll arrows and holds them hidden, freeing the 43px each one takes from
    /// a strip that is short of room precisely when they appear. The strip still scrolls by wheel, and the
    /// active tab is revealed on selection.
    /// </summary>
    private void HideTabStripScrollButtons()
    {
        if (_scrollButtonsHidden)
        {
            return;
        }

        var tabListView = VisualTree.FindDescendant<ListViewBase>(TabView);
        if (tabListView is null)
        {
            // The tab list's template has not been applied yet, so try again on the next cycle.
            return;
        }

        foreach (var containerName in TabStripScrollButtonContainerNames)
        {
            if (VisualTree.FindDescendantByName(tabListView, containerName) is not FrameworkElement container)
            {
                continue;
            }

            container.Visibility = Visibility.Collapsed;

            // The strip shows the arrows again every time it starts overflowing, so the collapse has to be
            // held rather than applied once.
            container.RegisterPropertyChangedCallback(VisibilityProperty, HoldScrollButtonCollapsed);
        }

        _scrollButtonsHidden = true;
    }

    // Wires the parts that need the strip's own ScrollViewer, which only exists once the tab list's template
    // has been applied.
    private void AttachTabStripScrollHandlers()
    {
        var scrollViewer = GetTabStripScrollViewer();
        if (scrollViewer is null)
        {
            return;
        }

        AttachTabStripScrollIndicator(scrollViewer);
        AttachTabStripWheelHandler(scrollViewer);

        // Left to itself the strip scrolls a tab into view whenever that tab takes focus, landing wherever the
        // framework chooses rather than at the least scroll that shows the tab, and sometimes short of the
        // edge it scrolled towards. Revealing runs from the selection instead, once the strip has settled.
        scrollViewer.BringIntoViewOnFocusChange = false;
    }

    private void OnTabViewSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        RevealSelectedTab();
    }

    private void AttachTabStripScrollIndicator(ScrollViewer scrollViewer)
    {
        if (_scrollIndicatorAttached)
        {
            return;
        }

        scrollViewer.ViewChanged += OnTabStripViewChanged;
        ScrollIndicator.ScrollRequested += OnScrollIndicatorScrollRequested;
        _scrollIndicatorAttached = true;

        UpdateTabStripOverlays();
    }

    // UNO-BUG: the tab strip's ScrollViewer scrolls horizontal wheel input the wrong way on macOS.
    /// <summary>
    /// Takes over wheel scrolling of the tab strip on macOS. Registered inside the strip's ScrollViewer, on
    /// the presenter the wheel passes through first, so the event is handled before the ScrollViewer's own
    /// wheel handling sees it.
    /// </summary>
    private void AttachTabStripWheelHandler(ScrollViewer scrollViewer)
    {
        if (!_platformInfo.RequiresMacOSTabWheelScroll ||
            _tabStripWheelHandlerAttached)
        {
            return;
        }

        // The presenter fills the strip's viewport, so every wheel event over the strip bubbles through it.
        var scrollPresenter = VisualTree.FindDescendant<ScrollContentPresenter>(scrollViewer);
        if (scrollPresenter is null)
        {
            return;
        }

        scrollPresenter.AddHandler(PointerWheelChangedEvent, _tabStripWheelHandler, handledEventsToo: true);
        _tabStripWheelHandlerAttached = true;
    }

    private void OnTabStripViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        UpdateTabStripOverlays();
    }

    private void OnScrollIndicatorScrollRequested(double offset)
    {
        var scrollViewer = GetTabStripScrollViewer();
        scrollViewer?.ChangeView(offset, null, null, disableAnimation: true);
    }

    // The strip draws two overlays outside the tabs themselves: the scroll indicator along the band's bottom
    // edge, and the active document indicator in the gap above the tabs. Both are placed from the strip's
    // geometry, so both are refreshed whenever that geometry moves.
    private void UpdateTabStripOverlays()
    {
        UpdateTabStripScrollIndicator();
        UpdateActiveDocumentIndicator();
    }

    /// <summary>
    /// Places the active document indicator over the tab holding the active document, and hides it while this
    /// section has no such tab or that tab has scrolled out of the strip.
    /// </summary>
    public void UpdateActiveDocumentIndicator()
    {
        if (_isShuttingDown)
        {
            return;
        }

        DocumentTab? activeTab = null;
        foreach (var tab in GetAllTabs())
        {
            if (tab.IsActiveDocument)
            {
                activeTab = tab;
                break;
            }
        }

        var scrollViewer = GetTabStripScrollViewer();
        if (activeTab is null ||
            activeTab.ActualWidth <= 0 ||
            scrollViewer is null ||
            scrollViewer.ActualWidth <= 0 ||
            MeasureTabStripHeight() <= 0)
        {
            ActiveDocumentIndicator.Visibility = Visibility.Collapsed;

            return;
        }

        var stripBounds = scrollViewer
            .TransformToVisual(RootGrid)
            .TransformBounds(new Rect(0, 0, scrollViewer.ActualWidth, scrollViewer.ActualHeight));

        var tabBounds = activeTab
            .TransformToVisual(RootGrid)
            .TransformBounds(new Rect(0, 0, activeTab.ActualWidth, activeTab.ActualHeight));

        // The strip scrolls under the indicator rather than carrying it, so whatever of the tab has passed
        // either end of the strip comes off the bar instead of being drawn over the chrome beside it. The
        // inset is taken off the tab before that, so a bar the strip has clipped still stops short of its tab.
        double left = Math.Max(tabBounds.Left + ActiveDocumentIndicatorLeadingInset, stripBounds.Left);
        double right = Math.Min(tabBounds.Right - ActiveDocumentIndicatorTrailingInset, stripBounds.Right);
        if (right - left < 1)
        {
            ActiveDocumentIndicator.Visibility = Visibility.Collapsed;

            return;
        }

        // The bar sits nearer the top of the strip than the tab below it, rather than centred in the gap: the
        // section's own edge runs directly above the bar and reads as part of the space over it, so an equal
        // split leaves the top looking the wider of the two. Measured from the top of the strip rather than of
        // the section, which the section's edge holds a pixel below it.
        double indicatorTop = stripBounds.Top + ActiveDocumentIndicatorInset;

        ActiveDocumentIndicator.Visibility = Visibility.Visible;
        ActiveDocumentIndicator.Width = right - left;
        ActiveDocumentIndicator.Margin = new Thickness(left, indicatorTop, 0, 0);

        FocusedDocumentIndicator.Visibility = activeTab.IsFocusedActiveDocument ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Places the scroll indicator inside the tab strip and hands it the strip's geometry.
    /// </summary>
    private void UpdateTabStripScrollIndicator()
    {
        if (_isShuttingDown)
        {
            return;
        }

        // A collapsed band means the section draws no strip, so there is nowhere for the indicator to sit.
        double stripHeight = MeasureTabStripHeight();
        var scrollViewer = GetTabStripScrollViewer();
        if (stripHeight <= 0 ||
            scrollViewer is null ||
            scrollViewer.ActualWidth <= 0)
        {
            ScrollIndicator.Visibility = Visibility.Collapsed;

            return;
        }

        // Where the tabs start across the band depends on the toolbar in the strip header, so the leading
        // edge is the one part of the placement that has to be measured.
        var stripBounds = scrollViewer
            .TransformToVisual(RootGrid)
            .TransformBounds(new Rect(0, 0, scrollViewer.ActualWidth, scrollViewer.ActualHeight));

        // The band is pinned to the authored strip height, so the indicator's place down the band is the same
        // in every section. Measured from the top of the strip rather than of the section, which the section's
        // own edge holds a pixel below it. The pixel the bar keeps below itself clears the document
        // underneath: on macOS that is a native web view drawn over managed content, which clips a bar flush
        // with the band's bottom edge.
        double indicatorTop = stripBounds.Top + stripHeight - ScrollIndicator.Height - 1;

        ScrollIndicator.Width = stripBounds.Width;
        ScrollIndicator.Margin = new Thickness(stripBounds.Left, indicatorTop, 0, 0);

        ScrollIndicator.Update(
            MeasureTabStripContentWidth(scrollViewer),
            scrollViewer.ViewportWidth,
            scrollViewer.HorizontalOffset);
    }

    // The strip's ExtentWidth under-reports the width it actually arranges its tabs in, so a thumb sized from
    // it alone reaches the end of its track before the tabs reach the end of the strip. The trailing arranged
    // tab's right edge is the accurate figure and it is available exactly where it matters, at the end of the
    // strip. Away from there the tabs past the viewport can be virtualized, and the extent is the estimate
    // that accounts for them.
    private double MeasureTabStripContentWidth(ScrollViewer scrollViewer)
    {
        double arrangedRight = 0;
        foreach (var tabItem in TabView.TabItems)
        {
            if (tabItem is not DocumentTab tab ||
                tab.ActualWidth <= 0)
            {
                continue;
            }

            var bounds = tab
                .TransformToVisual(scrollViewer)
                .TransformBounds(new Rect(0, 0, tab.ActualWidth, tab.ActualHeight));

            arrangedRight = Math.Max(arrangedRight, bounds.Right + scrollViewer.HorizontalOffset);
        }

        return Math.Max(scrollViewer.ExtentWidth, arrangedRight);
    }

    private static void HoldScrollButtonCollapsed(DependencyObject sender, DependencyProperty property)
    {
        if (sender is FrameworkElement container &&
            container.Visibility != Visibility.Collapsed)
        {
            container.Visibility = Visibility.Collapsed;
        }
    }

    // UNO-BUG: IsAddTabButtonVisible=False collapses the add button's presenter but leaves the button itself
    // with its declared 32px width, which the strip's width calculation still subtracts from the space it
    // gives the tabs. Observed in 6.6.166; not verified against the packaged Windows head.
    /// <summary>
    /// Clears the width the collapsed add button reserves in the tab strip, so the tabs get the whole strip
    /// rather than stopping 32px short of the trailing edge.
    /// </summary>
    private void ClearAddTabButtonWidthReservation()
    {
        if (TabView.IsAddTabButtonVisible)
        {
            // A button the user can actually reach keeps its width.
            return;
        }

        if (VisualTree.FindDescendantByName(TabView, "AddButton") is not FrameworkElement addButton)
        {
            return;
        }

        // Collapsing the button is what zeroes the reservation: the strip measures what the button wants, and
        // a collapsed element wants nothing.
        addButton.Visibility = Visibility.Collapsed;
    }

    private ScrollViewer? GetTabStripScrollViewer()
    {
        var tabListView = VisualTree.FindDescendant<ListViewBase>(TabView);

        return tabListView is null ? null : VisualTree.FindDescendant<ScrollViewer>(tabListView);
    }

    /// <summary>
    /// Scrolls the overflowing tab strip horizontally in response to the wheel, covering both the vertical
    /// wheel of a mouse and the horizontal scrolling of a trackpad. macOS only: the Uno TabView does not
    /// translate vertical wheel input into horizontal strip scrolling the way the packaged Windows TabView
    /// does, and it scrolls horizontal input backwards, so the strip's scroll offset is driven directly.
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

        var pointerPoint = e.GetCurrentPoint(scrollViewer);
        int wheelDelta = pointerPoint.Properties.MouseWheelDelta;
        if (wheelDelta == 0)
        {
            return;
        }

        bool isHorizontalWheel = pointerPoint.Properties.IsHorizontalMouseWheel;
        bool isOnGestureAxis = _wheelGestureAxisTracker.IsOnGestureAxis(
            pointerPoint.Timestamp,
            isHorizontalWheel,
            wheelDelta);
        if (isOnGestureAxis)
        {
            // A forward wheel notch (positive delta) reveals earlier tabs, matching the packaged Windows
            // TabView. macOS reports a trackpad swipe towards the earlier tabs with that same sign, so one
            // rule serves the horizontal wheel as well as the vertical one.
            ScrollTabStripBy(-wheelDelta);
        }

        // Handled even for an event left off the gesture's axis, so that it stops short of the ScrollViewer,
        // which would otherwise scroll horizontal input the other way and undo this.
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
        _dragSourceSectionView = this;
    }

    private void UpdateEmptySectionVisuals()
    {
        // Keep the TabView visible even when the section has no tabs, so its tab strip footer (which
        // hosts the split-editor toolbar on the rightmost section) stays accessible. The empty
        // placeholder renders behind the empty strip.
        EmptyPlaceholder.Visibility = TabView.TabItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        UpdateTabStripBorderLines();
    }

    /// <summary>
    /// Hides the bottom border of the tab strip while the section has no tabs, so an empty section reads as
    /// one empty rectangle instead of showing a line that divides nothing.
    /// </summary>
    private void UpdateTabStripBorderLines()
    {
        var tabStripContainer = VisualTree.FindDescendantByName(TabView, "TabContainerGrid");
        if (tabStripContainer is null)
        {
            return;
        }

        // The stock TabView spreads this border over four elements: a pair in its own template flanking the
        // tab list, and a 4px pair inside the list's items presenter. Opacity rather than Visibility, which
        // the template drives on these same elements from its own visual states.
        double borderOpacity = 1.0;
        if (TabView.TabItems.Count == 0)
        {
            borderOpacity = 0.0;
        }

        foreach (var borderLine in VisualTree.FindDescendantsByName(tabStripContainer, "LeftBottomBorderLine"))
        {
            borderLine.Opacity = borderOpacity;
        }

        foreach (var borderLine in VisualTree.FindDescendantsByName(tabStripContainer, "RightBottomBorderLine"))
        {
            borderLine.Opacity = borderOpacity;
        }
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
        if (_draggedTab != null && _dragSourceSectionView != null && _dragSourceSectionView != this)
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
        if (_draggedTab != null && _dragSourceSectionView != null && _dragSourceSectionView != this)
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
        if (_draggedTab != null && _dragSourceSectionView != null && _dragSourceSectionView != this)
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
        if (_draggedTab != null && _dragSourceSectionView != null && _dragSourceSectionView != this)
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
        _dragSourceSectionView = null;
    }

    /// <summary>
    /// Inserts a tab at the specified index.
    /// </summary>
    public void InsertTab(DocumentTab tab, int index)
    {
        tab.Section = Section;
        tab.IsAreaSplit = IsAreaSplit;
        tab.CanSplitArea = CanSplitArea;
        tab.ContextMenuActionRequested += OnDocumentTabContextMenuAction;
        tab.DragStarted += OnDocumentTabDragStarted;
        AddTabPointerPressedHandler(tab);

        // An index outside the row appends, which is what DocumentAddress.AppendTabOrder relies on.
        if (index < 0 || index >= TabView.TabItems.Count)
        {
            TabView.TabItems.Add(tab);
        }
        else
        {
            TabView.TabItems.Insert(index, tab);
        }

        UpdateEmptySectionVisuals();
    }
}
