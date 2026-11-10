using Celbridge.UserInterface.Views.Controls;
using Windows.Foundation;

namespace Celbridge.UserInterface.DragDrop;

/// <summary>
/// Where a dragged tab would land: a strip, and the insertion slot in its tab order, from 0 to the tab count.
/// OverStrip is false for a point outside the strip that still drops into the strip, like a document section's body.
/// </summary>
public record TabDropSlot(TabStripView Strip, int InsertionSlot, bool OverStrip);

/// <summary>
/// The owner of the strips a tab drag can reach. The owner decides where a tab can land, draws the drop feedback, and
/// moves the tab when the drag completes.
/// </summary>
public interface ITabDragOwner
{
    /// <summary>
    /// Returns where a tab dropped at the given point would land, or null for a point that refuses the drop.
    /// </summary>
    TabDropSlot? FindDropSlot(TabViewItem tab, Point overlayPosition);

    /// <summary>
    /// Shows the feedback for a drop slot, or clears the feedback for null.
    /// </summary>
    void ShowDropSlot(TabViewItem tab, TabDropSlot? slot);

    /// <summary>
    /// Moves a tab to the slot a drag ended over.
    /// </summary>
    void CompleteDrag(TabViewItem tab, TabDropSlot slot);

    /// <summary>
    /// Returns the label the drag ghost shows for a tab.
    /// </summary>
    string GetDragLabel(TabViewItem tab);
}
