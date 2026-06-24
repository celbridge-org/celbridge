namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Shared state for resource drags that cross UI control boundaries (e.g. Explorer to a document
/// section). The DataPackage.Properties channel that carries the payload on Windows does not
/// round-trip on the Uno Skia desktop head, so the source publishes the in-flight resources here
/// alongside the DataPackage; consumers read this as a fallback when the payload is empty. A stale
/// value is harmless because DragOver only fires while a drag is in flight, and the next drag
/// overwrites it.
/// </summary>
public static class ResourceDragState
{
    private static IReadOnlyList<IResource>? _resources;

    /// <summary>
    /// The resources for the drag operation currently in flight, or null when no drag has started.
    /// </summary>
    public static IReadOnlyList<IResource>? Current => _resources;

    /// <summary>
    /// Records the resources for a new drag operation. Called by the source on drag start.
    /// </summary>
    public static void Begin(IReadOnlyList<IResource> resources)
    {
        _resources = resources;
    }

    /// <summary>
    /// Clears the in-flight drag. Consumers call this after handling a drop.
    /// </summary>
    public static void End()
    {
        _resources = null;
    }
}
