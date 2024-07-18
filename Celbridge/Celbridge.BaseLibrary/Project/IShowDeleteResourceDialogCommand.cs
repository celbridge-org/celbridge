using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Display the Delete Resource dialog to allow the user to confirm deleting a resource.
/// </summary>
public interface IShowDeleteResourceDialogCommand : IExecutableCommand
{
    /// <summary>
    /// Resource to delete.
    /// </summary>
    ResourceKey Resource { get; set; }
}
