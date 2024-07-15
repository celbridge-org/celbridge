using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Move a folder resource to a different path in the project.
/// </summary>
public interface IMoveFolderCommand : IExecutableCommand
{
    /// <summary>
    /// Resource key of the folder to be moved.
    /// </summary>
    ResourceKey FromResourceKey { get; set; }

    /// <summary>
    /// Resource key to move the folder to.
    /// </summary>
    ResourceKey ToResourceKey { get; set; }

    /// <summary>
    /// Expand the folder in the tree view after moving it.
    /// </summary>
    bool ExpandMovedFolder { get; set; }
}
