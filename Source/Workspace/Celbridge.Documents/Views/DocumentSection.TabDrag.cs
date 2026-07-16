using Celbridge.UserInterface.Helpers;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Celbridge.Documents.Views;

/// <summary>
/// A document tab header and its bounds relative to a reference element, used for drag hit-testing.
/// </summary>
public record TabHeaderBounds(DocumentTab Tab, Rect Bounds);

/// <summary>
/// Pointer-driven tab drag support for DocumentSection, used on heads where the built-in TabView
/// drag-and-drop is disabled (see TabDragController). Kept in its own partial so the desktop-only
/// drag surface stays discoverable and separate from the core tab management.
/// </summary>
public sealed partial class DocumentSection
{
    private readonly PointerEventHandler _tabPointerPressedHandler;

    /// <summary>
    /// Event raised when a pointer is pressed on a document tab header. Feeds the pointer-driven
    /// tab drag controller on heads where the built-in tab drag-and-drop is disabled.
    /// </summary>
    public event Action<DocumentSection, DocumentTab, PointerRoutedEventArgs>? TabPointerPressed;

    /// <summary>
    /// Disables the built-in TabView drag-and-drop on heads that use the pointer-driven controller,
    /// so the two do not compete for the same pointer gestures.
    /// </summary>
    private void DisableBuiltInTabDrag()
    {
        if (_platformInfo.UsesPointerDrivenTabDrag)
        {
            TabView.CanDragTabs = false;
            TabView.CanReorderTabs = false;
        }
    }

    private void OnTabPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        if (sender is DocumentTab tab)
        {
            TabPointerPressed?.Invoke(this, tab, e);
        }
    }

    // Presses inside a tab can be marked handled by the item's own pointer logic before they
    // bubble, so the handler must also see handled events.
    private void AddTabPointerPressedHandler(DocumentTab tab)
    {
        tab.AddHandler(PointerPressedEvent, _tabPointerPressedHandler, handledEventsToo: true);
    }

    private void RemoveTabPointerPressedHandler(DocumentTab tab)
    {
        tab.RemoveHandler(PointerPressedEvent, _tabPointerPressedHandler);
    }

    /// <summary>
    /// Moves a tab to the given insertion slot within this section. The slot is an insertion point
    /// in the current tab order (0 to TabCount inclusive), as computed by drag hit-testing.
    /// </summary>
    public void ReorderTab(DocumentTab tab, int insertionSlot)
    {
        EnsureUIThread();

        int currentIndex = TabView.TabItems.IndexOf(tab);
        if (currentIndex < 0)
        {
            return;
        }

        // Removing the tab shifts later slots down by one.
        int targetIndex = insertionSlot > currentIndex ? insertionSlot - 1 : insertionSlot;
        targetIndex = Math.Clamp(targetIndex, 0, TabView.TabItems.Count - 1);
        if (targetIndex == currentIndex)
        {
            return;
        }

        bool wasSelected = TabView.SelectedItem == tab;
        TabView.TabItems.RemoveAt(currentIndex);
        DetachStrandedContainer(tab);
        TabView.TabItems.Insert(targetIndex, tab);

        if (wasSelected)
        {
            SetSelectedItemWithLayoutRetry(tab, () => ScrollTabIntoView(tab));
        }

        // Flash the tab at its new position so the address change stands out.
        tab.FlashAttentionDeferred();
    }

    /// <summary>
    /// Gets the bounds of the tab strip relative to the given element, or Rect.Empty when the strip
    /// is collapsed or not yet laid out.
    /// </summary>
    public Rect GetTabStripBounds(UIElement relativeTo)
    {
        EnsureUIThread();

        var tabListView = VisualTree.FindDescendant<ListViewBase>(TabView);
        if (tabListView is null ||
            tabListView.ActualWidth <= 0 ||
            TabView.Visibility == Visibility.Collapsed)
        {
            return Rect.Empty;
        }

        var transform = tabListView.TransformToVisual(relativeTo);

        return transform.TransformBounds(new Rect(0, 0, tabListView.ActualWidth, tabListView.ActualHeight));
    }

    /// <summary>
    /// Gets the insertion slot (0 to TabCount inclusive) for a pointer at the given X, comparing it to
    /// the tab centres. The X and the tab bounds are both taken relative to the given element, so any
    /// element shared with the caller works.
    /// </summary>
    public int GetInsertionSlot(double pointerX, UIElement relativeTo)
    {
        var headerBounds = GetTabHeaderBounds(relativeTo);
        for (int i = 0; i < headerBounds.Count; i++)
        {
            var bounds = headerBounds[i].Bounds;
            double centerX = bounds.X + (bounds.Width / 2);
            if (pointerX < centerX)
            {
                return i;
            }
        }

        return headerBounds.Count;
    }

    /// <summary>
    /// Gets each tab header and its bounds relative to the given element, in tab order.
    /// </summary>
    public List<TabHeaderBounds> GetTabHeaderBounds(UIElement relativeTo)
    {
        EnsureUIThread();

        var headerBounds = new List<TabHeaderBounds>();
        foreach (var tabItem in TabView.TabItems)
        {
            if (tabItem is not DocumentTab tab ||
                tab.ActualWidth <= 0)
            {
                continue;
            }

            var transform = tab.TransformToVisual(relativeTo);
            var bounds = transform.TransformBounds(new Rect(0, 0, tab.ActualWidth, tab.ActualHeight));
            headerBounds.Add(new TabHeaderBounds(tab, bounds));
        }

        return headerBounds;
    }

    /// <summary>
    /// Scrolls the tab strip horizontally by the given delta in pixels, clamped to the scrollable range.
    /// </summary>
    public void ScrollTabStripBy(double delta)
    {
        EnsureUIThread();

        var scrollViewer = GetTabStripScrollViewer();
        if (scrollViewer is null)
        {
            return;
        }

        double targetOffset = Math.Clamp(scrollViewer.HorizontalOffset + delta, 0, scrollViewer.ScrollableWidth);
        scrollViewer.ChangeView(targetOffset, null, null, disableAnimation: true);
    }
}
