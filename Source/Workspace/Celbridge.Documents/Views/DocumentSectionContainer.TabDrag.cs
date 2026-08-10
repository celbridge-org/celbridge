using Celbridge.Platform;
using Windows.Foundation;

namespace Celbridge.Documents.Views;

/// <summary>
/// The section and insertion slot a resource drop from the Explorer would land in.
/// </summary>
public record ResourceDropLocation(DocumentSectionView SectionView, int InsertionSlot);

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

        var sectionView = GetSectionAtWindowPoint(windowPoint);
        if (sectionView is null)
        {
            return null;
        }

        var sectionPoint = WindowToSectionPoint(sectionView, windowPoint);
        int slot = sectionView.GetInsertionSlot(sectionPoint.X, sectionView);

        return new ResourceDropLocation(sectionView, slot);
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

        _dropPreview.ShowInsertion(location.SectionView, location.InsertionSlot, draggedTab: null);
        _dropPreview.ShowHighlight(location.SectionView);
    }

    /// <summary>
    /// Clears any resource drop-target feedback.
    /// </summary>
    public void HideResourceDropPreview()
    {
        _dropPreview?.Hide();
    }

    private DocumentSectionView? GetSectionAtWindowPoint(Point windowPoint)
    {
        foreach (var sectionView in GetMountedSections())
        {
            var local = WindowToSectionPoint(sectionView, windowPoint);
            if (local.X >= 0 &&
                local.Y >= 0 &&
                local.X < sectionView.ActualWidth &&
                local.Y < sectionView.ActualHeight)
            {
                return sectionView;
            }
        }

        return null;
    }

    private Point WindowToSectionPoint(DocumentSectionView sectionView, Point windowPoint)
    {
        if (XamlRoot?.Content is UIElement windowContent)
        {
            return windowContent.TransformToVisual(sectionView).TransformPoint(windowPoint);
        }

        return windowPoint;
    }

    /// <summary>
    /// Commits a completed tab drag: a reorder within the source section, or a move to another
    /// section at the given insertion slot.
    /// </summary>
    internal void CommitTabDrag(DocumentTab tab, DocumentSectionView sourceSectionView, DocumentSectionView targetSectionView, int insertionSlot)
    {
        if (sourceSectionView == targetSectionView)
        {
            sourceSectionView.ReorderTab(tab, insertionSlot);
            ActivateDocument(tab.ViewModel.FileResource, sourceSectionView.Section);
            NotifyLayoutChanged();
        }
        else if (MoveTabToSection(tab, targetSectionView.Section, insertionSlot))
        {
            NotifyLayoutChanged();
        }
    }

    private void OnSectionTabPointerPressed(DocumentSectionView sectionView, DocumentTab tab, PointerRoutedEventArgs e)
    {
        _tabDragController?.OnTabPressed(sectionView, tab, e);
    }
}
