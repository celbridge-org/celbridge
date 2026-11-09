using Celbridge.Platform;
using Celbridge.UserInterface.DragDrop;
using Celbridge.Workspace;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.Foundation.Collections;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// A tab strip built on a TabView, with a band of fixed height and a scroll indicator. The strip scrolls with the wheel,
/// scrolls the selected tab into view, and reports each press on a tab for a drag to start from. The owner adds the
/// tabs, and decides what a tab shows and where a dragged tab can land.
/// </summary>
public sealed partial class TabStripView : UserControl
{
    /// <summary>
    /// The gap each tab keeps between itself and the top of the band.
    /// </summary>
    public const double TabTopGap = 7;

    /// <summary>
    /// The height of each tab: the band less the gap above the tab.
    /// </summary>
    public const double TabHeight = WorkspaceConstants.SectionTabStripHeight - TabTopGap;

    // A scroll shorter than this moves the strip by less than a pixel.
    private const double RevealAlignmentTolerance = 0.5;

    // The names of the containers around the tab list template's two overflow arrows. The strip hides each
    // container, because the container has the arrow's width.
    private static readonly string[] ScrollButtonContainerNames =
    {
        "ScrollDecreaseButtonContainer",
        "ScrollIncreaseButtonContainer"
    };

    private readonly IPlatformInfo _platformInfo;
    private readonly PointerEventHandler _wheelHandler;
    private readonly PointerEventHandler _tabPressedHandler;
    private readonly WheelGestureAxisTracker _wheelGestureAxisTracker = new();

    private bool _isShutDown;
    private bool _scrollButtonsHidden;
    private bool _scrollIndicatorAttached;
    private bool _wheelHandlerAttached;

    // The band from the TabView template, resolved once so a size query does not walk the visual tree each time.
    private FrameworkElement? _band;

    // The tab this strip last selected. Reloading the tab list can clear the TabView's own selection, and this field
    // survives the reload.
    private object? _selectedTab;

    /// <summary>
    /// Raised when the pointer is pressed on a tab, including a press the tab handles itself. A drag starts from
    /// this press.
    /// </summary>
    public event Action<TabViewItem, PointerRoutedEventArgs>? TabPressed;

    /// <summary>
    /// Raised after the strip has placed the scroll indicator. An owner places its overlays from the same geometry.
    /// </summary>
    public event EventHandler? StripArranged;

    public TabStripView()
    {
        InitializeComponent();

        _platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();
        _wheelHandler = OnStripPointerWheelChanged;
        _tabPressedHandler = OnTabViewPointerPressed;

        StripTabView.Loaded += OnTabViewLoaded;
        StripTabView.SelectionChanged += OnTabViewSelectionChanged;
        StripTabView.TabItemsChanged += OnTabItemsChanged;

        // A narrower strip moves the scroll indicator, and can show or hide the indicator. A narrower strip can also
        // clip the selected tab, so a window resize scrolls the selected tab into view again.
        StripTabView.SizeChanged += OnTabViewSizeChanged;

        // The toolbars in these slots share the band with the tab list. Their width decides where the tabs start,
        // and the overlays drawn over the tabs move with the tabs.
        HeaderPresenter.SizeChanged += OnPresenterSizeChanged;
        FooterPresenter.SizeChanged += OnPresenterSizeChanged;

        // A tab marks its own press handled, so the strip listens for handled presses too.
        StripTabView.AddHandler(PointerPressedEvent, _tabPressedHandler, handledEventsToo: true);
    }

    /// <summary>
    /// The TabView the strip is built on. The owner adds tabs to the TabView's TabItems and listens to the TabView's
    /// events.
    /// </summary>
    public TabView TabView => StripTabView;

    /// <summary>
    /// The height the band takes: the strip's fixed height, or zero while the band is hidden.
    /// </summary>
    public double VisibleBandHeight
    {
        get
        {
            _band ??= VisualTree.FindDescendantByName(StripTabView, "TabContainerGrid") as FrameworkElement;
            if (_band is not null
                && _band.Visibility == Visibility.Collapsed)
            {
                return 0;
            }

            return WorkspaceConstants.SectionTabStripHeight;
        }
    }

    /// <summary>
    /// Sets the content to show before the tabs, at the leading edge of the band.
    /// </summary>
    public void SetHeader(UIElement? content)
    {
        HeaderPresenter.Content = content;
    }

