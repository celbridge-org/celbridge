using Celbridge.Core;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

using KeyEventHandler = Microsoft.UI.Xaml.Input.KeyEventHandler;

namespace Celbridge.UserInterface.DragDrop;

/// <summary>
/// Pointer-driven resource drag coordinator. Tracks a pointer press reported by a drag source through
/// a drag threshold, renders a ghost that follows the pointer in the workspace overlay, drives the
/// registered drop targets' feedback, and dispatches the drop on release. Used on heads where the
/// built-in drag-and-drop is disabled; a single instance is shared and re-initialized per workspace.
/// </summary>
public sealed class ResourceDragCoordinator : IResourceDragCoordinator
{
    private const double DragThreshold = 5.0;

    private readonly List<IResourceDropTarget> _dropTargets = new();
    private readonly PointerEventHandler _pointerMovedHandler;
    private readonly PointerEventHandler _pointerReleasedHandler;
    private readonly PointerEventHandler _pointerCaptureLostHandler;
    private readonly KeyEventHandler _keyDownHandler;
    private readonly IStringLocalizer _stringLocalizer;

    private Canvas? _overlay;
    private UIElement? _trackingRoot;
    private DragGhost? _ghost;

    private IReadOnlyList<IResource>? _pressedResources;
    private Point _pressPosition;
    private bool _isDragging;
    private UIElement? _keyEventRoot;
    private IResourceDropTarget? _activeTarget;

    public ResourceDragCoordinator()
    {
        _pointerMovedHandler = OnPointerMoved;
        _pointerReleasedHandler = OnPointerReleased;
        _pointerCaptureLostHandler = OnPointerCaptureLost;
        _keyDownHandler = OnRootKeyDown;
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
    }

    public void Initialize(Canvas overlay, UIElement trackingRoot)
    {
        _overlay = overlay;
        _trackingRoot = trackingRoot;
        _ghost = new DragGhost(overlay);
    }

    public void Reset()
    {
        if (_pressedResources is not null)
        {
            EndTracking();
        }

        _dropTargets.Clear();
        _overlay = null;
        _trackingRoot = null;
        _ghost = null;
    }

    public void RegisterDropTarget(IResourceDropTarget dropTarget)
    {
        if (!_dropTargets.Contains(dropTarget))
        {
            _dropTargets.Add(dropTarget);
        }
    }

    public void UnregisterDropTarget(IResourceDropTarget dropTarget)
    {
        _dropTargets.Remove(dropTarget);
    }

    public void OnResourcePressed(IReadOnlyList<IResource> resources, PointerRoutedEventArgs e)
    {
        if (_trackingRoot is null ||
            resources.Count == 0)
        {
            return;
        }

        if (_pressedResources is not null)
        {
            // A stale press whose release was delivered elsewhere - reset before tracking anew.
            EndTracking();
        }

        var pointerPoint = e.GetCurrentPoint(_trackingRoot);
        if (!pointerPoint.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pressedResources = resources;
        _pressPosition = pointerPoint.Position;

        // Track on the root, not the source item: the tree's list captures the pointer on press, which
        // routes subsequent events to the capture owner and up its ancestor chain. The root is on that
        // chain.
        _trackingRoot.AddHandler(UIElement.PointerMovedEvent, _pointerMovedHandler, handledEventsToo: true);
        _trackingRoot.AddHandler(UIElement.PointerReleasedEvent, _pointerReleasedHandler, handledEventsToo: true);
        _trackingRoot.AddHandler(UIElement.PointerCaptureLostEvent, _pointerCaptureLostHandler, handledEventsToo: true);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pressedResources is null ||
            _trackingRoot is null)
        {
            return;
        }

        var position = e.GetCurrentPoint(_trackingRoot).Position;

        if (!_isDragging)
        {
            if (Math.Abs(position.X - _pressPosition.X) < DragThreshold &&
                Math.Abs(position.Y - _pressPosition.Y) < DragThreshold)
            {
                return;
            }

            BeginDrag(e);
        }

        UpdateDrag(e);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pressedResources is null)
        {
            return;
        }

