using Windows.Foundation;

namespace Celbridge.UserInterface.DragDrop;

/// <summary>
/// A place a pointer-driven resource drag can be dropped, such as a folder in the resource tree or a
/// document section. The drag coordinator asks each registered target to update its own feedback as
/// the pointer moves, and to handle the drop on release. Points are relative to the window content
/// (XamlRoot.Content) so every target can convert them into its own coordinate space.
/// </summary>
public interface IResourceDropTarget
{
    /// <summary>
    /// Updates this target's drag feedback for a drag hovering at the given window point, and returns
    /// the drop caption to show (for example "Open", "Move" or "Copy") when the point is over a part of
    /// this target that accepts the drop. Returns null when the point is not over a droppable part, in
    /// which case the target shows no feedback.
    /// </summary>
    string? UpdateDragOver(Point windowPoint, IReadOnlyList<IResource> resources);

    /// <summary>
    /// Clears any drag feedback this target is currently showing.
    /// </summary>
    void ClearDragFeedback();

    /// <summary>
    /// Attempts to drop the resources at the given window point, returning true when this target handled
    /// the drop.
    /// </summary>
    bool TryDrop(Point windowPoint, IReadOnlyList<IResource> resources);
}
