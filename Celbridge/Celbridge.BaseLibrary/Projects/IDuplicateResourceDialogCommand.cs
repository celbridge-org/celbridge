using Celbridge.Commands;
using Celbridge.Resources;

namespace Celbridge.Projects;

/// <summary>
/// Display the Duplicate Resource dialog to allow the user to enter a name for the duplicated resource.
/// </summary>
public interface IDuplicateResourceDialogCommand : IExecutableCommand
{
    /// <summary>
    /// Resource to duplicate.
    /// </summary>
    ResourceKey Resource { get; set; }
}
