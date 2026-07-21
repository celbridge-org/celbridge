using Celbridge.Platform;
using Windows.Foundation;

namespace Celbridge.Documents.Views;

/// <summary>
/// The section and insertion slot a resource drop from the Explorer would land in.
/// </summary>
public record ResourceDropLocation(DocumentSection Section, int InsertionSlot);

/// <summary>
/// Pointer-driven tab drag support for DocumentSectionContainer, used on heads where the built-in
/// TabView drag-and-drop is disabled. Hosts the TabDragController and commits completed drags. Kept
/// in its own partial so the desktop-only drag surface stays discoverable.
/// </summary>
public sealed partial class DocumentSectionContainer
{
    private TabDragController? _tabDragController;
    private SectionDragPreview? _dropPreview;

    /// <summary>
    /// Enables the drag overlay used on heads where the built-in TabView drag-and-drop is disabled:
    /// the pointer-driven tab drag controller and the shared drop-target preview (the insertion divider
    /// and section highlight, used by tab drags and by resource drags from the Explorer). No-op on heads
    /// that keep the built-in drag-and-drop.
    /// </summary>
    public void InitializeTabDrag(Canvas dragOverlay)
    {
        var platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();
        if (!platformInfo.UsesPointerDrivenTabDrag)
        {
            return;
        }

        _dropPreview = new SectionDragPreview(this, dragOverlay);
        _tabDragController = new TabDragController(this, dragOverlay, _dropPreview);
    }

    /// <summary>
    /// Resolves the section and insertion slot a resource drop at the given window point would land in,
    /// or null when the point is over no section or the drag overlay is not active on this head.
    /// </summary>
    public ResourceDropLocation? GetResourceDropLocation(Point windowPoint)
    {
        if (_dropPreview is null)
        {
            return null;
        }

        var section = GetSectionAtWindowPoint(windowPoint);
        if (section is null)
        {
            return null;
        }

        var sectionPoint = WindowToSectionPoint(section, windowPoint);
        int slot = section.GetInsertionSlot(sectionPoint.X, section);

        return new ResourceDropLocation(section, slot);
    }

    /// <summary>
    /// Shows the drop-target divider and highlight for a resource drag over the given location.
    /// </summary>
    public void ShowResourceDropPreview(ResourceDropLocation location)
    {
        if (_dropPreview is null)
        {
            return;
        }

        _dropPreview.ShowInsertion(location.Section, location.InsertionSlot, draggedTab: null);
        _dropPreview.ShowHighlight(location.Section);
    }

    /// <summary>
    /// Clears any resource drop-target feedback.
    /// </summary>
    public void HideResourceDropPreview()
    {
        _dropPreview?.Hide();
    }

    private DocumentSection? GetSectionAtWindowPoint(Point windowPoint)
    {
        for (int i = 0; i < _sectionCount && i < _sections.Count; i++)
        {
            var section = _sections[i];
            var local = WindowToSectionPoint(section, windowPoint);
            if (local.X >= 0 &&
                local.Y >= 0 &&
                local.X < section.ActualWidth &&
                local.Y < section.ActualHeight)
            {
                return section;
            }
        }

        return null;
    }

    private Point WindowToSectionPoint(DocumentSection section, Point windowPoint)
    {
        if (XamlRoot?.Content is UIElement windowContent)
        {
            return windowContent.TransformToVisual(section).TransformPoint(windowPoint);
        }

        return windowPoint;
    }

    /// <summary>
    /// Commits a completed tab drag: a reorder within the source section, or a move to another
    /// section at the given insertion slot.
    /// </summary>
    internal void CommitTabDrag(DocumentTab tab, DocumentSection sourceSection, DocumentSection targetSection, int insertionSlot)
    {
        if (sourceSection == targetSection)
        {
            sourceSection.ReorderTab(tab, insertionSlot);
            ActivateDocument(tab.ViewModel.FileResource, sourceSection.SectionIndex);
            NotifyLayoutChanged();
        }
        else if (MoveTabToSection(tab, targetSection.SectionIndex, insertionSlot))
        {
            NotifyLayoutChanged();
        }
    }

    private void OnSectionTabPointerPressed(DocumentSection section, DocumentTab tab, PointerRoutedEventArgs e)
    {
        _tabDragController?.OnTabPressed(section, tab, e);
    }
}
