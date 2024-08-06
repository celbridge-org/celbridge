using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Display the Add Resource dialog to allow the user to add a new resource to the project.
/// </summary>
public interface IAddResourceDialogCommand : IExecutableCommand
{
    /// <summary>
    /// The type of resource to add.
    /// </summary>
    ResourceType ResourceType { get; set; }

    /// <summary>
    /// Resource key for the folder which will contain the new resource.
    /// </summary>
    ResourceKey DestFolderResource { get; set; }
}
