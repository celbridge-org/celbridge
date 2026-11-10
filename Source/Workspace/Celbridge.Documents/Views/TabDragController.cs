using Celbridge.UserInterface.DragDrop;
using Celbridge.UserInterface.Views.Controls;
using Windows.Foundation;

namespace Celbridge.Documents.Views;

/// <summary>
/// The owner of a document tab drag. A tab drops into any mounted section, over the section's strip or the
/// section's body. The pointer's X marks the slot in both cases, and a drop over the body also tints the section.
/// The shared tab drag gesture tracks the pointer, and the container commits the reorder or the move.
/// </summary>
internal sealed class TabDragController : ITabDragOwner
{
    private readonly DocumentSectionContainer _container;
    private readonly Canvas _overlay;
    private readonly SectionDragPreview _dropPreview;
    private readonly TabDragGesture _gesture;

    public TabDragController(DocumentSectionContainer container, Canvas overlay, SectionDragPreview dropPreview)
    {
        _container = container;
        _overlay = overlay;
        _dropPreview = dropPreview;
        _gesture = new TabDragGesture(this, overlay);
    }

    /// <summary>
    /// Begins tracking a press on a tab header. The drag itself starts only if the pointer travels past the drag
    /// threshold, so plain clicks are unaffected.
    /// </summary>
    public void OnTabPressed(DocumentTab tab, PointerRoutedEventArgs e)
    {
        _gesture.OnTabPressed(tab, e);
    }

    public string GetDragLabel(TabViewItem tab)
    {
        return tab is DocumentTab documentTab ? documentTab.ViewModel.DocumentName : string.Empty;
    }

    public TabDropSlot? FindDropSlot(TabViewItem tab, Point overlayPosition)
    {
        foreach (var sectionView in _container.GetMountedSections())
        {
            var strip = sectionView.TabStrip;

            var stripBounds = strip.GetStripBounds(_overlay);
            if (!stripBounds.IsEmpty &&
                stripBounds.Contains(overlayPosition))
            {
                int slot = strip.GetInsertionSlot(overlayPosition.X, _overlay);
                return new TabDropSlot(strip, slot, OverStrip: true);
            }

            var sectionBounds = GetElementBounds(sectionView);
            if (sectionBounds.Contains(overlayPosition))
            {
                int slot = strip.GetInsertionSlot(overlayPosition.X, _overlay);
                return new TabDropSlot(strip, slot, OverStrip: false);
            }
        }

        return null;
    }

    public void ShowDropSlot(TabViewItem tab, TabDropSlot? slot)
    {
        var sectionView = slot is null ? null : FindSection(slot.Strip);
        if (slot is null
            || sectionView is null)
        {
            _dropPreview.Hide();
            return;
        }

        _dropPreview.ShowInsertion(sectionView, slot.InsertionSlot, tab as DocumentTab);

        if (slot.OverStrip)
        {
            _dropPreview.HideHighlight();
        }
        else
        {
            _dropPreview.ShowHighlight(sectionView);
        }
    }

    // The source is the section holding the tab at the drop, since a command can move the tab during the drag.
    public void CompleteDrag(TabViewItem tab, TabDropSlot slot)
    {
        if (tab is not DocumentTab documentTab)
        {
            return;
        }

        var sourceSectionView = FindSection(documentTab);
        var targetSectionView = FindSection(slot.Strip);
        if (sourceSectionView is null
            || targetSectionView is null)
        {
            return;
        }

        _container.CommitTabDrag(documentTab, sourceSectionView, targetSectionView, slot.InsertionSlot);
    }

    private DocumentSectionView? FindSection(TabStripView strip)
    {
        return _container.GetMountedSections().FirstOrDefault(sectionView => ReferenceEquals(sectionView.TabStrip, strip));
    }

    private DocumentSectionView? FindSection(DocumentTab tab)
    {
        return _container.GetMountedSections().FirstOrDefault(sectionView => sectionView.TabStrip.TabView.TabItems.Contains(tab));
    }

    private Rect GetElementBounds(FrameworkElement element)
    {
        if (element.ActualWidth <= 0)
        {
            return Rect.Empty;
        }

        var transform = element.TransformToVisual(_overlay);

        return transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
    }
}
