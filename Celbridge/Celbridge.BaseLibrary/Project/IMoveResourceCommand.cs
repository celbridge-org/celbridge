using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Move a resource to a different path in the project.
/// </summary>
public interface IMoveResourceCommand : IExecutableCommand
{
    /// <summary>
    /// Resource to be moved.
    /// </summary>
    ResourceKey FromResourceKey { get; set; }

    /// <summary>
    /// Resource key to move to.
    /// </summary>
    ResourceKey ToResourceKey { get; set; }

    /// <summary>
    /// If the resource is a folder, expand the folder after moving it.
    /// </summary>
    bool ExpandMovedFolder { get; set; }
}
