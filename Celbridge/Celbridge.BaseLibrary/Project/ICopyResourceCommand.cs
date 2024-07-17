using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Copy a resource to a different location in the project.
/// </summary>
public interface ICopyResourceCommand : IExecutableCommand
{
    /// <summary>
    /// Resource to be copied.
    /// </summary>
    ResourceKey SourceResourceKey { get; set; }

    /// <summary>
    /// Location to move the resource to.
    /// </summary>
    ResourceKey DestResourceKey { get; set; }

    /// <summary>
    /// Controls whether the resource is copied or moved to the new location.
    /// If the resource is moved, the resource in the original location is deleted.
    /// </summary>
    CopyResourceOperation Operation { get; set; }

    /// <summary>
    /// If the copied resource is a folder, expand the folder after moving it.
    /// </summary>
    bool ExpandCopiedFolder { get; set; }
}