    /// <summary>
    /// Sets the content to show after the tabs, at the trailing edge of the band.
    /// </summary>
    public void SetFooter(UIElement? content)
    {
        FooterPresenter.Content = content;
    }

    /// <summary>
    /// Shows or hides the band. The TabView's content area stays visible.
    /// </summary>
    public void SetBandVisible(bool isVisible)
    {
        if (!StripTabView.IsLoaded)
        {
            return;
        }

        var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

        if (VisualTree.FindDescendant<ListView>(StripTabView) is ListView tabList)
        {
            tabList.Visibility = visibility;
        }

        if (VisualTree.FindDescendantByName(StripTabView, "TabContainerGrid") is FrameworkElement band)
        {
            band.Visibility = visibility;
        }
    }

    /// <summary>
    /// Selects a tab and scrolls it into view.
    /// </summary>
    public void SelectTab(object? tab)
    {
        SetSelectedItemWithLayoutRetry(tab, () =>
        {
            if (tab is not null)
            {
                ScrollTabIntoView(tab);
            }
        });
    }

    /// <summary>
    /// Removes a tab from the strip.
    /// </summary>
    public void RemoveTab(object tab)
    {
        StripTabView.TabItems.Remove(tab);
        DetachStrandedContainer(tab);

        // A removed tab is never restored, and holding it would keep the tab's content alive.
        if (ReferenceEquals(_selectedTab, tab))
        {
            _selectedTab = null;
        }
    }

    /// <summary>
    /// Moves a tab to a new index in the strip, keeping the tab selected when it was. Returns false when the tab is
    /// not in the strip or is already at that index.
    /// </summary>
    public bool MoveTab(object tab, int newIndex)
    {
        var tabItems = StripTabView.TabItems;

        int currentIndex = tabItems.IndexOf(tab);
        if (currentIndex < 0)
        {
            return false;
        }

        int targetIndex = Math.Clamp(newIndex, 0, tabItems.Count - 1);
        if (targetIndex == currentIndex)
        {
            return false;
        }

        bool wasSelected = ReferenceEquals(StripTabView.SelectedItem, tab);
        tabItems.RemoveAt(currentIndex);
        DetachStrandedContainer(tab);
        tabItems.Insert(targetIndex, tab);

        if (wasSelected)
        {
            SelectTab(tab);
        }

        return true;
    }

    /// <summary>
    /// Scrolls the selected tab into view. Does nothing while the selected tab is fully in view already.
    /// </summary>
    public void RevealSelectedTab()
    {
        if (StripTabView.SelectedItem is { } selectedTab)
        {
            ScrollTabIntoView(selectedTab);
        }
    }

    /// <summary>
    /// Scrolls the strip horizontally by the given distance in pixels, clamped to the scrollable range.
    /// </summary>
    public void ScrollBy(double delta)
    {
        var scrollViewer = GetScrollViewer();
        if (scrollViewer is null)
        {
            return;
        }

        double targetOffset = Math.Clamp(scrollViewer.HorizontalOffset + delta, 0, scrollViewer.ScrollableWidth);
        scrollViewer.ChangeView(targetOffset, null, null, disableAnimation: true);
    }

    /// <summary>
    /// Gets the bounds of the tab list relative to the given element, or Rect.Empty when the strip is collapsed or
    /// not yet laid out.
    /// </summary>
    public Rect GetStripBounds(UIElement relativeTo)
    {
        var tabList = VisualTree.FindDescendant<ListViewBase>(StripTabView);
        if (tabList is null
            || tabList.ActualWidth <= 0
            || Visibility == Visibility.Collapsed)
        {
            return Rect.Empty;
        }

        var transform = tabList.TransformToVisual(relativeTo);

        return transform.TransformBounds(new Rect(0, 0, tabList.ActualWidth, tabList.ActualHeight));
    }

    /// <summary>
    /// Gets the bounds of each tab relative to the given element, in tab order. A tab not laid out has empty bounds. A
    /// virtualizing strip lays out only the tabs in view.
    /// </summary>
    public List<Rect> GetTabBounds(UIElement relativeTo)
    {
        var tabBounds = new List<Rect>();
        foreach (var tabItem in StripTabView.TabItems)
        {
            if (tabItem is not FrameworkElement tab
                || tab.ActualWidth <= 0)
            {
                tabBounds.Add(Rect.Empty);
                continue;
            }

            var transform = tab.TransformToVisual(relativeTo);
            tabBounds.Add(transform.TransformBounds(new Rect(0, 0, tab.ActualWidth, tab.ActualHeight)));
        }

        return tabBounds;
    }

