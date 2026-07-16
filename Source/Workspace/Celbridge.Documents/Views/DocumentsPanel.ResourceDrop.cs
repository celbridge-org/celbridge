using Celbridge.Platform;
using Celbridge.UserInterface.DragDrop;
using Microsoft.Extensions.Localization;
using Windows.Foundation;

namespace Celbridge.Documents.Views;

/// <summary>
/// Resource drop-target support for DocumentsPanel, used on heads where the built-in drag-and-drop is
/// disabled. Registers the panel with the shared drag coordinator so a resource dragged from the
/// Explorer over a document section shows the divider and highlight (via SectionDragPreview) and opens
/// at that slot on drop. Kept in its own partial so the desktop-only drag surface stays discoverable.
/// </summary>
public sealed partial class DocumentsPanel : IResourceDropTarget
{
    private IResourceDragCoordinator? _resourceDragCoordinator;

    private void ConfigureResourceDropTarget()
    {
        var platformInfo = _serviceProvider.AcquireService<IPlatformInfo>();
        if (!platformInfo.UsesPointerDrivenTabDrag)
        {
            return;
        }

        _resourceDragCoordinator = _serviceProvider.AcquireService<IResourceDragCoordinator>();
    }

    private void RegisterAsResourceDropTarget()
    {
        _resourceDragCoordinator?.RegisterDropTarget(this);
    }

    private void UnregisterAsResourceDropTarget()
    {
        _resourceDragCoordinator?.UnregisterDropTarget(this);
    }

    public string? UpdateDragOver(Point windowPoint, IReadOnlyList<IResource> resources)
    {
        var location = SectionContainer.GetResourceDropLocation(windowPoint);
        if (location is null)
        {
            SectionContainer.HideResourceDropPreview();
            return null;
        }

        SectionContainer.ShowResourceDropPreview(location);

        return _stringLocalizer.GetString("ResourceTree_Open");
    }

    public void ClearDragFeedback()
    {
        SectionContainer.HideResourceDropPreview();
    }

    public bool TryDrop(Point windowPoint, IReadOnlyList<IResource> resources)
    {
        var location = SectionContainer.GetResourceDropLocation(windowPoint);
        SectionContainer.HideResourceDropPreview();
        if (location is null)
        {
            return false;
        }

        _ = HandleDroppedFiles(location.Section, resources.ToList(), location.InsertionSlot);

        return true;
    }
}
