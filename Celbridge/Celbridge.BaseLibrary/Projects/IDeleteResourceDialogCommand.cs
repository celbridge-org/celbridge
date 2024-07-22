using Celbridge.Commands;
using Celbridge.Resources;

namespace Celbridge.Projects;

/// <summary>
/// Display the Delete Resource dialog to allow the user to confirm deleting a resource.
/// </summary>
public interface IDeleteResourceDialogCommand : IExecutableCommand
{
    /// <summary>
    /// Resource to delete.
    /// </summary>
    ResourceKey Resource { get; set; }
}