    /// <summary>
    /// Gets the insertion slot, from 0 to the tab count, for a pointer at the given X. The X is relative to the given
    /// element.
    /// </summary>
    public int GetInsertionSlot(double pointerX, UIElement relativeTo)
    {
        return TabDragGesture.GetInsertionSlot(pointerX, GetTabBounds(relativeTo));
    }

    /// <summary>
    /// Stops the strip's deferred work. Call when the owner shuts down.
    /// </summary>
    public void Shutdown()
    {
        _isShutDown = true;
    }

    private void OnTabViewLoaded(object sender, RoutedEventArgs e)
    {
        // Tabs snap into place when added or removed.
        DisableTabAnimations();

        ClearAddTabButtonWidthReservation();

        // The border lines come from the strip's template, and the pair inside the tab list only appears once that
        // list has laid out. The parts are applied now and once more on the next dispatcher cycle, so a strip that
        // starts empty gets them too. The scroll arrows and the tab list's items presenter live in that same late
        // template.
        ApplyTemplateParts();

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            ApplyTemplateParts();
            RestoreSelectionDroppedByReload();
        });
    }

    private void ApplyTemplateParts()
    {
        DisableTabVirtualization();
        UpdateBorderLines();
        FixBandHeight();
        AlignTabListToBandTop();
        HideScrollButtons();
        AttachScrollHandlers();
    }

    // UNO-BUG: each time the tab list loads, the TabView empties the list and adds the tabs back, which clears the
    // selection. The virtualizing panel happens to restore the selection, and the non-virtualizing panel leaves it
    // cleared. A strip shown again then has every tab unselected and shows blank content. Observed in 6.6.166.
    /// <summary>
    /// Selects the strip's last selected tab again when reloading the tab list has cleared the selection.
    /// </summary>
    private void RestoreSelectionDroppedByReload()
    {
        if (!_platformInfo.RequiresNonVirtualizingTabStrip
            || StripTabView.SelectedItem is not null
            || _selectedTab is null
            || !StripTabView.TabItems.Contains(_selectedTab))
        {
            return;
        }

        SelectTab(_selectedTab);
    }

    private void OnTabViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isShutDown)
        {
            return;
        }

        RevealSelectedTab();

        // The strip is still mid-arrange while the size change is raised, so the strip's leading edge only settles on
        // the following cycle.
        _ = DispatcherQueue.TryEnqueue(UpdateOverlays);
    }

    private void OnTabItemsChanged(TabView sender, IVectorChangedEventArgs args)
    {
        if (_isShutDown)
        {
            return;
        }

        UpdateBorderLines();

        // Removing tabs can leave the strip scrolled past the end of the shrunken content. Tabs are then clipped at
        // the leading edge, with a blank gap at the trailing edge. Clamp again once layout has settled.
        _ = DispatcherQueue.TryEnqueue(ClampScrollOffset);

        // Adding or removing a tab changes the width the strip lays its tabs out in, which can leave the selected tab
        // clipped. Enqueued after the clamp, so the clamp cannot undo the reveal.
        _ = DispatcherQueue.TryEnqueue(RevealSelectedTab);

        // Opening or closing a tab changes how much there is to scroll. The indicator is attached late, and a strip's
        // first tabs can arrive before the indicator exists. A strip created with its first tab also loads before the
        // tab list exists.
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            DisableTabVirtualization();
            AlignTabListToBandTop();
            HideScrollButtons();
            AttachScrollHandlers();
            UpdateOverlays();
        });
    }

    private void OnTabViewSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isShutDown)
        {
            return;
        }

        if (StripTabView.SelectedItem is { } selectedTab)
        {
            _selectedTab = selectedTab;
        }

        RevealSelectedTab();
    }

    private void OnPresenterSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isShutDown)
        {
            return;
        }

        // The strip is still mid-arrange while the size change is raised, so the tabs only settle on the following
        // cycle.
        _ = DispatcherQueue.TryEnqueue(UpdateOverlays);
    }

    private void OnTabViewPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_isShutDown
            || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        // A tab's content shows in the TabView's content area, outside the tab. Only a press on the tab itself finds
        // the tab among the press's ancestors.
        foreach (var ancestor in VisualTree.GetAncestors(source, includeSelf: true))
        {
            if (ReferenceEquals(ancestor, StripTabView))
            {
                return;
            }

            if (ancestor is TabViewItem tab
                && StripTabView.TabItems.Contains(tab))
            {
                TabPressed?.Invoke(tab, e);
                return;
            }
        }
    }

    private void DisableTabAnimations()
    {
        // The TabView lays the tabs out in a ListViewBase. The list's item transitions animate an add or a removal.
        var tabList = VisualTree.FindDescendant<ListViewBase>(StripTabView);
        if (tabList is not null)
        {
            tabList.ItemContainerTransitions = new TransitionCollection();
        }
    }

    // UNO-BUG: the tab list's virtualizing panel estimates the width of tabs not yet laid out from the average width
    // of the tabs laid out. The strip then cannot scroll a far tab into view, or the last tab fully into view.
    // Observed in 6.6.166.
    /// <summary>
    /// Swaps the tab list's virtualizing panel for a panel that lays out every tab. The strip's extent is then the
    /// true width of the tabs, and every tab has bounds to reveal.
    /// </summary>
    private void DisableTabVirtualization()
    {
        if (!_platformInfo.RequiresNonVirtualizingTabStrip)
        {
            return;
        }

        var tabList = VisualTree.FindDescendant<ListViewBase>(StripTabView);
        if (tabList is null)
        {
            return;
        }

        tabList.ItemsPanel = (ItemsPanelTemplate)Resources["NonVirtualizingTabStripPanel"];

        ReserveTabListHeaderAndFooterWidth();
    }

    // UNO-BUG: over a StackPanel, the items presenter leaves the header and footer out of the width the presenter
    // asks for. The presenter still lays the header and footer out either side of the tabs. The strip's extent then
    // stops short by that width and clips the last tab. Observed in 6.6.166.
    /// <summary>
    /// Pads the tab list's items presenter by the width of the presenter's header and footer. The width the presenter
    /// asks for then covers everything the presenter lays out. The header and footer contain the ends of the strip's
    /// bottom border.
    /// </summary>
    private void ReserveTabListHeaderAndFooterWidth()
    {
        if (GetScrollViewer()?.Content is not ItemsPresenter itemsPresenter)
        {
            // The tab list's template has not been applied yet, so try again on the next cycle.
            return;
        }

        double reservedWidth = MeasureWidth(itemsPresenter.Header) + MeasureWidth(itemsPresenter.Footer);

        itemsPresenter.Padding = new Thickness(0, 0, reservedWidth, 0);
    }

    // Measured here, so the width is known before the strip has laid out.
    private static double MeasureWidth(object? content)
    {
        if (content is not UIElement element)
        {
            return 0;
        }

        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        return element.DesiredSize.Width;
    }

    // UNO-BUG: TabItems.Remove strands the removed tab's container.
    /// <summary>
    /// Detaches a removed tab's container from this strip's items panel. On the Skia head, TabItems.Remove can leave
    /// the container parented to the panel, seen when it was the selected or last tab. Adding the tab to another
    /// strip then leaves its header blank until some later reorder rebuilds the panel.
    /// </summary>
    private void DetachStrandedContainer(object tab)
    {
        var tabList = VisualTree.FindDescendant<ListViewBase>(StripTabView);
        var itemsPanel = tabList?.ItemsPanelRoot;
        if (tab is UIElement tabElement
            && itemsPanel is not null
            && itemsPanel.Children.Contains(tabElement))
        {
            itemsPanel.Children.Remove(tabElement);
        }
    }

    /// <summary>
    /// Assigns TabView.SelectedItem, retrying once on the next dispatcher cycle if Uno throws a layout exception. On
    /// the macOS Skia head, selecting a tab in a strip not yet measured makes Uno throw while bringing the tab into
    /// view. The throw comes from the selected tab's corner render, an invalid NaN or Infinity frame size. The common
    /// path stays synchronous, so tab selection order during a restore stays the same.
    /// </summary>
    private void SetSelectedItemWithLayoutRetry(object? selectedItem, Action onSelected)
    {
        try
        {
            StripTabView.SelectedItem = selectedItem;
        }
        catch (InvalidOperationException) when (_platformInfo.RequiresMacOSLayoutRetry)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                StripTabView.SelectedItem = selectedItem;
                onSelected();
            });
            return;
        }

        onSelected();
    }

    /// <summary>
    /// Scrolls the strip so the given tab is visible when the tab lies outside the visible area. Neither head reveals
    /// a tab selected in the same pass that adds the tab, and the strip's overflow arrows are hidden. The strip's
    /// scroll offset is therefore driven directly.
    /// </summary>
    private void ScrollTabIntoView(object tab)
    {
        // Deferred to the next dispatcher cycle so the strip has completed layout. A tab just added, or a selection
        // that changes the strip's extent, has no scroll geometry to act on until the layout pass runs.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isShutDown)
            {
                return;
            }

            var tabList = VisualTree.FindDescendant<ListViewBase>(StripTabView);
            var scrollViewer = GetScrollViewer();
            if (tabList is null
                || scrollViewer is null)
            {
                return;
            }

            // Settle the strip before measuring it, so the offset and the bounds read below belong to the same
            // arrangement.
            tabList.UpdateLayout();

            if (tabList.ContainerFromItem(tab) is FrameworkElement container
                && container.ActualWidth > 0)
            {
                ScrollToRevealTab(container, scrollViewer);

                return;
            }

            // A virtualized tab has no bounds to measure, and only the strip's own reveal realizes the tab. Where the
            // tab lands is then corrected on the following cycle, once the tab has settled.
            tabList.ScrollIntoView(tab, ScrollIntoViewAlignment.Default);

            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (_isShutDown)
                {
                    return;
                }

                if (tabList.ContainerFromItem(tab) is FrameworkElement realized
                    && realized.ActualWidth > 0)
                {
                    ScrollToRevealTab(realized, scrollViewer);
                }
            });
        });
    }

    /// <summary>
    /// Scrolls the strip by the least distance that brings a tab fully into view. A tab already in view leaves the
    /// strip alone. The rest of the strip stays where the user left it.
    /// </summary>
    private static void ScrollToRevealTab(FrameworkElement container, ScrollViewer scrollViewer)
    {
        double viewportWidth = scrollViewer.ViewportWidth;
        if (viewportWidth <= 0)
        {
            // The strip is still unarranged, so the viewport has nothing to measure a tab against.
            return;
        }

        double tabViewportX = container.TransformToVisual(scrollViewer).TransformPoint(new Point(0, 0)).X;
        double tabWidth = container.ActualWidth;
        double currentOffset = scrollViewer.HorizontalOffset;

        double targetOffset;
        if (tabWidth >= viewportWidth)
        {
            // A tab too wide for the strip cannot be brought fully into view, so its leading edge is the one to show.
            // The label starts there, and the trailing edge has only the close button.
            targetOffset = currentOffset + tabViewportX;
        }
        else if (tabViewportX < 0)
        {
            targetOffset = currentOffset + tabViewportX;
        }
        else if (tabViewportX + tabWidth > viewportWidth)
        {
            targetOffset = currentOffset + (tabViewportX + tabWidth - viewportWidth);
        }
        else
        {
            return;
        }

        double clampedOffset = Math.Clamp(targetOffset, 0, scrollViewer.ScrollableWidth);

        // Transformed coordinates have a fractional part, so a tab already sitting where it is wanted still measures a
        // hair off. Scrolling by less than a pixel would move nothing.
        if (Math.Abs(clampedOffset - currentOffset) < RevealAlignmentTolerance)
        {
            return;
        }

        scrollViewer.ChangeView(clampedOffset, null, null, disableAnimation: true);
    }

    private void ClampScrollOffset()
    {
        var scrollViewer = GetScrollViewer();
        if (scrollViewer is not null
            && scrollViewer.HorizontalOffset > scrollViewer.ScrollableWidth)
        {
            scrollViewer.ChangeView(scrollViewer.ScrollableWidth, null, null, disableAnimation: true);
        }
    }

    /// <summary>
    /// Fixes the band at its authored height, so what a tab happens to contain cannot set the height of the strip.
    /// Left to itself the band takes the tallest tab, which puts one tab's icon in charge of the strip's height.
    /// </summary>
    private void FixBandHeight()
    {
        if (VisualTree.FindDescendantByName(StripTabView, "TabContainerGrid") is not FrameworkElement band)
        {
            return;
        }

        band.Height = WorkspaceConstants.SectionTabStripHeight;
    }

    /// <summary>
    /// Aligns the tab list to the top of the band. The list takes the height of the tabs alone. Left to itself the
    /// list is centered in the band, splitting the gap above the tabs.
    /// </summary>
    private void AlignTabListToBandTop()
    {
        var tabList = VisualTree.FindDescendant<ListViewBase>(StripTabView);
        if (tabList?.ItemsPanelRoot is null)
        {
            // The tab list's template has not been applied yet, so try again on the next cycle.
            return;
        }

        tabList.ItemsPanelRoot.VerticalAlignment = VerticalAlignment.Top;
    }

    /// <summary>
    /// Hides the strip's overflow scroll arrows for good. Each arrow takes 43px from a strip that is short of room
    /// whenever the arrows appear. The strip still scrolls by wheel, and the selected tab is revealed on selection.
    /// </summary>
    private void HideScrollButtons()
    {
        if (_scrollButtonsHidden)
        {
            return;
        }

        var tabList = VisualTree.FindDescendant<ListViewBase>(StripTabView);
        if (tabList is null)
        {
            // The tab list's template has not been applied yet, so try again on the next cycle.
            return;
        }

        foreach (var containerName in ScrollButtonContainerNames)
        {
            if (VisualTree.FindDescendantByName(tabList, containerName) is not FrameworkElement container)
            {
                continue;
            }

            container.Visibility = Visibility.Collapsed;

            // The strip shows the arrows again every time it starts overflowing, so the collapse is applied again on
            // each change.
            container.RegisterPropertyChangedCallback(VisibilityProperty, HoldScrollButtonCollapsed);

            // Set only once an arrow is found. Before its template applies, the list has neither arrow, and a later
            // attempt finds them.
            _scrollButtonsHidden = true;
        }
    }

    private static void HoldScrollButtonCollapsed(DependencyObject sender, DependencyProperty property)
    {
        if (sender is FrameworkElement container
            && container.Visibility != Visibility.Collapsed)
        {
            container.Visibility = Visibility.Collapsed;
        }
    }

    // UNO-BUG: IsAddTabButtonVisible=False collapses the add button's presenter, and leaves the button itself at the
    // declared 32px width. The strip still subtracts that width from the space the tabs get. Observed in 6.6.166, and
    // unverified on the packaged Windows head.
    /// <summary>
    /// Clears the width the collapsed add button reserves in the strip, so the tabs get the whole strip.
    /// </summary>
    private void ClearAddTabButtonWidthReservation()
    {
        if (StripTabView.IsAddTabButtonVisible)
        {
            return;
        }

        if (VisualTree.FindDescendantByName(StripTabView, "AddButton") is not FrameworkElement addButton)
        {
            return;
        }

        // The strip measures what the button wants, and a collapsed element wants nothing.
        addButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Hides the bottom border of the strip while the strip has no tabs. An empty strip then reads as part of the
    /// empty area below the strip.
    /// </summary>
    private void UpdateBorderLines()
    {
        var band = VisualTree.FindDescendantByName(StripTabView, "TabContainerGrid");
        if (band is null)
        {
            return;
        }

        // The stock TabView spreads this border over four elements. A pair in the TabView's template flanks the tab
        // list, and a 4px pair sits inside the list's items presenter. The template's visual states drive Visibility on
        // these elements, so the strip sets Opacity.
        double borderOpacity = StripTabView.TabItems.Count == 0 ? 0.0 : 1.0;

        foreach (var borderLine in VisualTree.FindDescendantsByName(band, "LeftBottomBorderLine"))
        {
            borderLine.Opacity = borderOpacity;
        }

        foreach (var borderLine in VisualTree.FindDescendantsByName(band, "RightBottomBorderLine"))
        {
            borderLine.Opacity = borderOpacity;
        }
    }

    // Wires the parts that need the strip's own ScrollViewer, which only exists once the tab list's template has been
    // applied.
    private void AttachScrollHandlers()
    {
        var scrollViewer = GetScrollViewer();
        if (scrollViewer is null)
        {
            return;
        }

        AttachScrollIndicator(scrollViewer);
        AttachWheelHandler(scrollViewer);

        // The strip scrolls a tab into view whenever that tab takes focus. Revealing is driven from the selection, so
        // that is turned off.
        scrollViewer.BringIntoViewOnFocusChange = false;
    }

    private void AttachScrollIndicator(ScrollViewer scrollViewer)
    {
        if (_scrollIndicatorAttached)
        {
            return;
        }

        scrollViewer.ViewChanged += OnStripViewChanged;
        ScrollIndicator.ScrollRequested += OnScrollIndicatorScrollRequested;
        _scrollIndicatorAttached = true;

        UpdateOverlays();
    }

    // UNO-BUG: the tab strip's ScrollViewer scrolls horizontal wheel input the wrong way on macOS.
    /// <summary>
    /// Takes over wheel scrolling of the strip on macOS. The handler sits inside the strip's ScrollViewer, on the
    /// presenter the wheel passes through first. The handler then sees each event before the ScrollViewer does.
    /// </summary>
    private void AttachWheelHandler(ScrollViewer scrollViewer)
    {
        if (!_platformInfo.RequiresMacOSTabWheelScroll
            || _wheelHandlerAttached)
        {
            return;
        }

        // The presenter fills the strip's viewport, so every wheel event over the strip bubbles through it.
        var scrollPresenter = VisualTree.FindDescendant<ScrollContentPresenter>(scrollViewer);
        if (scrollPresenter is null)
        {
            return;
        }

        scrollPresenter.AddHandler(PointerWheelChangedEvent, _wheelHandler, handledEventsToo: true);
        _wheelHandlerAttached = true;
    }

    private void OnStripViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        UpdateOverlays();
    }

    private void OnScrollIndicatorScrollRequested(double offset)
    {
        var scrollViewer = GetScrollViewer();
        scrollViewer?.ChangeView(offset, null, null, disableAnimation: true);
    }

    // The owner's overlays are placed from the same geometry as the scroll indicator, so they follow it.
    private void UpdateOverlays()
    {
        UpdateScrollIndicator();

        StripArranged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Places the scroll indicator inside the strip and hands it the strip's geometry.
    /// </summary>
    private void UpdateScrollIndicator()
    {
        if (_isShutDown)
        {
            return;
        }

        // A collapsed band means there is no strip, so there is nowhere for the indicator to sit.
        double bandHeight = VisibleBandHeight;
        var scrollViewer = GetScrollViewer();
        if (bandHeight <= 0
            || scrollViewer is null
            || scrollViewer.ActualWidth <= 0)
        {
            ScrollIndicator.Visibility = Visibility.Collapsed;

            return;
        }

        // The toolbar in the strip header decides where the tabs start. The leading edge is therefore the one part of
        // the placement to measure.
        var stripBounds = scrollViewer
            .TransformToVisual(RootGrid)
            .TransformBounds(new Rect(0, 0, scrollViewer.ActualWidth, scrollViewer.ActualHeight));

        // The pixel the bar keeps below itself clears the macOS web view, which clips a bar flush with the band's
        // bottom edge.
        double indicatorTop = stripBounds.Top + bandHeight - ScrollIndicator.Height - 1;

        ScrollIndicator.Width = stripBounds.Width;
        ScrollIndicator.Margin = new Thickness(stripBounds.Left, indicatorTop, 0, 0);

        ScrollIndicator.Update(
            scrollViewer.ExtentWidth,
            scrollViewer.ViewportWidth,
            scrollViewer.HorizontalOffset);
    }

    private ScrollViewer? GetScrollViewer()
    {
        var tabList = VisualTree.FindDescendant<ListViewBase>(StripTabView);

        return tabList is null ? null : VisualTree.FindDescendant<ScrollViewer>(tabList);
    }

    /// <summary>
    /// Scrolls the overflowing strip horizontally in response to the wheel. This covers both a mouse's vertical wheel
    /// and a trackpad's horizontal scrolling. The Uno TabView on macOS ignores vertical wheel input over the strip, and
    /// scrolls horizontal input backwards. The strip's scroll offset is therefore driven directly.
    /// </summary>
    private void OnStripPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_isShutDown)
        {
            return;
        }

        var scrollViewer = GetScrollViewer();
        if (scrollViewer is null
            || scrollViewer.ScrollableWidth <= 0)
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
            // A forward wheel notch, a positive delta, reveals earlier tabs, matching the packaged Windows TabView.
            // macOS reports a trackpad swipe towards the earlier tabs with the same sign. One rule then serves both
            // wheels.
            ScrollBy(-wheelDelta);
        }

        // An event off the gesture's axis is handled too, and stops short of the ScrollViewer. The ScrollViewer would
        // scroll horizontal input the other way and undo this.
        e.Handled = true;
    }
}
