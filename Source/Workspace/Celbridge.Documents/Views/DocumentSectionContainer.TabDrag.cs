using Windows.Foundation;

namespace Celbridge.Documents.Views;

/// <summary>
/// The section and insertion slot a resource drop from the Explorer would land in.
/// </summary>
public record ResourceDropLocation(DocumentSectionView SectionView, int InsertionSlot);

/// <summary>
/// Drag support for DocumentSectionContainer: the tab drag, the drop preview, and the commit of a
/// completed tab drag. A tab drag and a resource drag from the Explorer share the drop preview.
/// </summary>
public sealed partial class DocumentSectionContainer
{
    private TabDragController? _tabDragController;
    private SectionDragPreview? _dropPreview;
    private FrameworkElement? _dragInputRoot;

    /// <summary>
    /// Enables the drag overlay: the tab drag, and the shared drop-target preview of an insertion
    /// divider and a section highlight. The input root spans every section, and a resource drop's
    /// window point is mapped through it.
    /// </summary>
    public void InitializeTabDrag(Canvas dragOverlay, FrameworkElement inputRoot)
    {
        _dragInputRoot = inputRoot;
        _dropPreview = new SectionDragPreview(this, dragOverlay);
        _tabDragController = new TabDragController(this, dragOverlay, _dropPreview);
    }

    /// <summary>
    /// Resolves the section and insertion slot a resource drop at the given window point would land in,
    /// or null for a point outside every section and for a drag overlay still unset.
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
        if (_dragInputRoot?.XamlRoot?.Content is UIElement windowContent)
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
            ActivateDocument(
                tab.ViewModel.FileResource,
                sourceSectionView.Section,
                ActiveDocumentChangeReason.Activated);
            NotifyLayoutChanged();
        }
        else if (MoveTabToSection(tab, targetSectionView.Section, insertionSlot))
        {
            NotifyLayoutChanged();
        }
    }

    private void OnSectionTabPointerPressed(DocumentTab tab, PointerRoutedEventArgs e)
    {
        _tabDragController?.OnTabPressed(tab, e);
    }
}
