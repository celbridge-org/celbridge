using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Resources;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Delete a folder resource from the project.
/// </summary>
public interface IDeleteFolderCommand : IExecutableCommand
{
    /// <summary>
    /// Resource key for the folder to delete.
    /// </summary>
    ResourceKey ResourceKey { get; set; }
}
