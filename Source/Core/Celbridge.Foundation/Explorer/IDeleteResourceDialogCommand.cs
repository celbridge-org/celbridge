using Celbridge.Commands;

namespace Celbridge.Explorer;

/// <summary>
/// Display the Delete Resource dialog to allow the user to confirm deleting one or more resources.
/// </summary>
public interface IDeleteResourceDialogCommand : IExecutableCommand
{
    /// <summary>
    /// Resources to delete.
    /// </summary>
    List<ResourceKey> Resources { get; set; }
}
