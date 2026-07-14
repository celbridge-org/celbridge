using Celbridge.Platform;
using Microsoft.UI.Xaml.Input;

namespace Celbridge.Documents.Views;

/// <summary>
/// Pointer-driven tab drag support for DocumentSectionContainer, used on heads where the built-in
/// TabView drag-and-drop is disabled. Hosts the TabDragController and commits completed drags. Kept
/// in its own partial so the desktop-only drag surface stays discoverable.
/// </summary>
public sealed partial class DocumentSectionContainer
{
    private TabDragController? _tabDragController;

    /// <summary>
    /// Enables the pointer-driven tab drag controller, rendering drag visuals in the given overlay.
    /// No-op on heads that keep the built-in TabView drag-and-drop.
    /// </summary>
    public void InitializeTabDrag(Canvas dragOverlay)
    {
        var platformInfo = ServiceLocator.AcquireService<IPlatformInfo>();
        if (!platformInfo.UsesPointerDrivenTabDrag)
        {
            return;
        }

        _tabDragController = new TabDragController(this, dragOverlay);
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
