using Celbridge.Documents.Helpers;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.Views.Controls;
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
    private readonly IMessengerService _messengerService;
    private readonly IWebViewFocusRegistry _webViewFocusRegistry;
    private readonly IManagedFocus _managedFocus;
    private readonly PointerEventHandler _documentPointerPressedHandler;
    private bool _isShuttingDown = false;
    private Storyboard? _perimeterStoryboard;

    // The gap the active document indicator leaves between itself and the tab below it. What the tab's own
    // gap leaves over goes above the bar.
    private const double ActiveDocumentIndicatorGapToTab = 3;

    // How far inside its tab each end of the active document indicator stops. The two differ deliberately:
    // the trailing end needs more to clear the gutter beside the last tab in the strip.
    private const double ActiveDocumentIndicatorLeadingInset = 2;
    private const double ActiveDocumentIndicatorTrailingInset = 6;

    // The stock tab strip's content host, and how far into its template to look for it.
    private const string TabContentPresenterName = "TabContentPresenter";
    private const int TabContentPresenterSearchDepth = 12;

    private ContentPresenter? _tabContentPresenter;

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
    /// Event raised when resource files are dropped into this section from the ResourceTree, with the
    /// insertion slot in the tab order the drop point maps to.
    /// </summary>
    public event Action<DocumentSectionView, List<ResourceItem>, int>? FilesDropped;

    public DocumentSectionView()
    {
        InitializeComponent();

        _logger = ServiceLocator.AcquireService<IDocumentSectionViewLogger>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _webViewFocusRegistry = ServiceLocator.AcquireService<IWebViewFocusRegistry>();
        _managedFocus = ServiceLocator.AcquireService<IManagedFocus>();
        _documentPointerPressedHandler = OnDocumentPointerPressed;

        // A press inside a document makes it the active document. Subscribed for handled events too, and on
        // the section root rather than on each document view, because the controls a document is built from
        // mark their own presses handled and a view is adopted into the tab long after this runs.
        RootGrid.AddHandler(PointerPressedEvent, _documentPointerPressedHandler, handledEventsToo: true);

        AutomationProperties.SetAutomationId(TabView, "document-tab-strip");
        TabView.TabCloseRequested += TabView_CloseRequested;
        TabView.TabItemsChanged += TabView_TabItemsChanged;
        TabView.SizeChanged += OnTabViewContentSizeChanged;

        // A resource dragged over the strip from the Explorer drops into this section, on heads that use the
        // built-in drag-and-drop for resources.
        TabView.AllowDrop = true;
        TabView.DragOver += RootGrid_DragOver;
        TabView.Drop += RootGrid_Drop;

        // The active document indicator sits over the strip, so it moves with the strip's tabs.
        DocumentTabStrip.StripArranged += (_, _) => UpdateActiveDocumentIndicator();
        DocumentTabStrip.TabPressed += OnStripTabPressed;
    }

    private TabView TabView => DocumentTabStrip.TabView;

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

        var focusIsInPressedDocument = focusedElement is not null
            && ReferenceEquals(FocusTracking.FindDocumentView(focusedElement), documentView);

        // Nothing focused leaves the keyboard unclaimed, so it reads as the main content.
        var focusLocation = FocusLocation.MainContent;
        if (focusedElement is UIElement focusedUIElement)
        {
            focusLocation = FocusTracking.GetFocusLocation(focusedUIElement);
        }

        if (!ActiveDocumentFocusPolicy.ShouldFocusPressedDocument(focusIsInPressedDocument, focusLocation))
        {
            return;
        }

        _logger.LogTrace("Giving focus to {Document}, whose press reached nothing focusable", documentView.FileResource);

        documentView.FocusDocument();
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
    /// Sets the content to display before the tabs, at the leading edge of the tab strip.
    /// </summary>
    public void SetTabStripHeader(UIElement? content)
    {
        DocumentTabStrip.SetHeader(content);
    }

    /// <summary>
    /// Sets the content to display after the tabs, at the trailing edge of the tab strip.
    /// </summary>
    public void SetTabStripFooter(UIElement? content)
    {
        DocumentTabStrip.SetFooter(content);
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

        return new Size(edges, DocumentTabStrip.VisibleBandHeight + edges);
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
        TabView.TabItems.Add(tab);
        UpdateEmptySectionVisuals();
    }

    /// <summary>
    /// Removes a document tab from this section. Gives up keyboard focus first if the keyboard is on the tab's header.
    /// </summary>
    public void RemoveTab(DocumentTab tab)
    {
        // WinUI passes the keyboard of a removed element to the next tab stop. That is often the web view of the
        // document in front. The web view reports focus a moment later, and the report makes its document active.
        // By then the caller may have activated another document.
        if (IsKeyboardInTab(tab))
        {
            _logger.LogTrace("Giving up the keyboard before removing the tab of {Document}", tab.ViewModel.FileResource);
            _managedFocus.YieldFocus();
        }

        tab.ContextMenuActionRequested -= OnDocumentTabContextMenuAction;
        DocumentTabStrip.RemoveTab(tab);

        UpdateEmptySectionVisuals();
    }

    // Returns true if the keyboard is on the tab's header or on a control in the header. Right-clicking a tab gives
    // the header the keyboard. On Windows, choosing an item from the tab's menu returns the keyboard to the header. The
    // TabView shows the document's content outside the tab, so the keyboard in the document's content counts as outside
    // the tab.
    private bool IsKeyboardInTab(DocumentTab tab)
    {
        var focusedElement = _managedFocus.FocusedElement;
        if (focusedElement is null)
        {
            return false;
        }

        return VisualTree.GetAncestors(focusedElement, includeSelf: true)
            .Any(ancestor => ReferenceEquals(ancestor, tab));
    }

    /// <summary>
    /// Selects a document tab in this section.
    /// </summary>
    public void SelectTab(DocumentTab tab)
    {
        DocumentTabStrip.SelectTab(tab);
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
        DocumentTabStrip.SelectTab(selectedItem);
    }

    /// <summary>
    /// Updates the tab strip visibility for presenter mode.
    /// </summary>
    public void UpdateTabStripVisibility(bool showTabStrip)
    {
        DocumentTabStrip.SetBandVisible(showTabStrip);
    }

    /// <summary>
    /// Prepares this section for shutdown.
    /// </summary>
    public void Shutdown()
    {
        _isShuttingDown = true;
        DocumentTabStrip.Shutdown();

        foreach (var tabItem in TabView.TabItems)
        {
            if (tabItem is not DocumentTab documentTab)
            {
                continue;
            }

            documentTab.ContextMenuActionRequested -= OnDocumentTabContextMenuAction;

            var documentView = documentTab.Content as IDocumentView;
            if (documentView != null)
            {
                _ = documentView.PrepareToClose();
            }
        }

        TabView.TabItems.Clear();
    }

    /// <summary>
    /// Tells this section's documents the size they are presented at, which is the box the tab strip's
    /// content area gives each of them in turn. A document in a tab that has not been shown is never laid
    /// out, so a view that sizes its own content has nothing else to measure against.
    /// </summary>
    public void UpdatePresentedDocumentSizes()
    {
        var presenter = TabContentPresenter;
        if (presenter is null ||
            presenter.ActualWidth <= 0 ||
            presenter.ActualHeight <= 0)
        {
            return;
        }

        foreach (var tabItem in TabView.TabItems)
        {
            if (tabItem is DocumentTab tab &&
                tab.Content is IDocumentView documentView)
            {
                documentView.SetPresentedSize(presenter.ActualWidth, presenter.ActualHeight);
            }
        }
    }

    // The presenter every document in this section is shown in, whichever tab is selected. It is part of
    // the tab strip's template, so it only exists once that template has been applied.
    private ContentPresenter? TabContentPresenter
    {
        get
        {
            if (_tabContentPresenter is not null)
            {
                return _tabContentPresenter;
            }

            _tabContentPresenter = FindTabContentPresenter(TabView, 0);
            if (_tabContentPresenter is not null)
            {
                _tabContentPresenter.SizeChanged += OnTabViewContentSizeChanged;
            }

            return _tabContentPresenter;
        }
    }

    private static ContentPresenter? FindTabContentPresenter(DependencyObject node, int depth)
    {
        if (depth > TabContentPresenterSearchDepth)
        {
            return null;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is ContentPresenter presenter &&
                presenter.Name == TabContentPresenterName)
            {
                return presenter;
            }

            var found = FindTabContentPresenter(child, depth + 1);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private void OnTabViewContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePresentedDocumentSizes();
    }

    private void TabView_TabItemsChanged(TabView sender, IVectorChangedEventArgs args)
    {
        if (_isShuttingDown)
        {
            return;
        }

        var documentResources = GetOpenDocuments();
        DocumentsLayoutChanged?.Invoke(this, documentResources);

        // A tab that arrives from another section brings a view sized for that section's box.
        UpdatePresentedDocumentSizes();

        ToolTipService.SetToolTip(TabView, null);
        UpdateEmptySectionVisuals();
    }

    /// <summary>
    /// Places the active document indicator over the tab holding the active document, and hides it when this
    /// section has no such tab.
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

        var stripBounds = DocumentTabStrip.GetStripBounds(RootGrid);
        if (activeTab is null
            || activeTab.ActualWidth <= 0
            || stripBounds.IsEmpty
            || DocumentTabStrip.VisibleBandHeight <= 0)
        {
            ActiveDocumentIndicator.Visibility = Visibility.Collapsed;

            return;
        }

        var tabBounds = activeTab
            .TransformToVisual(RootGrid)
            .TransformBounds(new Rect(0, 0, activeTab.ActualWidth, activeTab.ActualHeight));

        // The indicator is drawn outside the strip, so the bar is clipped to the strip's own edges.
        double left = Math.Max(tabBounds.Left + ActiveDocumentIndicatorLeadingInset, stripBounds.Left);
        double right = Math.Min(tabBounds.Right - ActiveDocumentIndicatorTrailingInset, stripBounds.Right);
        if (right - left < 1)
        {
            ActiveDocumentIndicator.Visibility = Visibility.Collapsed;

            return;
        }

        double indicatorTop = stripBounds.Top
            + TabStripView.TabTopGap
            - ActiveDocumentIndicator.Height
            - ActiveDocumentIndicatorGapToTab;

        ActiveDocumentIndicator.Visibility = Visibility.Visible;
        ActiveDocumentIndicator.Width = right - left;
        ActiveDocumentIndicator.Margin = new Thickness(left, indicatorTop, 0, 0);

        FocusedDocumentIndicator.Visibility = activeTab.IsFocusedActiveDocument ? Visibility.Visible : Visibility.Collapsed;
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

    private void UpdateEmptySectionVisuals()
    {
        // Keep the TabView visible even when the section has no tabs, so its tab strip footer (which
        // hosts the split-editor toolbar on the rightmost section) stays accessible. The empty
        // placeholder renders behind the empty strip.
        EmptyPlaceholder.Visibility = TabView.TabItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
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

    private static List<ResourceItem>? TakeResourceDragPayload(DragEventArgs e)
    {
        if (e.Data?.Properties?.TryGetValue("DraggedResources", out var draggedObj) == true)
        {
            return draggedObj as List<ResourceItem>;
        }

        if (e.DataView?.Properties?.TryGetValue("DraggedResources", out var draggedViewObj) == true)
        {
            return draggedViewObj as List<ResourceItem>;
        }

        return null;
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