        var resources = _pressedResources;
        var target = _activeTarget;
        bool wasDragging = _isDragging;

        var windowPoint = GetWindowPoint(e);

        EndTracking();

        if (!wasDragging)
        {
            // A plain click - leave selection and activation to the normal tap handling.
            return;
        }

        e.Handled = true;

        target?.TryDrop(windowPoint, resources);
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_pressedResources is null)
        {
            return;
        }

        if (_isDragging)
        {
            // During a drag, only the tracking root losing its own capture cancels; the source list
            // losing it is the expected result of stealing capture in BeginDrag.
            if (ReferenceEquals(e.OriginalSource, _trackingRoot))
            {
                EndTracking();
            }
        }
        else
        {
            // Before the drag starts the source list holds the capture. If it loses it - for example
            // the pressed item is recycled when a folder is collapsed - the press is over, so stop
            // tracking; otherwise a later pointer move could resume a phantom drag.
            EndTracking();
        }
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape &&
            _pressedResources is not null)
        {
            e.Handled = true;
            EndTracking();
        }
    }

    private void BeginDrag(PointerRoutedEventArgs e)
    {
        _isDragging = true;
        _trackingRoot!.CapturePointer(e.Pointer);
        _ghost?.Show(BuildLabel(_pressedResources!));

        // Escape cancels the drag. Wired on the window content so it fires wherever managed focus sits.
        _keyEventRoot = _trackingRoot.XamlRoot?.Content as UIElement;
        _keyEventRoot?.AddHandler(UIElement.KeyDownEvent, _keyDownHandler, handledEventsToo: true);
    }

    private void UpdateDrag(PointerRoutedEventArgs e)
    {
        var resources = _pressedResources!;

        if (_overlay is not null)
        {
            _ghost?.Move(e.GetCurrentPoint(_overlay).Position);
        }

        var windowPoint = GetWindowPoint(e);

        // The first target that accepts the point owns the feedback; every other target is cleared.
        _activeTarget = null;
        string? activeCaption = null;
        foreach (var target in _dropTargets)
        {
            if (_activeTarget is null)
            {
                var caption = target.UpdateDragOver(windowPoint, resources);
                if (caption is not null)
                {
                    _activeTarget = target;
                    activeCaption = caption;
                    continue;
                }
            }

            target.ClearDragFeedback();
        }

        _ghost?.SetCaption(activeCaption);
    }

    private void EndTracking()
    {
        if (_trackingRoot is not null)
        {
            _trackingRoot.RemoveHandler(UIElement.PointerMovedEvent, _pointerMovedHandler);
            _trackingRoot.RemoveHandler(UIElement.PointerReleasedEvent, _pointerReleasedHandler);
            _trackingRoot.RemoveHandler(UIElement.PointerCaptureLostEvent, _pointerCaptureLostHandler);
            _trackingRoot.ReleasePointerCaptures();
        }

        _keyEventRoot?.RemoveHandler(UIElement.KeyDownEvent, _keyDownHandler);
        _keyEventRoot = null;

        _ghost?.Hide();

        foreach (var target in _dropTargets)
        {
            target.ClearDragFeedback();
        }

        _pressedResources = null;
        _activeTarget = null;
        _isDragging = false;
    }

    private Point GetWindowPoint(PointerRoutedEventArgs e)
    {
        var windowContent = _trackingRoot?.XamlRoot?.Content as UIElement;

        return windowContent is null ? default : e.GetCurrentPoint(windowContent).Position;
    }

    private string BuildLabel(IReadOnlyList<IResource> resources)
    {
        if (resources.Count == 1)
        {
            return resources[0].Name;
        }

        return _stringLocalizer.GetString("ResourceTree_DragItemCount", resources.Count);
    }
}
