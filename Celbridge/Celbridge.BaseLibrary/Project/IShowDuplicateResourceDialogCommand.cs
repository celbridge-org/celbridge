using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Display the Duplicate Resource dialog to allow the user to enter a name for the duplicated resource.
/// </summary>
public interface IShowDuplicateResourceDialogCommand : IExecutableCommand
{
    /// <summary>
    /// Resource to duplicate.
    /// </summary>
    ResourceKey Resource { get; set; }
}
