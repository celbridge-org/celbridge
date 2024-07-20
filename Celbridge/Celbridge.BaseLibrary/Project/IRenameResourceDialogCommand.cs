using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Display the Rename Resource dialog to allow the user to rename a resource.
/// </summary>
public interface IRenameResourceDialogCommand : IExecutableCommand
{
    /// <summary>
    /// Resource to rename.
    /// </summary>
    ResourceKey Resource { get; set; }
}
