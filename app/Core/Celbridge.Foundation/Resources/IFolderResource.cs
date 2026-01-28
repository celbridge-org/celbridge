namespace Celbridge.Resources;

/// <summary>
/// A folder resource in the project folder.
/// </summary>
public interface IFolderResource : IResource
{
    /// <summary>
    /// The child resources of the folder.
    /// </summary>
    IList<IResource> Children { get; set; }

    /// <summary>
    /// The expanded state of the folder in the tree view.
    /// </summary>
    bool IsExpanded { get; set; }
}
