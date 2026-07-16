using Celbridge.Platform;
using Celbridge.UserInterface.Helpers;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Celbridge.Documents.Views;

/// <summary>
/// Pointer-driven tab drag support for DocumentSectionContainer, used on heads where the built-in
/// TabView drag-and-drop is disabled. Hosts the TabDragController and commits completed drags. Kept
/// in its own partial so the desktop-only drag surface stays discoverable.
/// </summary>
public sealed partial class DocumentSectionContainer
{
    private TabDragController? _tabDragController;
    private SectionDragPreview? _dropPreview;
    private DocumentSection? _highlightedSection;

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

        // A resource drag can end without a section leave or drop being delivered (released away from a
        // drop target, or cancelled), so clear the preview whenever any resource drag ends.
        ResourceDragState.Ended += OnResourceDragEnded;
    }

    private void OnSectionResourceDragOver(DocumentSection section, Point position)
    {
        if (_dropPreview is null)
        {
            return;
        }

        int slot = section.GetInsertionSlot(position.X, section);
        _dropPreview.ShowInsertion(section, slot, draggedTab: null);
        _dropPreview.ShowHighlight(section);
        _highlightedSection = section;
    }

    private void OnSectionResourceDragLeave(DocumentSection section)
    {
        // The pointer may already have moved on to another section, which re-targeted the single
        // preview. Only a leave from the section still highlighted should clear it.
        if (_highlightedSection != section)
        {
            return;
        }

        _dropPreview?.Hide();
        _highlightedSection = null;
    }

    private void OnResourceDragEnded()
    {
        _dropPreview?.Hide();
        _highlightedSection = null;
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
