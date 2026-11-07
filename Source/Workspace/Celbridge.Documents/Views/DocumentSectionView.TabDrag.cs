using Celbridge.UserInterface.DragDrop;
using Celbridge.UserInterface.Views.Controls;
using Windows.Foundation;

namespace Celbridge.Documents.Views;

/// <summary>
/// Tab drag support for DocumentSectionView. The section reports the press a drag starts from, and the strip
/// geometry a drop is placed against. A drop within the section reorders the tabs.
/// </summary>
public sealed partial class DocumentSectionView
{
    /// <summary>
    /// Event raised when a pointer is pressed on a document tab. Feeds the tab drag.
    /// </summary>
    public event Action<DocumentTab, PointerRoutedEventArgs>? TabPointerPressed;

    /// <summary>
    /// The strip this section's tabs sit in.
    /// </summary>
    internal TabStripView TabStrip => DocumentTabStrip;

    private void OnStripTabPressed(TabViewItem tab, PointerRoutedEventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        if (tab is DocumentTab documentTab)
        {
            TabPointerPressed?.Invoke(documentTab, e);
        }
    }

    /// <summary>
    /// Moves a tab to the given insertion slot within this section. The slot is an insertion point in the current tab
    /// order, from 0 to TabCount.
    /// </summary>
    public void ReorderTab(DocumentTab tab, int insertionSlot)
    {
        EnsureUIThread();

        int currentIndex = TabView.TabItems.IndexOf(tab);
        if (currentIndex < 0)
        {
            return;
        }

        int targetIndex = TabDragGesture.GetTargetIndex(currentIndex, insertionSlot);
        if (DocumentTabStrip.MoveTab(tab, targetIndex))
        {
            // Flash the tab at its new position so the address change stands out.
            tab.FlashAttentionDeferred();
        }
    }

    /// <summary>
    /// Gets the bounds of the tab strip relative to the given element, or Rect.Empty when the strip is collapsed or
    /// not yet laid out.
    /// </summary>
    public Rect GetTabStripBounds(UIElement relativeTo)
    {
        EnsureUIThread();

        return DocumentTabStrip.GetStripBounds(relativeTo);
    }

    /// <summary>
    /// Gets the bounds of each tab relative to the given element, in tab order. A tab not laid out has empty bounds.
    /// </summary>
    public List<Rect> GetTabBounds(UIElement relativeTo)
    {
        EnsureUIThread();

        return DocumentTabStrip.GetTabBounds(relativeTo);
    }

    /// <summary>
    /// Gets the insertion slot, from 0 to TabCount, for a pointer at the given X. The X is relative to the given
    /// element.
    /// </summary>
    public int GetInsertionSlot(double pointerX, UIElement relativeTo)
    {
        EnsureUIThread();

        return DocumentTabStrip.GetInsertionSlot(pointerX, relativeTo);
    }
}
