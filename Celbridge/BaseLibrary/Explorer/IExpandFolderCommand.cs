using Celbridge.Commands;

namespace Celbridge.Explorer;

/// <summary>
/// Sets the expanded/collapsed state of a folder in the resource tree view.
/// </summary>
public interface IExpandFolderCommand : IExecutableCommand
{
    /// <summary>
    /// The folder to be expanded or collapsed.
    /// </summary>
    ResourceKey FolderResource { get; set; }

    /// <summary>
    /// If true, the folder will be expanded, if false the folder will be collapsed.
    /// </summary>
    bool Expanded { get; set; }

    /// <summary>
    /// If true, the tree view will be updated to reflect the new state of the folder.
    /// </summary>
    bool UpdateResources { get; set; }
}
