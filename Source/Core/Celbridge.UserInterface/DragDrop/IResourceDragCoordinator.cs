namespace Celbridge.UserInterface.DragDrop;

/// <summary>
/// Coordinates pointer-driven resource drags across the workspace, used on heads where the built-in
/// drag-and-drop is disabled. A drag source (the resource tree) reports a pointer press on its items;
/// the coordinator renders a managed ghost in the workspace overlay, asks the registered drop targets
/// (tree folders, document sections) for feedback as the pointer moves, and dispatches the drop on
/// release. A single instance is shared for the lifetime of the app and re-initialized per workspace.
/// </summary>
public interface IResourceDragCoordinator
{
    /// <summary>
    /// Binds the coordinator to a workspace: the overlay canvas the ghost renders into, and the element
    /// whose coordinate space the pointer is tracked in (the workspace layout root). Called when a
    /// workspace loads.
    /// </summary>
    void Initialize(Canvas overlay, UIElement trackingRoot);

    /// <summary>
    /// Cancels any drag in flight and unbinds the coordinator from the current workspace. Called when a
    /// workspace unloads.
    /// </summary>
    void Reset();

    /// <summary>
    /// Registers a drop target the coordinator queries during a drag. Targets register when their panel
    /// loads.
    /// </summary>
    void RegisterDropTarget(IResourceDropTarget dropTarget);

    /// <summary>
    /// Removes a previously registered drop target. Targets unregister when their panel unloads.
    /// </summary>
    void UnregisterDropTarget(IResourceDropTarget dropTarget);

    /// <summary>
    /// Begins tracking a pointer press on draggable resources. The drag itself starts only once the
    /// pointer travels past the drag threshold, so plain clicks are unaffected.
    /// </summary>
    void OnResourcePressed(IReadOnlyList<IResource> resources, PointerRoutedEventArgs e);
}
